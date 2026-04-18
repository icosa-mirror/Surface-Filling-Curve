cmake_minimum_required(VERSION 3.10...4.1)
project(surface_filling_curve)
set(CMAKE_CXX_STANDARD 17)

# Core algorithm sources — everything except the CLI entry point and the FFI layer
file(GLOB_RECURSE all_sources src/*.cpp src/*.h)
list(FILTER all_sources EXCLUDE REGEX ".*/main\\.cpp$")
list(FILTER all_sources EXCLUDE REGEX ".*/curve_ffi\\.cpp$")

# iOS and Android only build the plugin — no CLI executable on those platforms.
if(NOT CMAKE_SYSTEM_NAME MATCHES "iOS|Android")
    add_executable(curve ${all_sources} src/main.cpp)
    target_compile_options(curve PRIVATE -Wall -O3 -march=native)
endif()

# ── Shared library for Unity / .NET P/Invoke (desktop + Android) ────────────
# ── Static library for iOS (dynamic libs are not permitted in iOS apps) ──────
if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    add_library(surface_filling_curve STATIC ${all_sources} src/curve_ffi.cpp)
else()
    add_library(surface_filling_curve SHARED ${all_sources} src/curve_ffi.cpp)
endif()

if(NOT CMAKE_SYSTEM_NAME STREQUAL "iOS")
    # -march=native is meaningless when cross-compiling; the toolchain sets the right arch.
    target_compile_options(surface_filling_curve PRIVATE -Wall -O3 -march=native)
else()
    target_compile_options(surface_filling_curve PRIVATE -Wall -O3)
endif()

set_target_properties(surface_filling_curve PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
)
