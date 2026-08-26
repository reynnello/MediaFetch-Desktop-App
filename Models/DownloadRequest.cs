namespace MediaFetch.Desktop.Models;

public sealed record DownloadRequest(
    Uri Url,
    string OutputDirectory,
    string FormatTag,
    int? VideoHeight,
    int? AudioBitrateKbps,
    double? SourceAudioBitrateKbps);
