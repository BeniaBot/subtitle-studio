# tools

Put **`ffmpeg.exe`** here before building, and `build.cmd` will compress it
(194MB → 72MB) and embed it inside the produced EXE.

Download: [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) → `ffmpeg-release-full.7z`
→ copy `bin\ffmpeg.exe` into this folder.

The build **must** include `libass`, `libfribidi` and `libharfbuzz`, or Hebrew
subtitles burn in reversed. The full build has them; the essentials build may not.

`ffprobe.exe` is **not** needed — the app parses `ffmpeg -i` output instead.

Building without this file still works; the app then looks for `ffmpeg.exe` on PATH.
See [THIRD-PARTY.md](../THIRD-PARTY.md) for licensing.
