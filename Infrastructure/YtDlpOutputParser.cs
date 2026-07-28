using System.Globalization;
using System.Text.Json;
using MediaFetch.Api.Domain;

namespace MediaFetch.Api.Infrastructure;

public static class YtDlpOutputParser
{
    private const string ProgressPrefix = "download:";

    public static MediaMetadata ParseMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var title = GetString(root, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new YtDlpException("yt-dlp returned metadata without a title.");
        }

        TimeSpan? duration = root.TryGetProperty("duration", out var durationElement)
            && durationElement.TryGetDouble(out var durationSeconds)
                ? TimeSpan.FromSeconds(durationSeconds)
                : null;

        return new MediaMetadata(
            title,
            GetString(root, "uploader") ?? GetString(root, "channel"),
            duration,
            GetString(root, "extractor_key") ?? GetString(root, "extractor"),
            GetString(root, "thumbnail"),
            GetString(root, "webpage_url"));
    }

    public static bool TryParseProgress(string line, out DownloadProgress progress)
    {
        progress = new DownloadProgress(0, null, null);
        var markerIndex = line.IndexOf(ProgressPrefix, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var parts = line[(markerIndex + ProgressPrefix.Length)..].Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        var percentText = parts[0].Trim().TrimEnd('%');
        if (!double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return false;
        }

        progress = new DownloadProgress(
            Math.Clamp((int)Math.Round(percent), 0, 100),
            ParseNullableLong(parts[1]),
            ParseNullableLong(parts[2]));

        return true;
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    private static long? ParseNullableLong(string text) =>
        long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
