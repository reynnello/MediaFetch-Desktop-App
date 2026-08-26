# Third-party notices

MediaFetch invokes the following separately distributed command-line tools:

## yt-dlp

- Project: <https://github.com/yt-dlp/yt-dlp>
- License: [The Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE)
- Bundled executable licenses: [THIRD_PARTY_LICENSES.txt](https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt)

The portable package contains an unmodified official Windows executable.

## FFmpeg and FFprobe

- Project: <https://ffmpeg.org/>
- Windows build provider: <https://www.gyan.dev/ffmpeg/builds/>
- Source revision and build configuration: `licenses/FFmpeg-build-info.txt`
- License text: `licenses/FFmpeg-GPLv3.txt`

The bundled FFmpeg 8.1.2 full build is distributed under GPL version 3. FFmpeg
and FFprobe remain separate programs invoked by MediaFetch as external
processes.
