# clx_viewer

A lightweight, fast Windows viewer for Clinx `.clx` chemiluminescence
captures, plus an Explorer thumbnail extension — all built on the
[`clxcpp`](https://github.com/WizardPanda/clinx_format_cpp) C++ parser.

- **Viewer** (`.NET 8` WPF): Fluo / Bright / Merged views, real-time dual-thumb
  min/max slider with Auto, curated metadata panel, raw-TIFF and processed-PNG
  exports.
- **Shell extension** (native C++): `.clx` thumbnail previews in Explorer
  (the auto-scaled fluorescence image; no badge, so it doesn't collide with
  the icon Windows overlays at the thumbnail's corner).

## Quick start

```powershell
.\build.ps1        # native (clxreader + thumbnail provider) + WPF viewer
.\register.ps1     # Explorer thumbnail handler + .clx file association
.\viewer\bin\x64\Release\net8.0-windows\ClxViewer.exe  <capture.clx>
```

See **[docs/README.md](docs/README.md)** for the full guide, the rendering /
auto-level details, export naming, and the shell-integration notes.

## Repository layout

```
native/clxreader/     C-ABI DLL over clxcpp (clxreader.dll)
native/shellext/      COM IThumbnailProvider (ClinxThumbnailProvider.dll)
third_party/clinx_format_cpp/   clxcpp parser (git submodule)
viewer/               .NET 8 WPF application
tools/                icon generator
assets/               brand icon
docs/                 documentation
```

The parser is pulled in as a git submodule
(`git submodule update --init --recursive` after cloning) and referenced via
CMake `add_subdirectory`, so exported pixels stay byte-identical to the
instrument's TIFFs.
