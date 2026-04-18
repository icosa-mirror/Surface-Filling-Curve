// MIT License
// Copyright 2026 Giovanni Cocco and Inria

#pragma once
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#ifdef _WIN32
    #define SFC_API __declspec(dllexport)
#else
    #define SFC_API __attribute__((visibility("default")))
#endif

// Direction field initialization mode — mirrors Stripe::Mode
//   0 = Extrinsic  : directions[] holds 3 floats/vert (x,y,z encoded as vertex color)
//   1 = Intrinsic  : directions[] holds 1 float/vert (angle from smooth field)
//   2 = Parallel   : directions[] holds 1 float/vert (angle, field parallel to borders)
//   3 = Nearest    : directions[] holds 1 float/vert (angle from nearest border)
//   4 = Printing   : directions[] holds 1 float/vert (3-D printing layers)
//
// directions may be null; the algorithm then derives directions from the mesh geometry.

// Creates and runs the full curve generation pipeline.
// Returns an opaque handle, or null on failure.
// The caller owns the handle and must free it with SFC_Destroy.
SFC_API void* SFC_Create(
    const float*    positions,          // vertCount * 3 floats  (x,y,z per vertex)
    uint32_t        vertCount,
    const uint32_t* triangles,          // triCount  * 3 indices (CCW winding)
    uint32_t        triCount,
    float           width,              // target spacing between parallel curve segments
    const float*    directions,         // nullable — see mode description above
    int             mode,               // 0..4, see above
    int             resample,           // non-zero to uniformly resample curve
    int             repulse,            // non-zero to run repulsion pass (experimental)
    int             stitch,             // non-zero to run stitching step (recommended)
    int             quiet,              // non-zero to suppress stdout
    int             maxTriangles // max mesh subdivision steps (-1 = unlimited)
);

// Requests cancellation of any in-progress SFC_Create call.
// Safe to call from any thread. The native call will return null as soon as
// it reaches the next cancellation checkpoint. Has no effect if no generation
// is running.
SFC_API void SFC_Cancel();

// Clears any pending cancellation. Only safe to call after SFC_WaitIdle confirms
// no generation is running — otherwise it races with a running thread's cancel check.
SFC_API void SFC_ResetCancel();

// Blocks until all in-progress SFC_Create calls have returned, or until timeoutMs
// milliseconds have elapsed. Returns 1 if idle (safe to ResetCancel), 0 if timed out.
// The native library persists across Unity domain reloads, so this correctly tracks
// generations that started in a previous managed domain.
SFC_API int SFC_WaitIdle(int timeoutMs);

// Registers a callback that receives every line the native library writes to stdout.
// Pass null to restore stdout and stop forwarding.
// The callback is invoked from whichever thread is running generation — keep it fast.
typedef void (*SFCLogCallback)(const char* message);
SFC_API void SFC_SetLogCallback(SFCLogCallback callback);

// Drains all log lines queued since the last call and forwards each to the
// registered callback. MUST be called from a managed (Mono-attached) thread —
// never from a native worker thread — so the callback can safely invoke
// managed APIs such as Debug.Log.
SFC_API void SFC_DrainLogs();

// Frees all resources associated with a handle returned by SFC_Create.
SFC_API void SFC_Destroy(void* handle);

// Returns the number of closed curve cycles produced.
SFC_API int SFC_GetCycleCount(void* handle);

// Returns the number of points in cycle at cycleIdx.
// Each point is a (position, normal) pair — 6 floats total.
SFC_API int SFC_GetCyclePointCount(void* handle, int cycleIdx);

// Fills outBuffer with pointCount * 6 floats for the given cycle:
//   [px, py, pz, nx, ny, nz,  px, py, pz, nx, ny, nz, ...]
// outBuffer must be at least SFC_GetCyclePointCount(handle, cycleIdx) * 6 * sizeof(float) bytes.
SFC_API void SFC_FillCycleData(void* handle, int cycleIdx, float* outBuffer);

#ifdef __cplusplus
}
#endif
