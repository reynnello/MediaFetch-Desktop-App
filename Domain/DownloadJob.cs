namespace MediaFetch.Api.Domain;

public sealed class DownloadJob
{
    private DownloadJob()
    {
    }

    public Guid Id { get; private set; }
    public string SourceUrl { get; private set; } = string.Empty;
    public DownloadMode Mode { get; private set; }
    public int? MaxHeight { get; private set; }
    public AudioFormat? AudioFormat { get; private set; }
    public DownloadStatus Status { get; private set; }
    public int ProgressPercent { get; private set; }
    public string? Title { get; private set; }
    public string? Author { get; private set; }
    public double? DurationSeconds { get; private set; }
    public string? Source { get; private set; }
    public string? ThumbnailUrl { get; private set; }
    public string? OutputPath { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static DownloadJob Create(
        string sourceUrl,
        DownloadMode mode,
        int? maxHeight,
        AudioFormat? audioFormat,
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;

        return new DownloadJob
        {
            Id = Guid.NewGuid(),
            SourceUrl = sourceUrl,
            Mode = mode,
            MaxHeight = mode is DownloadMode.Video ? maxHeight ?? 720 : null,
            AudioFormat = mode is DownloadMode.Audio
                ? audioFormat ?? global::MediaFetch.Api.Domain.AudioFormat.Mp3
                : null,
            Status = DownloadStatus.Queued,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    public void StartInspecting() => TransitionTo(DownloadStatus.Inspecting);

    public void StartDownloading(MediaMetadata metadata)
    {
        EnsureStatus(DownloadStatus.Inspecting);
        Title = metadata.Title;
        Author = metadata.Author;
        DurationSeconds = metadata.Duration?.TotalSeconds;
        Source = metadata.Source;
        ThumbnailUrl = metadata.ThumbnailUrl;
        ProgressPercent = 0;
        TransitionTo(DownloadStatus.Downloading);
    }

    public void UpdateProgress(int percent)
    {
        EnsureStatus(DownloadStatus.Downloading);
        ProgressPercent = Math.Clamp(percent, 0, 99);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(string outputPath)
    {
        EnsureStatus(DownloadStatus.Downloading);
        OutputPath = outputPath;
        ProgressPercent = 100;
        TransitionTo(DownloadStatus.Completed);
    }

    public void Fail(string error)
    {
        if (IsTerminal)
        {
            return;
        }

        Error = string.IsNullOrWhiteSpace(error) ? "Download failed." : error;
        TransitionTo(DownloadStatus.Failed);
    }

    public void Cancel()
    {
        if (IsTerminal)
        {
            return;
        }

        TransitionTo(DownloadStatus.Canceled);
    }

    public bool IsTerminal =>
        Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled;

    private void TransitionTo(DownloadStatus next)
    {
        var allowed = (Status, next) switch
        {
            (DownloadStatus.Queued, DownloadStatus.Inspecting) => true,
            (DownloadStatus.Inspecting, DownloadStatus.Downloading) => true,
            (DownloadStatus.Downloading, DownloadStatus.Completed) => true,
            (_, DownloadStatus.Failed) when !IsTerminal => true,
            (_, DownloadStatus.Canceled) when !IsTerminal => true,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException($"Cannot transition a job from {Status} to {next}.");
        }

        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureStatus(DownloadStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Expected job status {expected}, but found {Status}.");
        }
    }
}
