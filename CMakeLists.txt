cmake_minimum_required(VERSION 3.10...4.1)
project(surface_filling_curve)
set(CMAKE_CXX_STANDARD 17)

# Core algorithm sources — everything except the CLI entry point and the FFI layer
file(GLOB_RECURSE all_sources src/*.cpp src/*.h)
list(FILTER all_sources EXCLUDE REGEX ".*/main\\.cpp$")
list(FILTER all_sources EXCLUDE REGEX ".*/curve_ffi\\.cpp$")

# ── CLI executable (unchanged behaviour) ────────────────────────────────────
add_executable(curve ${all_sources} src/main.cpp)
target_compile_options(curve PRIVATE -Wall -O3 -march=native)

# ── Shared library for Unity / .NET P/Invoke ────────────────────────────────
add_library(surface_filling_curve SHARED ${all_sources} src/curve_ffi.cpp)
target_compile_options(surface_filling_curve PRIVATE -Wall -O3 -march=native)

# Hide all symbols by default; only SFC_API-tagged symbols are exported.
# This keeps the DLL surface clean and avoids accidental C++ ABI exposure.
set_target_properties(surface_filling_curve PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
)
