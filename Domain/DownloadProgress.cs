namespace MediaFetch.Api.Domain;

public sealed record DownloadProgress(int Percent, long? DownloadedBytes, long? TotalBytes);
