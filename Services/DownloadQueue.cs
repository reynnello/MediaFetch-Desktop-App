using System.Threading.Channels;
using MediaFetch.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaFetch.Api.Services;

public sealed class DownloadQueue : IDownloadQueue
{
    private readonly Channel<Guid> _channel;

    public DownloadQueue(IOptions<MediaFetchOptions> options)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
