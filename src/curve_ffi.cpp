// MIT License
// Copyright 2026 Giovanni Cocco and Inria

#include "curve_ffi.h"
#include "curve.h"
#include <vector>
#include <array>
#include <atomic>
#include <mutex>
#include <condition_variable>
#include <chrono>
#include <iostream>
#include <string>

// ── Log callback ──────────────────────────────────────────────────────────────
//
// The native pipeline uses Parallel::For whose worker threads are plain
// std::threads that are NOT attached to the Mono runtime. Calling managed code
// (Debug.Log) directly from those threads crashes Unity. Instead:
//   • The streambuf queues every line into g_log_queue (native, thread-safe).
//   • C# calls SFC_DrainLogs() after SFC_Create returns, on the managed thread,
//     where it is safe to invoke the callback → Debug.Log.

typedef void (*SFCLogCallback)(const char* message);
static SFCLogCallback g_log_callback  = nullptr;
static std::streambuf* g_original_cout_buf = nullptr;
static std::mutex      g_log_mutex;          // guards g_log_queue and g_log_buf_
static std::vector<std::string> g_log_queue;

// Streambuf that accumulates chars into lines and pushes completed lines into
// g_log_queue. Thread-safe: all shared state is protected by g_log_mutex.
class QueueingStreambuf : public std::streambuf {
    std::string buf_;   // partial line; protected by g_log_mutex
    void pushChar(char ch) {
        // caller holds g_log_mutex
        if (ch == '\n') {
            if (!buf_.empty()) { g_log_queue.push_back(buf_); buf_.clear(); }
        } else {
            buf_ += ch;
        }
    }
protected:
    int overflow(int c) override {
        if (c != EOF) {
            std::lock_guard<std::mutex> lk(g_log_mutex);
            pushChar(static_cast<char>(c));
        }
        return c;
    }
    std::streamsize xsputn(const char* s, std::streamsize n) override {
        std::lock_guard<std::mutex> lk(g_log_mutex);
        for (std::streamsize i = 0; i < n; ++i)
            pushChar(static_cast<char>(static_cast<unsigned char>(s[i])));
        return n;
    }
};
static QueueingStreambuf g_queueing_streambuf;

void SFC_SetLogCallback(SFCLogCallback callback)
{
    std::lock_guard<std::mutex> lk(g_log_mutex);
    g_log_callback = callback;
    if (callback && !g_original_cout_buf) {
        g_original_cout_buf = std::cout.rdbuf(&g_queueing_streambuf);
    } else if (!callback && g_original_cout_buf) {
        std::cout.rdbuf(g_original_cout_buf);
        g_original_cout_buf = nullptr;
    }
}

// Must be called from a managed thread (e.g. right after SFC_Create returns).
// Drains all queued log lines and forwards each to the registered callback.
void SFC_DrainLogs()
{
    std::vector<std::string> msgs;
    {
        std::lock_guard<std::mutex> lk(g_log_mutex);
        msgs.swap(g_log_queue);
    }
    SFCLogCallback cb = g_log_callback;
    if (cb)
        for (const auto& m : msgs)
            cb(m.c_str());
}

// Cancellation flag. Set to 1 by SFC_Cancel(); reset to 0 by SFC_ResetCancel().
// Must only be reset AFTER all active generations have stopped (see SFC_WaitIdle).
static std::atomic<int> g_sfc_cancel{0};

// Active-generation counter. Incremented at the start of SFC_Create, decremented
// at the end (whether success, failure, or exception). SFC_WaitIdle blocks until
// this reaches zero so the caller knows it is safe to call SFC_ResetCancel.
static std::atomic<int>      g_active_count{0};
static std::mutex             g_idle_mutex;
static std::condition_variable g_idle_cv;

