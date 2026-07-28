using MediaFetch.Api.Data;
using MediaFetch.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MediaFetch.Api.Services;

public sealed class DownloadWorker(
    IServiceScopeFactory scopeFactory,
    IDownloadQueue queue,
    IYtDlpRunner runner,
    DownloadCancellationRegistry cancellationRegistry,
    ILogger<DownloadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recoveredJobIds = await RecoverJobsAsync(stoppingToken);
        foreach (var jobId in recoveredJobIds)
        {
            await ProcessJobAsync(jobId, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessJobAsync(jobId, stoppingToken);
        }
    }

    internal async Task<IReadOnlyList<Guid>> RecoverJobsAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MediaFetchDbContext>();

        var interrupted = await dbContext.DownloadJobs
            .Where(job => job.Status == DownloadStatus.Inspecting
                || job.Status == DownloadStatus.Downloading)
            .ToListAsync(cancellationToken);

        foreach (var job in interrupted)
        {
            job.Fail("The application stopped while this job was running.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var queuedJobs = await dbContext.DownloadJobs
            .Where(job => job.Status == DownloadStatus.Queued)
            .ToListAsync(cancellationToken);
        var queuedIds = queuedJobs
            .OrderBy(job => job.CreatedAt)
            .Select(job => job.Id)
            .ToList();

        logger.LogInformation(
            "Recovered {QueuedCount} queued jobs and marked {InterruptedCount} interrupted jobs as failed.",
            queuedIds.Count,
            interrupted.Count);

        return queuedIds;
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var jobCancellation = cancellationRegistry.Register(jobId, stoppingToken);
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MediaFetchDbContext>();
        var job = await dbContext.DownloadJobs.SingleOrDefaultAsync(
            candidate => candidate.Id == jobId,
            stoppingToken);

        if (job is null || job.Status != DownloadStatus.Queued)
        {
            cancellationRegistry.Unregister(jobId);
            return;
        }

        try
        {
            job.StartInspecting();
            await dbContext.SaveChangesAsync(jobCancellation.Token);

            var metadata = await runner.InspectAsync(
                new Uri(job.SourceUrl),
                jobCancellation.Token);

            job.StartDownloading(metadata);
            await dbContext.SaveChangesAsync(jobCancellation.Token);

            var latestProgress = 0;
            var progress = new Progress<DownloadProgress>(value =>
                Interlocked.Exchange(ref latestProgress, value.Percent));

            var downloadTask = runner.DownloadAsync(job, progress, jobCancellation.Token);
            while (!downloadTask.IsCompleted)
            {
                await Task.WhenAny(
                    downloadTask,
                    Task.Delay(TimeSpan.FromSeconds(1), jobCancellation.Token));

                var currentProgress = Volatile.Read(ref latestProgress);
                if (currentProgress > job.ProgressPercent)
                {
                    job.UpdateProgress(currentProgress);
                    await dbContext.SaveChangesAsync(jobCancellation.Token);
                }
            }

            var outputPath = await downloadTask;
            job.Complete(outputPath);
            await dbContext.SaveChangesAsync(jobCancellation.Token);
            logger.LogInformation("Download job {JobId} completed.", jobId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Download job {JobId} was interrupted by application shutdown.", jobId);
        }
        catch (OperationCanceledException)
        {
            job.Cancel();
            await SaveWithoutCancellationAsync(dbContext);
            logger.LogInformation("Download job {JobId} was canceled.", jobId);
        }
        catch (Exception exception)
        {
            job.Fail(Truncate(exception.Message, 4000));
            await SaveWithoutCancellationAsync(dbContext);
            logger.LogError(exception, "Download job {JobId} failed.", jobId);
        }
        finally
        {
            cancellationRegistry.Unregister(jobId);
        }
    }

    private static async Task SaveWithoutCancellationAsync(MediaFetchDbContext dbContext)
    {
        try
        {
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // The original failure remains the useful error; persistence is logged by EF.
        }
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
