# MediaFetch dependencies for Windows x64

This package contains the prerequisites used by the small, framework-dependent
MediaFetch release.

## Installation

1. Extract this archive and the MediaFetch `standard` archive into the same
   folder.
2. Open the `dotnet` folder and run the Windows Desktop Runtime installer once.
3. Keep the `tools` folder next to `MediaFetch.exe`.
4. Start `MediaFetch.exe`.

The .NET installer does not need to stay beside the application after it has
been installed. The `tools` folder must remain there because it contains
yt-dlp, FFmpeg, and FFprobe.

If the .NET 10 Windows Desktop Runtime and all three command-line tools are
already installed on the computer and available through `PATH`, this package
is optional.

Only download media you have permission to save. MediaFetch does not bypass
DRM, authentication, paywalls, or other technical protections.
