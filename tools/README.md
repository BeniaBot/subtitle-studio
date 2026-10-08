# tools

Put **`ffmpeg.exe`** here before building, and `build.cmd` will compress it
(LZMA through Python's `lzma` module: 100 MB -> 27 MB; without Python, Deflate: 37 MB)
and attach it to the end of the produced EXE (`build\make-payload.ps1`, `build\make-overlay.ps1`).

Download: [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) -> `ffmpeg-<version>-essentials_build.zip`
-> copy `bin\ffmpeg.exe` into this folder. Since 0.8.8: **9.0.2 essentials** (SHA-256 of the zip
`60f46726...47ba`, checked against gyan.dev's published `.sha256`).

The build **must** include `libass`, `libfribidi` and `libharfbuzz`, or Hebrew
subtitles burn in reversed. gyan.dev's essentials and full builds both have them -
check `--enable-libfribidi` in `ffmpeg -version`.

**Changing the engine changes its identity:** users then download the whole EXE once instead of
the small app-only update. Keep `build\payload\ffmpeg.pack` between releases.

`ffprobe.exe` is **not** needed — the app parses `ffmpeg -i` output instead.

Building without this file still works; the app then looks for `ffmpeg.exe` on PATH.
See [THIRD-PARTY.md](../THIRD-PARTY.md) for licensing.
