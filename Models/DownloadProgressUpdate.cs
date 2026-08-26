namespace MediaFetch.Desktop.Models;

public sealed record DownloadProgressUpdate(
    double? Percentage,
    string Status);
