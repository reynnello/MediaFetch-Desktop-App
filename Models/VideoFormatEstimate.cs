namespace MediaFetch.Desktop.Models;

public sealed record VideoFormatEstimate(
    int Height,
    string Extension,
    long EstimatedBytes,
    bool IncludesAudio);
