using System.IO;

namespace MediaFetch.Desktop.Services;

internal static class ExternalToolLocator
{
    private const string ToolsDirectoryName = "tools";

    public static string YtDlpExecutable => ResolveExecutable("yt-dlp.exe");

    public static string? BundledFfmpegDirectory
    {
        get
        {
            var directory = Path.Combine(AppContext.BaseDirectory, ToolsDirectoryName);

            return File.Exists(Path.Combine(directory, "ffmpeg.exe"))
                && File.Exists(Path.Combine(directory, "ffprobe.exe"))
                    ? directory
                    : null;
        }
    }

    private static string ResolveExecutable(string fileName)
    {
        var bundledPath = Path.Combine(
            AppContext.BaseDirectory,
            ToolsDirectoryName,
            fileName);

        return File.Exists(bundledPath)
            ? bundledPath
            : fileName;
    }
}
