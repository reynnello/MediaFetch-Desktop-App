using MediaFetch.Api.Services;

namespace MediaFetch.Tests.Services;

public sealed class DownloadCancellationRegistryTests
{
    [Fact]
    public void TryCancel_CancelsRegisteredJob()
    {
        var registry = new DownloadCancellationRegistry();
        var jobId = Guid.NewGuid();
        using var source = registry.Register(jobId, CancellationToken.None);

        var found = registry.TryCancel(jobId);

        Assert.True(found);
        Assert.True(source.IsCancellationRequested);
        registry.Unregister(jobId);
    }

    [Fact]
    public void TryCancel_ReturnsFalseForUnknownJob()
    {
        var registry = new DownloadCancellationRegistry();

        Assert.False(registry.TryCancel(Guid.NewGuid()));
    }
}
