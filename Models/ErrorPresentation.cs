namespace MediaFetch.Desktop.Models;

public sealed record ErrorPresentation(
    string Title,
    string Message,
    string Suggestion,
    string TechnicalDetails);
