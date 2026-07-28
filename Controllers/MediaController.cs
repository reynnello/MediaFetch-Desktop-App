using MediaFetch.Api.Contracts;
using MediaFetch.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediaFetch.Api.Controllers;

[ApiController]
[Route("api/media")]
public sealed class MediaController(
    IMediaUrlValidator urlValidator,
    IYtDlpRunner runner) : ControllerBase
{
    [HttpPost("inspect")]
    [ProducesResponseType<MediaMetadataResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MediaMetadataResponse>> Inspect(
        InspectMediaRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await urlValidator.ValidateAsync(request.Url, cancellationToken);
        if (!validation.IsValid)
        {
            ModelState.AddModelError(nameof(request.Url), validation.Error!);
            return ValidationProblem(ModelState);
        }

        var metadata = await runner.InspectAsync(validation.Uri!, cancellationToken);
        return Ok(MediaMetadataResponse.FromDomain(metadata));
    }
}
