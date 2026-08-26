namespace MediaFetch.Desktop.Models;

public sealed record QualityOption(
    string Value,
    string Label,
    string? EstimatedSizeText = null);
