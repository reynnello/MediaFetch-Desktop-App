using MediaFetch.Api.Domain;

namespace MediaFetch.Api.Contracts;

public sealed record DownloadJobResponse(
    Guid Id,
    string Url,
    DownloadMode Mode,
    int? MaxHeight,
    AudioFormat? AudioFormat,
    DownloadStatus Status,
    int ProgressPercent,
    string? Title,
    string? Author,
    double? DurationSeconds,
    string? Source,
    string? ThumbnailUrl,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? FileUrl)
{
    public static DownloadJobResponse FromDomain(DownloadJob job) =>
        new(
            job.Id,
            job.SourceUrl,
            job.Mode,
            job.MaxHeight,
            job.AudioFormat,
            job.Status,
            job.ProgressPercent,
            job.Title,
            job.Author,
            job.DurationSeconds,
            job.Source,
            job.ThumbnailUrl,
            job.Error,
            job.CreatedAt,
            job.UpdatedAt,
            job.Status is DownloadStatus.Completed
                ? $"/api/downloads/{job.Id}/file"
                : null);
}
