using MediaFetch.Api.Domain;

namespace MediaFetch.Api.Services;

public interface IYtDlpRunner
{
    Task<MediaMetadata> InspectAsync(Uri url, CancellationToken cancellationToken);

    Task<string> DownloadAsync(
        DownloadJob job,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken);
}
