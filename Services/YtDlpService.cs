using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MediaFetch.Desktop.Models;

namespace MediaFetch.Desktop.Services;

public sealed class YtDlpService
{
    public async Task<MediaMetadata> InspectAsync(
        Uri url,
        CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(redirectStandardOutput: true);
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add(url.AbsoluteUri);

        var result = await RunAsync(startInfo, cancellationToken);
        EnsureSuccess(result, "Could not inspect link.");

        return ParseMetadata(result.StandardOutput);
    }

    public async Task DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(request.OutputDirectory);

        var separatorIndex = request.FormatTag.IndexOf(':');

        if (separatorIndex <= 0 || separatorIndex == request.FormatTag.Length - 1)
        {
            throw new ArgumentException("Invalid output format.", nameof(request));
        }

        var mode = request.FormatTag[..separatorIndex];
        var targetFormat = request.FormatTag[(separatorIndex + 1)..];
        var startInfo = CreateStartInfo(redirectStandardOutput: true);
        startInfo.ArgumentList.Add("--no-playlist");

        string outputTemplate;

        if (mode == "audio")
        {
            outputTemplate = AddAudioArguments(startInfo, request, targetFormat);
        }
        else if (mode == "video")
        {
            outputTemplate = AddVideoArguments(startInfo, request, targetFormat);
        }
        else
        {
            throw new ArgumentException("Invalid output mode.", nameof(request));
        }

        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(Path.Combine(request.OutputDirectory, outputTemplate));
        startInfo.ArgumentList.Add(request.Url.AbsoluteUri);

        var result = await RunAsync(startInfo, cancellationToken);
        EnsureSuccess(result, "Download failed.");
    }

    private static string AddAudioArguments(
        ProcessStartInfo startInfo,
        DownloadRequest request,
        string targetFormat)
    {
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("bestaudio/best");
        startInfo.ArgumentList.Add("--extract-audio");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add(targetFormat == "original" ? "best" : targetFormat);

        if (targetFormat == "original")
        {
            var bitrateLabel = request.SourceAudioBitrateKbps is null
                ? "best"
                : $"{request.SourceAudioBitrateKbps.Value:0}kbps";

            return $"%(title)s [ORIGINAL {bitrateLabel}].%(ext)s";
        }

        if (targetFormat is "flac" or "wav")
        {
            return $"%(title)s [{targetFormat.ToUpperInvariant()}].%(ext)s";
        }

        if (request.AudioBitrateKbps is null)
        {
            throw new ArgumentException("Audio bitrate is required.", nameof(request));
        }

        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add($"{request.AudioBitrateKbps.Value}K");

        return $"%(title)s [{targetFormat.ToUpperInvariant()} {request.AudioBitrateKbps.Value}kbps].%(ext)s";
    }

    private static string AddVideoArguments(
        ProcessStartInfo startInfo,
        DownloadRequest request,
        string targetFormat)
    {
        if (request.VideoHeight is null)
        {
            throw new ArgumentException("Video quality is required.", nameof(request));
        }

        var maxHeight = request.VideoHeight.Value;
        var formatSelector = targetFormat switch
        {
            "mp4" or "mov" =>
                $"bv*[height<={maxHeight}][ext=mp4]+ba[ext=m4a]/b[height<={maxHeight}][ext=mp4]",
            "webm" =>
                $"bv*[height<={maxHeight}][ext=webm]+ba[ext=webm]/b[height<={maxHeight}][ext=webm]",
            "mkv" => $"bv*[height<={maxHeight}]+ba/b[height<={maxHeight}]",
            _ => throw new ArgumentException("Unsupported video format.", nameof(request))
        };

        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add(formatSelector);
        startInfo.ArgumentList.Add("--merge-output-format");
        startInfo.ArgumentList.Add(targetFormat);
        startInfo.ArgumentList.Add("--remux-video");
        startInfo.ArgumentList.Add(targetFormat);

        return $"%(title)s [%(height)sp {targetFormat.ToUpperInvariant()}].%(ext)s";
    }

    private static MediaMetadata ParseMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("formats", out var formats)
            || formats.ValueKind != JsonValueKind.Array)
        {
            throw new YtDlpException("The source did not return a formats list.");
        }

        var videoHeights = new SortedSet<int>();
        var mp4VideoHeights = new SortedSet<int>();
        var webMVideoHeights = new SortedSet<int>();
        string? sourceAudioCodec = null;
        string? sourceAudioExtension = null;
        double? sourceAudioBitrate = null;

        foreach (var format in formats.EnumerateArray())
        {
            if (TryGetString(format, "vcodec") is { } videoCodec
                && videoCodec != "none"
                && TryGetInt32(format, "height") is { } height)
            {
                videoHeights.Add(height);

                switch (TryGetString(format, "ext"))
                {
                    case "mp4":
                        mp4VideoHeights.Add(height);
                        break;
                    case "webm":
                        webMVideoHeights.Add(height);
                        break;
                }
            }

            if (TryGetString(format, "acodec") is not { } audioCodec
                || audioCodec == "none")
            {
                continue;
            }

            var audioBitrate = TryGetDouble(format, "abr");

            if (sourceAudioCodec is null
                || (audioBitrate is not null
                    && (sourceAudioBitrate is null || audioBitrate > sourceAudioBitrate)))
            {
                sourceAudioCodec = audioCodec;
                sourceAudioExtension = TryGetString(format, "ext");
                sourceAudioBitrate = audioBitrate;
            }
        }

        var durationSeconds = TryGetDouble(root, "duration");

        return new MediaMetadata(
            Title: TryGetString(root, "title"),
            Author: TryGetString(root, "uploader") ?? TryGetString(root, "channel"),
            ThumbnailUrl: TryGetString(root, "thumbnail"),
            Source: TryGetString(root, "extractor_key") ?? TryGetString(root, "extractor"),
            Duration: durationSeconds is null ? null : TimeSpan.FromSeconds(durationSeconds.Value),
            VideoHeights: videoHeights.ToArray(),
            Mp4VideoHeights: mp4VideoHeights.ToArray(),
            WebMVideoHeights: webMVideoHeights.ToArray(),
            SourceAudioCodec: sourceAudioCodec,
            SourceAudioExtension: sourceAudioExtension,
            SourceAudioBitrateKbps: sourceAudioBitrate);
    }

    private static ProcessStartInfo CreateStartInfo(bool redirectStandardOutput)
    {
        return new ProcessStartInfo
        {
            FileName = "yt-dlp",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError = true
        };
    }

    private static async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new YtDlpException("Could not start yt-dlp.");

        var outputTask = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync()
            : Task.FromResult(string.Empty);
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            try
            {
                await Task.WhenAll(outputTask, errorTask);
            }
            catch
            {
                // The process streams may close while the process tree is being killed.
            }

            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static void EnsureSuccess(ProcessResult result, string fallbackMessage)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(result.StandardError)
            ? fallbackMessage
            : result.StandardError.Trim();

        throw new YtDlpException(message);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
                ? value
                : null;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out var value)
                ? value
                : null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

public sealed class YtDlpException(string message) : Exception(message);
