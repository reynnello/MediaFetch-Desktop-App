namespace MediaFetch.Desktop.Models;

public sealed record AppSettings(
    string? DownloadDirectory = null,
    string? LastFormatTag = "video:mp4",
    string? LastQuality = null,
    string Theme = "Dark");
