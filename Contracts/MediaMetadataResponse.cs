using MediaFetch.Api.Domain;

namespace MediaFetch.Api.Contracts;

public sealed record MediaMetadataResponse(
    string Title,
    string? Author,
    double? DurationSeconds,
    string? Source,
    string? ThumbnailUrl,
    string? CanonicalUrl)
{
    public static MediaMetadataResponse FromDomain(MediaMetadata metadata) =>
        new(
            metadata.Title,
            metadata.Author,
            metadata.Duration?.TotalSeconds,
            metadata.Source,
            metadata.ThumbnailUrl,
            metadata.CanonicalUrl);
}
