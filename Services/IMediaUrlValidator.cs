namespace MediaFetch.Api.Services;

public interface IMediaUrlValidator
{
    Task<UrlValidationResult> ValidateAsync(string url, CancellationToken cancellationToken);
}

public sealed record UrlValidationResult(bool IsValid, Uri? Uri, string? Error)
{
    public static UrlValidationResult Valid(Uri uri) => new(true, uri, null);
    public static UrlValidationResult Invalid(string error) => new(false, null, error);
}
