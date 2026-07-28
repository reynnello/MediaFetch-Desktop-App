namespace MediaFetch.Api.Infrastructure;

public sealed class ExternalToolUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class YtDlpException(string message) : Exception(message);
