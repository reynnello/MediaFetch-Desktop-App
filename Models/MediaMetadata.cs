namespace MediaFetch.Desktop.Models;

public sealed record MediaMetadata(
    string? Title,
    string? Author,
    string? ThumbnailUrl,
    string? Source,
    TimeSpan? Duration,
    IReadOnlyList<int> VideoHeights,
    IReadOnlyList<int> Mp4VideoHeights,
    IReadOnlyList<int> WebMVideoHeights,
    IReadOnlyList<VideoFormatEstimate> VideoFormatEstimates,
    string? SourceAudioCodec,
    string? SourceAudioExtension,
    double? SourceAudioBitrateKbps,
    long? SourceAudioEstimatedBytes);
