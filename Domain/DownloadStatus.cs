namespace MediaFetch.Api.Domain;

public enum DownloadStatus
{
    Queued,
    Inspecting,
    Downloading,
    Completed,
    Failed,
    Canceled
}
