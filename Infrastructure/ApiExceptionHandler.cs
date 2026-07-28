using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MediaFetch.Api.Infrastructure;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, logLevel) = exception switch
        {
            ExternalToolUnavailableException =>
                (StatusCodes.Status503ServiceUnavailable, "Required external tool is unavailable", LogLevel.Warning),
            YtDlpException =>
                (StatusCodes.Status422UnprocessableEntity, "The media could not be processed", LogLevel.Information),
            _ =>
                (StatusCodes.Status500InternalServerError, "An unexpected error occurred", LogLevel.Error)
        };

        logger.Log(logLevel, exception, "Request failed with status code {StatusCode}.", statusCode);
        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception is ExternalToolUnavailableException or YtDlpException
                    || environment.IsDevelopment()
                    || environment.IsEnvironment("Testing")
                    ? exception.Message
                    : null
            }
        });
    }
}
