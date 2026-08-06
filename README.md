# Clx Viewer

A lightweight, fast Windows viewer for **Clinx `.clx` chemiluminescence
captures** — an in-app image viewer plus an Explorer thumbnail extension — all
built on the [`clxcpp`](https://github.com/WizardPanda/clinx_format_cpp) C++
parser, so exported image data stays **pixel-identical** to the instrument's
own files.

## Features

- **Open & navigate** `.clx` files — multi-select Open, folder file list,
  Prev/Next, drag & drop, double-click from Explorer, single-instance.
- **Three views**, switchable live:
  - **Fluo** — fluorescence, shown **reverted** by default (western-blot look).
  - **Bright** — brightfield (white film on a black board).
  - **Merged** — the fluorescence signal is **subtracted** from the brightfield,
    so bands appear as dark marks on the bright film; uses the un-reverted
    fluorescence with the same min/max as the reverted view.
- **Dual-thumb range slider** with an **Auto** button — min/max update the image
  in real time; the numeric boxes are editable too.
- **Auto min/max**, tuned for this data:
  - *Fluo:* the background renders as a clean light gray (~234/255) instead of
    blank white, while bands stay dark and crisp.
  - *Bright:* the film renders near 90% brightness so markers stay visible, even
    when the film only covers a small part of the frame.
- **Metadata panel** — sample name, capture time, exposure, software, build
  date, per-image descriptor, filename metadata and trailer info, on demand.
- **Exports** — pixel-identical 16-bit TIFFs and processed 8-bit PNGs (see
  [Export naming](#export-naming)), with a non-blocking success toast.
- **Zoom & pan** (wheel, drag, double-click to fit) and a raw pixel-value
  readout in the status bar.
- **Explorer thumbnails** — a native `IThumbnailProvider` renders the
  fluorescence preview with no extra badge.

## Requirements

- Windows 10/11 (x64)
- VS 2022 Build Tools (MSVC + CMake + Ninja)
- .NET Framework 4.8 — preinstalled on Windows 10/11; the viewer targets it, so
  users don't need to install a separate runtime
- The `clinx_format_cpp` submodule

## Build & run

```powershell
git submodule update --init --recursive
.\tools\build.ps1          # native + WPF viewer; stages clxreader.dll next to the exe
.\tools\register.ps1       # Explorer thumbnail handler + .clx file association
.\viewer\bin\x64\Release\net48\ClxViewer.exe  <capture.clx>
```

Run `.\tools\unregister.ps1` to undo the shell integration.

### Artifacts

| Artifact | Location |
|---|---|
| Viewer | `viewer\bin\x64\Release\net48\ClxViewer.exe` |
| Native reader | `build-native\bin\clxreader.dll` (also copied next to the exe) |
| Thumbnail provider | `build-native\bin\ClinxThumbnailProvider.dll` |

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+O` | Open `.clx` files |
| `Ctrl+E` | Export the current view as PNG |
| `Ctrl+Shift+E` | Export raw 16-bit TIFF(s) |
| `Alt+E` | Choose export folder |
| `←` / `→` | Previous / next file |
| `F` / `B` / `M` | Fluo / Bright / Merged view |
| `A` | Auto min/max |
| `I` | Invert fluo colors |
| `Ctrl+W` | Close |

(A keyboard-glyph button on the toolbar shows the same cheat sheet.)

## Export naming

- **Raw TIFF** — `<stem>_<i>_16bit.tif`, one per embedded image; always
  overwritten so there are no duplicates.
- **Processed PNG** — `<stem>_<view>_min<low>_max<high>.png`, where
  `view` is `fluo`, `bf` or `merged`.

Exports go next to the `.clx` file by default, or to a folder you choose
(`Alt+E`).

## Explorer thumbnail previews

`ClinxThumbnailProvider.dll` is an in-process COM `IThumbnailProvider` that
parses the `.clx` with `clxcpp` and renders the fluorescence channel with the
same auto levels as the viewer (transparent margins). It registers under
**HKCU** (current user, no admin rights) and draws no badge of its own, so
`.clx` behaves like a normal file type — Explorer applies its usual thumbnail
icon overlay per the global **"Display file icon on thumbnails"** folder option.

## Repository layout

```
native/clxreader/       C-ABI DLL over clxcpp (clxreader.dll)
native/shellext/        COM IThumbnailProvider (ClinxThumbnailProvider.dll)
third_party/clinx_format_cpp/   clxcpp parser (git submodule)
viewer/                 WPF application (targets .NET Framework 4.8)
tools/                  build / register / unregister / refresh-thumbnails / icon scripts
assets/                 brand icon
```

The parser is pulled in as a git submodule and referenced via CMake
`add_subdirectory`, so exported pixels stay byte-identical to the instrument's
TIFFs.

## Notes

- **Thumbnail cache**: after first registration Explorer may keep showing
  generic icons until the cache is refreshed — run
  `.\tools\refresh-thumbnails.ps1` or navigate away and back in the folder.
- **Auto-level constants** live in `viewer/Services/AutoLevels.cs` and are
  mirrored in `native/shellext/thumbnail_provider.cpp` — keep them in sync.
- **Headless export** (used by build checks):
  `ClxViewer.exe --selftest-export <outdir> <file.clx>` exports raw TIFFs and
  all three views without showing the window.
- The `.clx` format is reverse-engineered (see `clinx_format_cpp/docs`); it is
  validated against real instrument samples in the clxcpp test suite.