// RAII guard that maintains the active count around a generation.
struct GenerationGuard {
    GenerationGuard()  { ++g_active_count; }
    ~GenerationGuard() {
        std::lock_guard<std::mutex> lock(g_idle_mutex);
        if (--g_active_count == 0)
            g_idle_cv.notify_all();
    }
};

// Internal handle type — caches getCycles() so callers can query it
// multiple times without re-copying the nested vectors each time.
struct SFCHandle {
    Curve curve;
    std::vector<std::vector<std::array<Vec3, 2>>> cycles;

    SFCHandle(
        const float* positions, uint32_t vertCount,
        const uint32_t* triangles, uint32_t triCount,
        float width, const float* directions,
        Stripe::Mode mode, bool resample, bool repulse, bool stitch, bool quiet,
        const std::atomic<int>* cancel, int maxTriangles)
        : curve(positions, vertCount, triangles, triCount,
                width, directions, mode, resample, repulse, stitch, quiet, cancel, maxTriangles)
        , cycles(curve.getCycles())
    {}
};

void SFC_Cancel()
{
    g_sfc_cancel.store(1, std::memory_order_seq_cst);
}

void SFC_ResetCancel()
{
    g_sfc_cancel.store(0, std::memory_order_seq_cst);
}

// Block until all in-progress SFC_Create calls have returned, or until
// timeoutMs milliseconds have elapsed. Returns 1 if idle, 0 if timed out.
int SFC_WaitIdle(int timeoutMs)
{
    std::unique_lock<std::mutex> lock(g_idle_mutex);
    bool idle = g_idle_cv.wait_for(
        lock,
        std::chrono::milliseconds(timeoutMs),
        []{ return g_active_count.load() == 0; });
    return idle ? 1 : 0;
}

void* SFC_Create(
    const float*    positions,
    uint32_t        vertCount,
    const uint32_t* triangles,
    uint32_t        triCount,
    float           width,
    const float*    directions,
    int             mode,
    int             resample,
    int             repulse,
    int             stitch,
    int             quiet,
    int             maxTriangles)
{
    GenerationGuard guard; // counts this generation; decrements on any exit
    try {
        return new SFCHandle(
            positions, vertCount,
            triangles, triCount,
            width, directions,
            static_cast<Stripe::Mode>(mode),
            resample != 0,
            repulse  != 0,
            stitch   != 0,
            quiet    != 0,
            &g_sfc_cancel,
            maxTriangles);
    } catch (...) {
        return nullptr;
    }
}

void SFC_Destroy(void* handle)
{
    delete static_cast<SFCHandle*>(handle);
}

int SFC_GetCycleCount(void* handle)
{
    if (!handle) return 0;
    return static_cast<int>(static_cast<SFCHandle*>(handle)->cycles.size());
}

int SFC_GetCyclePointCount(void* handle, int cycleIdx)
{
    if (!handle) return 0;
    const auto& cycles = static_cast<SFCHandle*>(handle)->cycles;
    if (cycleIdx < 0 || cycleIdx >= static_cast<int>(cycles.size())) return 0;
    return static_cast<int>(cycles[cycleIdx].size());
}

void SFC_FillCycleData(void* handle, int cycleIdx, float* outBuffer)
{
    if (!handle || !outBuffer) return;
    const auto& cycles = static_cast<SFCHandle*>(handle)->cycles;
    if (cycleIdx < 0 || cycleIdx >= static_cast<int>(cycles.size())) return;
    const auto& cycle = cycles[cycleIdx];
    for (int i = 0; i < static_cast<int>(cycle.size()); ++i) {
        // [0] = position, [1] = normal
        outBuffer[6*i+0] = cycle[i][0].x;
        outBuffer[6*i+1] = cycle[i][0].y;
        outBuffer[6*i+2] = cycle[i][0].z;
        outBuffer[6*i+3] = cycle[i][1].x;
        outBuffer[6*i+4] = cycle[i][1].y;
        outBuffer[6*i+5] = cycle[i][1].z;
    }
}
