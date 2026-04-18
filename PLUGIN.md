# Unity Plugin — Build & Integration Guide

The core algorithm is implemented entirely in C++17 with no external dependencies.
The Unity plugin exposes it via a thin C FFI layer (`src/curve_ffi.h` / `src/curve_ffi.cpp`)
callable from C# through `[DllImport]` / P/Invoke, distributed as a UPM package.

---

## Package structure

```
unity/com.iota97.surface-filling-curve/
├── package.json                          # UPM manifest
├── CHANGELOG.md
├── README.md
├── Runtime/
│   ├── SurfaceFillingCurve.cs            # C# P/Invoke wrapper
│   └── SurfaceFillingCurve.Runtime.asmdef
└── Plugins/
    ├── macOS/
    │   └── libsurface_filling_curve.dylib      # built by build_plugin.sh
    ├── Windows/
    │   └── x86_64/
    │       └── surface_filling_curve.dll        # built by build_plugin.bat
    └── Linux/
        └── x86_64/
            └── libsurface_filling_curve.so      # built by build_plugin.sh
```

Native binaries are gitignored (they are build artifacts). `.meta` files are tracked
so Unity's platform assignments are preserved without manual Inspector configuration.

---

## Building the native library

No external dependencies beyond a C++17 compiler and CMake 3.10+.

### macOS / Linux — one command

```bash
./build_plugin.sh
```

This runs CMake, builds the `surface_filling_curve` shared library target, and copies
the result to the correct `Plugins/` subfolder inside the UPM package.

### Windows — one command

```bat
build_plugin.bat
```

Same as above for Windows.

### Manual build

```bash
cmake -B build_plugin -DCMAKE_BUILD_TYPE=Release
cmake --build build_plugin --target surface_filling_curve --config Release
```

Then copy the output:

| Platform | Built file | Destination |
|---|---|---|
| macOS | `build_plugin/libsurface_filling_curve.dylib` | `Plugins/macOS/` |
| Windows | `build_plugin/Release/surface_filling_curve.dll` | `Plugins/Windows/x86_64/` |
| Linux | `build_plugin/libsurface_filling_curve.so` | `Plugins/Linux/x86_64/` |

### Release / distribution builds

The default CMake flags include `-march=native`, which targets your build machine's
CPU and will crash on older hardware. For builds you intend to ship, edit
`CMakeLists.txt` and replace `-march=native`:

```cmake
# Safe x86 baseline (SSE4.2, ~2010+)
-march=x86-64-v2

# Maximum compatibility
-march=x86-64

# Apple Silicon — remove the flag entirely
```

---

## Installing in Unity

### Via UPM (recommended)

1. Open **Window → Package Manager**.
2. Click **+** → **Add package from disk…**
3. Select `unity/com.iota97.surface-filling-curve/package.json`.

Unity will import the package. If the native library has been built, it is
immediately usable. Platform assignments are pre-configured via the tracked `.meta` files.

### Via git URL (after pushing to a remote)

In `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.iota97.surface-filling-curve": "https://github.com/iota97/Surface-Filling-Curve.git?path=unity/com.iota97.surface-filling-curve"
  }
}
```

Note: native binaries are gitignored, so users installing via git URL must still run
`build_plugin.sh` / `build_plugin.bat` and copy the output manually.

---

## API reference

### C API (`src/curve_ffi.h`)

```c
// Run the full pipeline. Returns an opaque handle, or null on failure.
// Caller must free with SFC_Destroy.
void* SFC_Create(
    const float*    positions,   // vertCount * 3 floats (x,y,z per vertex)
    uint32_t        vertCount,
    const uint32_t* triangles,   // triCount * 3 indices (CCW winding)
    uint32_t        triCount,
    float           width,       // target spacing between parallel segments
    const float*    directions,  // nullable — see Direction modes below
    int             mode,        // 0=Extrinsic 1=Intrinsic 2=Parallel 3=Nearest 4=Printing
    int             resample,    // non-zero to uniformly resample output
    int             repulse,     // non-zero to run repulsion pass (experimental)
    int             stitch,      // non-zero to run stitching (recommended)
    int             quiet        // non-zero to suppress stdout
);

void SFC_Destroy(void* handle);
int  SFC_GetCycleCount(void* handle);
int  SFC_GetCyclePointCount(void* handle, int cycleIdx);

// Fills outBuffer with pointCount * 6 floats: [px,py,pz,nx,ny,nz, ...]
void SFC_FillCycleData(void* handle, int cycleIdx, float* outBuffer);
```

#### Direction modes

| Value | Name | `directions` layout |
|---|---|---|
| 0 | Extrinsic | 3 floats/vert — 3-D direction vector |
| 1 | Intrinsic | 1 float/vert — angle (rad) from a smooth field |
| 2 | Parallel  | 1 float/vert — angle from a field parallel to borders |
| 3 | Nearest   | 1 float/vert — angle from nearest border |
| 4 | Printing  | 1 float/vert — 3-D printing layer mode |

Pass `null` for `directions` to derive directions automatically from geometry.

### C# API (`Runtime/SurfaceFillingCurve.cs`)

```csharp
// From a Unity Mesh — simplest entry point
SurfaceFillingCurve.Cycle[] cycles = SurfaceFillingCurve.GenerateFromMesh(
    mesh,
    width: 0.05f,
    mode: SurfaceFillingCurve.DirectionMode.Extrinsic,
    stitch: true,
    quiet: true
);

foreach (var cycle in cycles)
{
    // cycle.Positions — Vector3[]
    // cycle.Normals   — Vector3[]
}

// From flat arrays
SurfaceFillingCurve.Cycle[] cycles = SurfaceFillingCurve.Generate(
    positions, vertCount,
    triangles, triCount,
    width: 0.05f
);
```

---

## Notes

- **Mesh requirements** — the input must be vertex and edge manifold with no vertex seams.
  Unity meshes can have seams at UV/normal discontinuities. Weld vertices before
  calling `Generate` if you see unexpected results.
- **Threading** — `SFC_Create` blocks until the full pipeline completes. Run it off
  the main thread (e.g. `await Task.Run(...)`) to avoid freezing the editor or player.
- **Repulse** — the `-R` option was not used in the paper but noticeably improves
  spacing near singularity points. Treat as experimental.
- **License** — `src/disk.cpp` is MPL v2.0 (derived from [libigl](https://github.com/libigl/libigl)).
  All other files are MIT.
