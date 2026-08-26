using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using MediaFetch.Desktop.Models;

namespace MediaFetch.Desktop.Services;

public sealed class YtDlpService
{
    private const string DownloadProgressPrefix = "MEDIAFETCH_DOWNLOAD|";
    private const string PostProcessProgressPrefix = "MEDIAFETCH_POSTPROCESS|";
    private const string OutputPathPrefix = "MEDIAFETCH_FILE|";

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

    public async Task<string> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var separatorIndex = request.FormatTag.IndexOf(':');

        if (separatorIndex <= 0 || separatorIndex == request.FormatTag.Length - 1)
        {
            throw new ArgumentException("Invalid output format.", nameof(request));
        }

        var mode = request.FormatTag[..separatorIndex];
        var targetFormat = request.FormatTag[(separatorIndex + 1)..];
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var stagingDirectory = CreateStagingDirectory(outputDirectory);

        try
        {
            var startInfo = CreateStartInfo(redirectStandardOutput: true);
            startInfo.ArgumentList.Add("--no-playlist");
            AddProgressArguments(startInfo);

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
            startInfo.ArgumentList.Add(Path.Combine(stagingDirectory, outputTemplate));
            startInfo.ArgumentList.Add("--print");
            startInfo.ArgumentList.Add($"after_move:{OutputPathPrefix}%(filepath)s");
            startInfo.ArgumentList.Add(request.Url.AbsoluteUri);

            var result = await RunAsync(
                startInfo,
                cancellationToken,
                line => ReportProgress(line, progress));
            EnsureSuccess(result, "Download failed.");

            var stagedFilePath = GetDownloadedFilePath(result.StandardOutput);
            return MoveCompletedFile(stagedFilePath, stagingDirectory, outputDirectory);
        }
        finally
        {
            await DeleteStagingDirectoryAsync(stagingDirectory);
        }
    }

    private static string CreateStagingDirectory(string outputDirectory)
    {
        var directory = Path.Combine(
            outputDirectory,
            $".mediafetch-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(directory);

        try
        {
            File.SetAttributes(
                directory,
                File.GetAttributes(directory) | FileAttributes.Hidden);
        }
        catch (IOException)
        {
            // The temporary directory can still be safely used and removed.
        }
        catch (UnauthorizedAccessException)
        {
            // Hiding the directory is cosmetic and must not block a download.
        }

        return directory;
    }

    private static string MoveCompletedFile(
        string stagedFilePath,
        string stagingDirectory,
        string outputDirectory)
    {
        var fullStagingDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(stagingDirectory));
        var fullStagedFilePath = Path.GetFullPath(stagedFilePath);
        var stagingPrefix = fullStagingDirectory + Path.DirectorySeparatorChar;

        if (!fullStagedFilePath.StartsWith(
                stagingPrefix,
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullStagedFilePath))
        {
            throw new YtDlpException(
                "The download completed, but the resulting file could not be found.");
        }

        var destinationPath = Path.Combine(
            outputDirectory,
            Path.GetFileName(fullStagedFilePath));
        File.Move(fullStagedFilePath, destinationPath, overwrite: true);
        return destinationPath;
    }

    private static async Task DeleteStagingDirectoryAsync(string stagingDirectory)
    {
        const int maximumAttempts = 5;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                await Task.Delay(100);
            }
            catch (UnauthorizedAccessException) when (attempt < maximumAttempts)
            {
                await Task.Delay(100);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private static string GetDownloadedFilePath(string standardOutput)
    {
        var path = standardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(OutputPathPrefix, StringComparison.Ordinal))
            .Select(line => line[OutputPathPrefix.Length..].Trim())
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new YtDlpException(
                "The download completed, but yt-dlp did not report the saved file path.");
        }

        return Path.GetFullPath(path);
    }

    private static void AddProgressArguments(ProcessStartInfo startInfo)
    {
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add("--progress-delta");
        startInfo.ArgumentList.Add("0.2");
        startInfo.ArgumentList.Add("--progress-template");
        startInfo.ArgumentList.Add(
            $"download:{DownloadProgressPrefix}%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(progress.progress_idx)s|%(progress.max_progress)s");
        startInfo.ArgumentList.Add("--progress-template");
        startInfo.ArgumentList.Add(
            $"postprocess:{PostProcessProgressPrefix}%(progress.status)s");
    }

    private static void ReportProgress(
        string line,
        IProgress<DownloadProgressUpdate>? progress)
    {
        if (progress is null)
        {
            return;
        }

        if (line.StartsWith(DownloadProgressPrefix, StringComparison.Ordinal))
        {
            var values = line[DownloadProgressPrefix.Length..].Split('|');

            if (values.Length == 0 || !TryParsePercentage(values[0], out var percentage))
            {
                return;
            }

            if (values.Length >= 5
                && int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var progressIndex)
                && int.TryParse(values[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximumProgress)
                && maximumProgress > 1)
            {
                var completedParts = Math.Clamp(progressIndex - 1, 0, maximumProgress - 1);
                percentage = ((completedParts * 100) + percentage) / maximumProgress;
            }

            var status = "Downloading";

            if (values.Length >= 2 && IsUsefulProgressValue(values[1]))
            {
                status += $"  •  {values[1].Trim()}";
            }

            if (values.Length >= 3 && IsUsefulProgressValue(values[2]))
            {
                status += $"  •  ETA {values[2].Trim()}";
            }

            progress.Report(new DownloadProgressUpdate(
                Percentage: Math.Clamp(percentage, 0, 100),
                Status: status));
            return;
        }

        if (line.StartsWith(PostProcessProgressPrefix, StringComparison.Ordinal))
        {
            progress.Report(new DownloadProgressUpdate(null, "Processing media..."));
            return;
        }

        if (line.Contains("[Merger]", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new DownloadProgressUpdate(null, "Merging video and audio..."));
        }
        else if (line.Contains("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new DownloadProgressUpdate(null, "Converting audio..."));
        }
        else if (line.Contains("[VideoRemuxer]", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new DownloadProgressUpdate(null, "Finalizing video..."));
        }
    }

    private static bool TryParsePercentage(string value, out double percentage)
    {
        return double.TryParse(
            value.Trim().TrimEnd('%'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out percentage);
    }

    private static bool IsUsefulProgressValue(string value)
    {
        var trimmed = value.Trim();

        return trimmed.Length > 0
            && !string.Equals(trimmed, "N/A", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, "Unknown", StringComparison.OrdinalIgnoreCase);
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
        var videoFormatEstimates = new List<VideoFormatEstimate>();
        string? sourceAudioCodec = null;
        string? sourceAudioExtension = null;
        double? sourceAudioBitrate = null;
        long? sourceAudioEstimatedBytes = null;
        var durationSeconds = TryGetDouble(root, "duration");

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

                var extension = TryGetString(format, "ext");
                var estimatedBytes = EstimateFormatBytes(format, durationSeconds);

                if (extension is not null && estimatedBytes is not null)
                {
                    videoFormatEstimates.Add(new VideoFormatEstimate(
                        Height: height,
                        Extension: extension,
                        EstimatedBytes: estimatedBytes.Value,
                        IncludesAudio: TryGetString(format, "acodec") is { } includedAudioCodec
                            && includedAudioCodec != "none"));
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
                sourceAudioEstimatedBytes = EstimateFormatBytes(format, durationSeconds);
            }
        }

        return new MediaMetadata(
            Title: TryGetString(root, "title"),
            Author: TryGetString(root, "uploader") ?? TryGetString(root, "channel"),
            ThumbnailUrl: TryGetString(root, "thumbnail"),
            Source: TryGetString(root, "extractor_key") ?? TryGetString(root, "extractor"),
            Duration: durationSeconds is null ? null : TimeSpan.FromSeconds(durationSeconds.Value),
            VideoHeights: videoHeights.ToArray(),
            Mp4VideoHeights: mp4VideoHeights.ToArray(),
            WebMVideoHeights: webMVideoHeights.ToArray(),
            VideoFormatEstimates: videoFormatEstimates,
            SourceAudioCodec: sourceAudioCodec,
            SourceAudioExtension: sourceAudioExtension,
            SourceAudioBitrateKbps: sourceAudioBitrate,
            SourceAudioEstimatedBytes: sourceAudioEstimatedBytes);
    }

    private static long? EstimateFormatBytes(
        JsonElement format,
        double? durationSeconds)
    {
        var reportedBytes = TryGetDouble(format, "filesize")
            ?? TryGetDouble(format, "filesize_approx");
        var bitrateKbps = TryGetDouble(format, "tbr");

        if (bitrateKbps is null)
        {
            var videoBitrate = TryGetDouble(format, "vbr") ?? 0;
            var audioBitrate = TryGetDouble(format, "abr") ?? 0;
            bitrateKbps = videoBitrate + audioBitrate;
        }

        double? bitrateEstimatedBytes = null;

        if (durationSeconds is > 0 && bitrateKbps is > 0)
        {
            bitrateEstimatedBytes = durationSeconds.Value * bitrateKbps.Value * 1000 / 8;
        }

        var estimatedBytes = new[] { reportedBytes, bitrateEstimatedBytes }
            .Where(value => value is > 0 and <= long.MaxValue)
            .Max();

        return estimatedBytes is > 0 and <= long.MaxValue
            ? (long)Math.Ceiling(estimatedBytes.Value)
            : null;
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
        CancellationToken cancellationToken,
        Action<string>? lineObserver = null)
    {
        using var process = Process.Start(startInfo)
            ?? throw new YtDlpException("Could not start yt-dlp.");

        var outputTask = startInfo.RedirectStandardOutput
            ? ReadStreamAsync(process.StandardOutput, lineObserver)
            : Task.FromResult(string.Empty);
        var errorTask = ReadStreamAsync(process.StandardError, lineObserver);

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

    private static async Task<string> ReadStreamAsync(
        StreamReader reader,
        Action<string>? lineObserver)
    {
        var output = new StringBuilder();

        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            output.AppendLine(line);
            lineObserver?.Invoke(line);
        }

        return output.ToString();
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
