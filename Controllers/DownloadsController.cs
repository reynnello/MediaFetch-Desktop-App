using MediaFetch.Api.Contracts;
using MediaFetch.Api.Data;
using MediaFetch.Api.Domain;
using MediaFetch.Api.Options;
using MediaFetch.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaFetch.Api.Controllers;

[ApiController]
[Route("api/downloads")]
public sealed class DownloadsController(
    MediaFetchDbContext dbContext,
    IMediaUrlValidator urlValidator,
    IDownloadQueue queue,
    DownloadCancellationRegistry cancellationRegistry,
    IOptions<MediaFetchOptions> options,
    IHostEnvironment environment) : ControllerBase
{
    private static readonly HashSet<int> SupportedHeights = [360, 720, 1080];
    private readonly string _outputDirectory =
        Path.GetFullPath(options.Value.OutputDirectory, environment.ContentRootPath);

    [HttpPost]
    [ProducesResponseType<DownloadJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DownloadJobResponse>> Create(
        CreateDownloadRequest request,
        CancellationToken cancellationToken)
    {
        ValidateFormat(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var urlValidation = await urlValidator.ValidateAsync(request.Url, cancellationToken);
        if (!urlValidation.IsValid)
        {
            ModelState.AddModelError(nameof(request.Url), urlValidation.Error!);
            return ValidationProblem(ModelState);
        }

        var job = DownloadJob.Create(
            urlValidation.Uri!.AbsoluteUri,
            request.Mode,
            request.MaxHeight,
            request.AudioFormat);

        dbContext.DownloadJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(job.Id, CancellationToken.None);

        return AcceptedAtAction(
            nameof(Get),
            new { id = job.Id },
            DownloadJobResponse.FromDomain(job));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DownloadJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DownloadJobResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.DownloadJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return job is null
            ? NotFound()
            : Ok(DownloadJobResponse.FromDomain(job));
    }

    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetFile(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.DownloadJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (job is null)
        {
            return NotFound();
        }

        if (job.Status != DownloadStatus.Completed || string.IsNullOrWhiteSpace(job.OutputPath))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The download is not complete."
            });
        }

        var fullPath = Path.GetFullPath(job.OutputPath);
        var relativePath = Path.GetRelativePath(_outputDirectory, fullPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || !System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };

        return PhysicalFile(
            fullPath,
            contentType,
            $"{job.Id:N}{extension}",
            enableRangeProcessing: true);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var job = await dbContext.DownloadJobs
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (job is null)
        {
            return NotFound();
        }

        if (job.IsTerminal)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = $"A {job.Status} job cannot be canceled."
            });
        }

        if (job.Status == DownloadStatus.Queued)
        {
            job.Cancel();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (!cancellationRegistry.TryCancel(job.Id))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The job is no longer cancelable."
            });
        }

        return AcceptedAtAction(nameof(Get), new { id = job.Id });
    }

    private void ValidateFormat(CreateDownloadRequest request)
    {
        if (request.Mode == DownloadMode.Video)
        {
            if (request.MaxHeight is not null && !SupportedHeights.Contains(request.MaxHeight.Value))
            {
                ModelState.AddModelError(
                    nameof(request.MaxHeight),
                    "MaxHeight must be 360, 720, or 1080.");
            }

            if (request.AudioFormat is not null)
            {
                ModelState.AddModelError(
                    nameof(request.AudioFormat),
                    "AudioFormat can only be used in audio mode.");
            }
        }
        else if (request.MaxHeight is not null)
        {
            ModelState.AddModelError(
                nameof(request.MaxHeight),
                "MaxHeight can only be used in video mode.");
        }
    }
}
