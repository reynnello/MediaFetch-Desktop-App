using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace MediaFetch.Desktop.Services;

public sealed class ThumbnailService
{
    private const int MaxThumbnailBytes = 8 * 1024 * 1024;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<BitmapImage?> LoadAsync(
        string? thumbnailUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        using var response = await HttpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaxThumbnailBytes)
        {
            return null;
        }

        var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (imageBytes.Length == 0 || imageBytes.Length > MaxThumbnailBytes)
        {
            return null;
        }

        using var imageStream = new MemoryStream(imageBytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 320;
        image.StreamSource = imageStream;
        image.EndInit();
        image.Freeze();

        return image;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MediaFetch/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/*");
        return client;
    }
}
