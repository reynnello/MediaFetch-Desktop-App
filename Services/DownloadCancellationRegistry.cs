using System.Collections.Concurrent;

namespace MediaFetch.Api.Services;

public sealed class DownloadCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();

    public CancellationTokenSource Register(Guid jobId, CancellationToken stoppingToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!_tokens.TryAdd(jobId, source))
        {
            source.Dispose();
            throw new InvalidOperationException($"Job {jobId} is already registered.");
        }

        return source;
    }

    public bool TryCancel(Guid jobId)
    {
        if (!_tokens.TryGetValue(jobId, out var source))
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    public void Unregister(Guid jobId)
    {
        if (_tokens.TryRemove(jobId, out var source))
        {
            source.Dispose();
        }
    }
}
