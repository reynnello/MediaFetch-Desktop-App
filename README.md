# MediaFetch

MediaFetch is a Windows desktop application for inspecting and downloading
public video or audio through a simple WPF interface.

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Windows](https://img.shields.io/badge/Windows-x64-0078D4)

## Features

- Automatically inspects a public media link and displays its source,
  thumbnail, title, author, duration, and available qualities.
- Downloads MP4, MKV, WebM, and MOV video.
- Extracts original audio or converts it to MP3, M4A, Opus, FLAC, or WAV.
- Shows real download progress and approximate output sizes.
- Supports cancellation and removes unfinished temporary files.
- Opens a completed file or reveals it in File Explorer.
- Includes light and dark themes and remembers user settings.
- Supports YouTube as the primary source and other public links supported by
  yt-dlp on a best-effort basis.

MediaFetch does not bypass DRM, authentication, paywalls, or other technical
protections. Only download media you have permission to save.

## Download and run

1. Download `MediaFetch-1.0.0-win-x64-portable.zip` from GitHub Releases.
2. Extract the entire archive. Do not run the application from inside the ZIP.
3. Launch `MediaFetch.exe`.

The portable package is self-contained. Users do **not** need to install .NET,
yt-dlp, FFmpeg, FFprobe, or Python. Keep the `tools` folder next to the
application executable.

Requirements:

- A supported 64-bit edition of Windows 10 or Windows 11.
- An internet connection.
- Write access to the selected download folder.

## Development

Prerequisites:

- .NET 10 SDK.
- Current `yt-dlp`, `ffmpeg`, and `ffprobe` executables available through PATH.

Run the application:

```powershell
dotnet run
```

Create a portable Windows x64 package:

```powershell
.\scripts\Publish-Release.ps1 -Version 1.0.0
```

The archive is written to `artifacts/`. The publish script copies the locally
installed command-line tools into the package; their versions therefore match
the build machine.

## Technology

- C# 14 and .NET 10
- WPF
- yt-dlp
- FFmpeg and FFprobe

Third-party licensing information is available in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
