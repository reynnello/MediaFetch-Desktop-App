using MediaFetch.Api.Data;
using MediaFetch.Api.Domain;
using MediaFetch.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFetch.Tests.Services;

public sealed class DownloadWorkerRecoveryTests
{
    [Fact]
    public async Task RecoverJobs_ReturnsQueuedAndFailsInterruptedJobs()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<MediaFetchDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        Guid queuedId;
        Guid interruptedId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MediaFetchDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            var queued = DownloadJob.Create(
                "https://example.com/queued",
                DownloadMode.Video,
                720,
                null);
            var interrupted = DownloadJob.Create(
                "https://example.com/interrupted",
                DownloadMode.Video,
                720,
                null);
            interrupted.StartInspecting();

            queuedId = queued.Id;
            interruptedId = interrupted.Id;
            dbContext.DownloadJobs.AddRange(queued, interrupted);
            await dbContext.SaveChangesAsync();
        }

        var worker = new DownloadWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new NoOpQueue(),
            new NoOpRunner(),
            new DownloadCancellationRegistry(),
            NullLogger<DownloadWorker>.Instance);

        var recovered = await worker.RecoverJobsAsync(CancellationToken.None);

        Assert.Equal([queuedId], recovered);

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<MediaFetchDbContext>();
        var interruptedJob = await verificationDb.DownloadJobs
            .SingleAsync(job => job.Id == interruptedId);
        Assert.Equal(DownloadStatus.Failed, interruptedJob.Status);
    }

    private sealed class NoOpQueue : IDownloadQueue
    {
        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
            ValueTask.FromCanceled<Guid>(cancellationToken);
    }

    private sealed class NoOpRunner : IYtDlpRunner
    {
        public Task<MediaMetadata> InspectAsync(Uri url, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> DownloadAsync(
            DownloadJob job,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
