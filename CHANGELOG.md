# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] - 2026-04-11

### Added
- Initial release.
- `SurfaceFillingCurve.GenerateFromMesh` — generate curves directly from a Unity `Mesh`.
- `SurfaceFillingCurve.Generate` — generate curves from flat vertex/triangle arrays.
- Native C FFI layer (`curve_ffi.h`) exposing the C++ algorithm via P/Invoke.
- Platform plugin stubs for macOS, Windows x86_64, and Linux x86_64.
