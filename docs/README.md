# Clx Viewer

A lightweight, fast Windows viewer for **Clinx `.clx` chemiluminescence
captures**, with an Explorer thumbnail extension. Built on the
[`clxcpp`](https://github.com/WizardPanda/clinx_format_cpp) C++ parser (pulled
in as a git submodule) so image data stays **pixel-identical** to the
instrument's own exports.

```
clx_viewer/
  CMakeLists.txt              native build (clxreader + shellext; adds third_party/clinx_format_cpp)
  build.ps1                   one-command build
  register.ps1 / unregister.ps1   Explorer integration (HKCU, no admin)
  assets/clxviewer.ico        brand icon (rounded #012c5d "Clx")
  native/clxreader/           C-ABI DLL over clxcpp  -> clxreader.dll
  native/shellext/            IThumbnailProvider COM DLL -> ClinxThumbnailProvider.dll
  viewer/                     .NET 8 WPF app          -> ClxViewer.exe
  tools/make-icon.ps1         regenerates assets/clxviewer.ico
  docs/                       this document
```

## What it does

* **Open & navigate** `.clx` files: multi-select Open, folder file list, Prev /
  Next, drag & drop, double-click from Explorer, single-instance.
  ([Screenshot](viewer-screenshot.png).)
* **Three views** of the capture, switchable live:
  * **Fluo** — fluorescence, shown **reverted by default** (western-blot look).
  * **Bright** — brightfield.
  * **Merged** — the fluorescence signal is **subtracted** from the
    brightfield, so bands appear as dark marks on the bright film; uses the
    brightfield and the **un-reverted** fluorescence with the same min/max you
    set for the reverted view.
* **Dual-thumb range slider** with an **Auto** button. Min/max update the image
  in real time; the numeric boxes are editable too. (Shortcut: `A`.)
* **Auto min/max** tuned for this data:
  * *Fluo:* noise floor at the 1st percentile, band top near the 99.9th
    percentile, plus a top-end gain so the background renders a clean light
    gray (~234/255) instead of blank white, while bands stay dark and crisp.
  * *Bright:* the "white film on a black board" — film level at the 98th
    percentile rendered near 90% brightness so the marker lines stay visible,
    even when the film covers only a small part of the frame.
* **Metadata panel** (toggle): curated fields — sample name, capture time,
  exposure, software, build date, file size, per-image descriptor
  (dims/bit-depth/min/max/type/channel), filename metadata, trailer info
  (`Gray.pal`, exposure match).
* **Exports** (default: next to the `.clx`, or to a folder you pick with the
  folder button):
  * **Raw TIFF** — pixel-identical 16-bit TIFFs, one per embedded image,
    named `<stem>_<i>_16bit.tif` (always overwritten, so no duplicates).
  * **Processed PNG** — the current view rendered with the current min/max,
    named `<stem>_<view>_min<low>_max<high>.png` (`view` = fluo/bf/merged).
* **Zoom & pan** the image (wheel zoom around the cursor, drag to pan,
  double-click to fit, `F` to fit, `+`/`-` to zoom). The status bar shows the
  raw pixel value under the cursor.

## Explorer thumbnail previews

The bundled `ClinxThumbnailProvider.dll` is an in-process COM
`IThumbnailProvider` that parses the `.clx` with `clxcpp` and renders the
fluorescence channel with the same auto levels as the viewer (western-blot
look, transparent margins). It registers under **HKCU** (current user, no
admin rights).

The provider draws no badge of its own, and no `TypeOverlay` exemption is
registered, so `.clx` behaves like a normal file type: Explorer applies its
usual thumbnail icon overlay per the global **"Display file icon on
thumbnails"** folder option.

## Building

Requirements: VS 2022 Build Tools (MSVC + CMake + Ninja), .NET 8 SDK, and the
`clinx_format_cpp` submodule checked out
(`git submodule update --init --recursive`).

```powershell
.\build.ps1             # native + viewer; stages clxreader.dll next to the exe
.\register.ps1          # thumbnail handler + .clx file association
```

Artifacts:

| Artifact | Location |
|---|---|
| Viewer | `viewer\bin\x64\Release\net8.0-windows\ClxViewer.exe` |
| Native reader | `build-native\bin\clxreader.dll` (also copied next to the exe) |
| Thumbnail provider | `build-native\bin\ClinxThumbnailProvider.dll` |

Run the viewer: `ClxViewer.exe <files.clx …>` (or open/drop).

To undo shell integration: `.\unregister.ps1`.

## Notes

* **Thumbnail cache**: the first time you register, Explorer may keep showing
  generic icons until the thumbnail cache is refreshed. Kill Explorer, delete
  `%LocalAppData%\Microsoft\Windows\Explorer\thumbcache_*.db`, restart it, or
  just navigate away and back in the folder.
* **Auto-level constants** live in `viewer/Services/AutoLevels.cs` and are
  mirrored in `native/shellext/thumbnail_provider.cpp` — keep them in sync.
* **Headless export** (used by the build checks): 
  `ClxViewer.exe --selftest-export <outdir> <file.clx>` exports raw TIFFs and
  all three views without showing the window.
* The `.clx` format is reverse-engineered (see `clinx_format_cpp/docs`); it is
  validated against the two real instrument samples in the test suite.
