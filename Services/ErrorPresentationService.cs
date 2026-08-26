using MediaFetch.Desktop.Models;

namespace MediaFetch.Desktop.Services;

public sealed class ErrorPresentationService
{
    public ErrorPresentation Create(
        Exception exception,
        Uri? sourceUrl,
        string fallbackTitle)
    {
        var details = BuildTechnicalDetails(exception, sourceUrl);
        var message = exception.Message;
        var host = sourceUrl?.Host ?? string.Empty;

        if (host.Contains("spotify.com", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorPresentation(
                Title: "Spotify links aren't downloadable",
                Message: "Spotify does not expose a public media stream that MediaFetch can save. Its links require Spotify playback services and API access.",
                Suggestion: "Use a direct public media link from a supported source. MediaFetch does not bypass Spotify access controls or DRM.",
                TechnicalDetails: details);
        }

        if (ContainsAny(message, "DRM", "protected content", "only images are available"))
        {
            return new ErrorPresentation(
                Title: "This media is protected",
                Message: "The source uses DRM or another technical protection, so MediaFetch cannot access the media stream.",
                Suggestion: "Try different public content that does not require DRM, a subscription, or protected playback.",
                TechnicalDetails: details);
        }

        if (ContainsAny(
                message,
                "login required",
                "sign in",
                "authentication",
                "cookies",
                "private video",
                "private account"))
        {
            return new ErrorPresentation(
                Title: "Sign-in is required",
                Message: "The source does not allow anonymous access to this media.",
                Suggestion: "Check that the link is public and opens without signing in. Private or account-only media is not supported yet.",
                TechnicalDetails: details);
        }

        if (ContainsAny(message, "HTTP Error 403", "403: Forbidden", "Access denied"))
        {
            return new ErrorPresentation(
                Title: "The source rejected the request",
                Message: "The website returned HTTP 403 Forbidden while MediaFetch was requesting the media stream.",
                Suggestion: "Confirm that the link opens publicly, update yt-dlp, and try again later. Some websites temporarily block automated requests.",
                TechnicalDetails: details);
        }

        if (ContainsAny(message, "HTTP Error 429", "Too Many Requests", "rate limit"))
        {
            return new ErrorPresentation(
                Title: "The source is rate-limiting requests",
                Message: "The website is temporarily refusing requests because too many were made in a short period.",
                Suggestion: "Wait a few minutes before trying again. Repeated retries can extend the temporary block.",
                TechnicalDetails: details);
        }

        if (ContainsAny(message, "Unsupported URL", "No suitable extractor"))
        {
            return new ErrorPresentation(
                Title: "This website isn't supported",
                Message: "yt-dlp does not recognize this link as a downloadable public media source.",
                Suggestion: "Check the URL or try a direct link to the video or audio instead of a profile, playlist, search, or share page.",
                TechnicalDetails: details);
        }

        if (ContainsAny(
                message,
                "Requested format is not available",
                "No video formats found",
                "format is not available"))
        {
            return new ErrorPresentation(
                Title: "That format isn't available",
                Message: "The selected quality or output format is no longer available for this link.",
                Suggestion: "Check the link again and select one of the refreshed quality options.",
                TechnicalDetails: details);
        }

        if (ContainsAny(message, "Video unavailable", "media is unavailable", "has been removed"))
        {
            return new ErrorPresentation(
                Title: "Media is unavailable",
                Message: "The source reports that this media was removed, restricted, or is not available in your region.",
                Suggestion: "Open the link in your browser to confirm that it is still publicly playable.",
                TechnicalDetails: details);
        }

        if (ContainsAny(
                message,
                "not available in your country",
                "not available in your region",
                "geo restricted"))
        {
            return new ErrorPresentation(
                Title: "This media is region-restricted",
                Message: "The source does not make this media available in your current region.",
                Suggestion: "Open the link in your browser to confirm its regional availability.",
                TechnicalDetails: details);
        }

        if (ContainsAny(
                message,
                "No space left",
                "disk full",
                "not enough space on the disk"))
        {
            return new ErrorPresentation(
                Title: "Not enough storage space",
                Message: "The download could not finish because the destination drive is full.",
                Suggestion: "Free some space or choose a download folder on another drive.",
                TechnicalDetails: details);
        }

        if (ContainsAny(
                message,
                "Unable to download webpage",
                "timed out",
                "Temporary failure",
                "Name or service not known",
                "connection"))
        {
            return new ErrorPresentation(
                Title: "Connection failed",
                Message: "MediaFetch could not reach the source or the request timed out.",
                Suggestion: "Check your internet connection and try the link again in a moment.",
                TechnicalDetails: details);
        }

        if (ContainsAny(message, "ffmpeg", "ffprobe"))
        {
            return new ErrorPresentation(
                Title: "FFmpeg is required",
                Message: "MediaFetch needs FFmpeg to merge streams or convert the selected output format.",
                Suggestion: "Install FFmpeg, add it to PATH, and restart MediaFetch.",
                TechnicalDetails: details);
        }

        if (exception is System.ComponentModel.Win32Exception || ContainsAny(
                message,
                "Could not start yt-dlp",
                "The system cannot find the file specified"))
        {
            return new ErrorPresentation(
                Title: "yt-dlp could not run",
                Message: "MediaFetch could not start the yt-dlp process.",
                Suggestion: "Confirm that the latest yt-dlp build is installed and available in PATH.",
                TechnicalDetails: details);
        }

        return new ErrorPresentation(
            Title: fallbackTitle,
            Message: "MediaFetch could not complete this operation.",
            Suggestion: "Try the link again. Open technical details below if the problem continues.",
            TechnicalDetails: details);
    }


    // debugging helpers
    private static string BuildTechnicalDetails(Exception exception, Uri? sourceUrl)
    {
        var source = sourceUrl is null
            ? string.Empty
            : $"Source: {sourceUrl.AbsoluteUri}{Environment.NewLine}{Environment.NewLine}";

        return $"{source}{exception.GetType().Name}: {exception.Message}";
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate =>
            value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}
