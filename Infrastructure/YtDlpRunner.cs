using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using MediaFetch.Api.Domain;
using MediaFetch.Api.Options;
using MediaFetch.Api.Services;
using Microsoft.Extensions.Options;

namespace MediaFetch.Api.Infrastructure;

public sealed class YtDlpRunner(
    IOptions<MediaFetchOptions> options,
    IHostEnvironment environment,
    ILogger<YtDlpRunner> logger) : IYtDlpRunner
{
    private readonly MediaFetchOptions _options = options.Value;
    private readonly string _outputDirectory =
        Path.GetFullPath(options.Value.OutputDirectory, environment.ContentRootPath);

    public async Task<MediaMetadata> InspectAsync(Uri url, CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "--no-config",
            "--no-playlist",
            "--no-warnings",
            "--skip-download",
            "--dump-single-json",
            url.AbsoluteUri
        };

        var result = await RunProcessAsync(arguments, null, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw BuildYtDlpException("Could not inspect the media URL.", result.StandardError);
        }

        try
        {
            return YtDlpOutputParser.ParseMetadata(result.StandardOutput);
        }
        catch (Exception exception) when (exception is not YtDlpException)
        {
            throw new YtDlpException($"Could not parse yt-dlp metadata: {exception.Message}");
        }
    }

    public async Task<string> DownloadAsync(
        DownloadJob job,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_outputDirectory);

        var outputTemplate = Path.Combine(_outputDirectory, $"{job.Id:N}.%(ext)s");
        var arguments = BuildDownloadArguments(job, outputTemplate);
        var result = await RunProcessAsync(
            arguments,
            line =>
            {
                if (YtDlpOutputParser.TryParseProgress(line, out var parsed))
                {
                    progress.Report(parsed);
                }
            },
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw BuildYtDlpException("The media download failed.", result.StandardError);
        }

        var outputPath = Directory
            .EnumerateFiles(_outputDirectory, $"{job.Id:N}.*", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (outputPath is null)
        {
            throw new YtDlpException("yt-dlp finished without producing an output file.");
        }

        var fileInfo = new FileInfo(outputPath);
        if (fileInfo.Length > _options.MaxFileSizeBytes)
        {
            File.Delete(outputPath);
            throw new YtDlpException("The downloaded file exceeded the configured size limit.");
        }

        return fileInfo.FullName;
    }

    private IReadOnlyList<string> BuildDownloadArguments(DownloadJob job, string outputTemplate)
    {
        var arguments = new List<string>
        {
            "--no-config",
            "--no-playlist",
            "--newline",
            "--progress-template",
            "download:%(progress._percent_str)s|%(progress.downloaded_bytes)s|%(progress.total_bytes_estimate)s",
            "--max-filesize",
            _options.MaxFileSizeBytes.ToString(),
            "--output",
            outputTemplate
        };

        if (!string.Equals(_options.FfmpegPath, "ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("--ffmpeg-location");
            arguments.Add(_options.FfmpegPath);
        }

        switch (job.Mode)
        {
            case DownloadMode.Video:
                var height = job.MaxHeight ?? 720;
                arguments.Add("--format");
                arguments.Add($"bestvideo[height<={height}]+bestaudio/best[height<={height}]");
                arguments.Add("--merge-output-format");
                arguments.Add("mp4");
                arguments.Add("--remux-video");
                arguments.Add("mp4");
                break;

            case DownloadMode.Audio:
                arguments.Add("--extract-audio");
                arguments.Add("--audio-format");
                arguments.Add((job.AudioFormat ?? AudioFormat.Mp3).ToString().ToLowerInvariant());
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(job.Mode));
        }

        arguments.Add(job.SourceUrl);
        return arguments;
    }

    private async Task<ProcessResult> RunProcessAsync(
        IEnumerable<string> arguments,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _options.YtDlpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new ExternalToolUnavailableException("Could not start yt-dlp.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new ExternalToolUnavailableException(
                $"yt-dlp was not found at '{_options.YtDlpPath}'. Install it or update MediaFetch:YtDlpPath.",
                exception);
        }

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        var outputTask = ReadLinesAsync(
            process.StandardOutput,
            standardOutput,
            onOutputLine,
            cancellationToken);
        var errorTask = ReadLinesAsync(
            process.StandardError,
            standardError,
            onOutputLine,
            cancellationToken);

        try
        {
            await Task.WhenAll(
                outputTask,
                errorTask,
                process.WaitForExitAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }

        logger.LogDebug("yt-dlp exited with code {ExitCode}.", process.ExitCode);
        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        StringBuilder capture,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (capture.Length < 1_000_000)
            {
                capture.AppendLine(line);
            }

            onLine?.Invoke(line);
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process exited between the check and Kill.
        }
    }

    private static YtDlpException BuildYtDlpException(string prefix, string standardError)
    {
        var detail = standardError
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        return new YtDlpException(detail is null ? prefix : $"{prefix} {detail}");
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
