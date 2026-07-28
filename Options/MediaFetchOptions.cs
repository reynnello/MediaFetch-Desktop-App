using System.ComponentModel.DataAnnotations;

namespace MediaFetch.Api.Options;

public sealed class MediaFetchOptions
{
    public const string SectionName = "MediaFetch";

    [Required]
    public string OutputDirectory { get; init; } = "downloads";

    [Required]
    public string YtDlpPath { get; init; } = "yt-dlp";

    [Required]
    public string FfmpegPath { get; init; } = "ffmpeg";

    [Range(1, long.MaxValue)]
    public long MaxFileSizeBytes { get; init; } = 1_073_741_824;

    [Range(1, 1000)]
    public int QueueCapacity { get; init; } = 100;
}
