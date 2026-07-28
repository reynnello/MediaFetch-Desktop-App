namespace MediaFetch.Api.Domain;

public sealed record MediaMetadata(
    string Title,
    string? Author,
    TimeSpan? Duration,
    string? Source,
    string? ThumbnailUrl,
    string? CanonicalUrl);
