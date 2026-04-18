// MIT License
// Copyright 2026 Giovanni Cocco and Inria

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using AOT;

/// <summary>
/// Unity wrapper for the Surface Filling Curve native library.
///
/// The native library must be built from source and placed in the package's
/// Plugins folder before use. See README.md for instructions.
/// </summary>
public static class SurfaceFillingCurve
{
    private const string LibName = "surface_filling_curve";

    /// <summary>
    /// How the direction field is initialized from the optional <c>directions</c> array.
    /// </summary>
    public enum DirectionMode
    {
        /// <summary>directions[] holds 3 floats/vert (x,y,z 3-D direction vector).</summary>
        Extrinsic = 0,
        /// <summary>directions[] holds 1 float/vert (angle in [0,π] from a smooth field).</summary>
        Intrinsic = 1,
        /// <summary>directions[] holds 1 float/vert (angle, field parallel to mesh borders).</summary>
        Parallel  = 2,
        /// <summary>directions[] holds 1 float/vert (angle from nearest border).</summary>
        Nearest   = 3,
        /// <summary>directions[] holds 1 float/vert (3-D printing layers).</summary>
        Printing  = 4,
    }

    // ── Log callback ─────────────────────────────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LogCallback([MarshalAs(UnmanagedType.LPStr)] string message);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SFC_SetLogCallback(LogCallback callback);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SFC_DrainLogs();

    // Kept in a static field so the GC never collects the delegate while native
    // code holds a pointer to it.
    private static readonly LogCallback s_logCallback = OnNativeLog;

    [MonoPInvokeCallback(typeof(LogCallback))]
    private static void OnNativeLog(string message) => Debug.Log("[SFC] " + message);

    // Re-registers the callback after every domain reload and clears it before
    // the domain unloads, so the native library never holds a stale pointer.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitLogCallback()
    {
        SFC_SetLogCallback(s_logCallback);
        AppDomain.CurrentDomain.DomainUnload += (_, __) => SFC_SetLogCallback(null);
    }

    // ── Native imports ───────────────────────────────────────────────────────

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SFC_Create(
        float[]  positions, uint vertCount,
        uint[]   triangles, uint triCount,
        float    width,
        float[]  directions,
        int      mode,
        int      resample,
        int      repulse,
        int      stitch,
        int      quiet,
        int      maxTriangles);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SFC_Cancel();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SFC_ResetCancel();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SFC_WaitIdle(int timeoutMs);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SFC_Destroy(IntPtr handle);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SFC_GetCycleCount(IntPtr handle);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SFC_GetCyclePointCount(IntPtr handle, int cycleIdx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SFC_FillCycleData(IntPtr handle, int cycleIdx, float[] outBuffer);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Requests cancellation of any in-progress <see cref="Generate"/> call.
    /// Safe to call from any thread. The native call returns as soon as it
    /// reaches the next cancellation checkpoint. Has no effect if idle.
    /// </summary>
    public static void Cancel() => SFC_Cancel();

    /// <summary>
    /// Blocks until all in-progress native generation calls have returned, or
    /// until <paramref name="timeoutMs"/> ms have elapsed. Returns true if idle.
    /// The native library persists across domain reloads, so this catches generations
    /// left running by a previous managed domain.
    /// </summary>
    public static bool WaitIdle(int timeoutMs = 3000) => SFC_WaitIdle(timeoutMs) != 0;

    /// <summary>
    /// Clears any pending cancellation. Only safe after <see cref="WaitIdle"/>
    /// confirms no generation is running.
    /// </summary>
    public static void ResetCancel() => SFC_ResetCancel();

    /// <summary>
    /// A single closed curve cycle. Each index pairs a surface position with its normal.
    /// </summary>
    public struct Cycle
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
    }

    /// <summary>
    /// Generates surface-filling curves for the given triangle mesh.
    /// </summary>
    /// <param name="positions">Flat vertex position array: [x0,y0,z0, x1,y1,z1, ...]</param>
    /// <param name="vertCount">Number of vertices.</param>
    /// <param name="triangles">Flat triangle index array: [i0,i1,i2, i3,i4,i5, ...]</param>
    /// <param name="triCount">Number of triangles.</param>
    /// <param name="width">Target spacing between parallel curve segments.</param>
    /// <param name="directions">
    ///   Optional per-vertex direction hints.
    ///   Extrinsic mode: 3 floats/vert (x,y,z). All other modes: 1 float/vert (radians).
    ///   Pass null to derive directions automatically from the mesh geometry.
    /// </param>
    /// <param name="mode">How <paramref name="directions"/> is interpreted.</param>
    /// <param name="resample">Uniformly resample the output curve.</param>
    /// <param name="repulse">Run repulsion post-pass to improve spacing (experimental).</param>
    /// <param name="stitch">Run the stitching step. Disable only for debugging.</param>
    /// <param name="quiet">Suppress native stdout logging.</param>
    /// <returns>Array of closed curve cycles, each with positions and normals.</returns>
    public static Cycle[] Generate(
        float[]       positions,
        uint          vertCount,
        uint[]        triangles,
        uint          triCount,
        float         width,
        float[]       directions         = null,
        DirectionMode mode               = DirectionMode.Extrinsic,
        bool          resample           = false,
        bool          repulse            = false,
        bool          stitch             = true,
        bool          quiet              = true,
        int           maxTriangles = -1)
    {
        IntPtr handle = SFC_Create(
            positions, vertCount,
            triangles, triCount,
            width,
            directions,
            (int)mode,
            resample ? 1 : 0,
            repulse  ? 1 : 0,
            stitch   ? 1 : 0,
            quiet    ? 1 : 0,
            maxTriangles);

        // Drain log lines queued by native worker threads. Must happen here, on
        // the managed generation thread, before any managed callback fires.
        SFC_DrainLogs();

        if (handle == IntPtr.Zero)
            return null; // cancelled or failed — caller checks for null

        try
        {
            int cycleCount = SFC_GetCycleCount(handle);
            var result = new Cycle[cycleCount];

            for (int c = 0; c < cycleCount; c++)
            {
                int pointCount = SFC_GetCyclePointCount(handle, c);
                var raw = new float[pointCount * 6];
                SFC_FillCycleData(handle, c, raw);

                result[c].Positions = new Vector3[pointCount];
                result[c].Normals   = new Vector3[pointCount];

                for (int p = 0; p < pointCount; p++)
                {
                    result[c].Positions[p] = new Vector3(raw[6*p+0], raw[6*p+1], raw[6*p+2]);
                    result[c].Normals[p]   = new Vector3(raw[6*p+3], raw[6*p+4], raw[6*p+5]);
                }
            }

            return result;
        }
        finally
        {
            SFC_Destroy(handle);
        }
    }

    /// <summary>
    /// Generates surface-filling curves directly from a Unity <see cref="Mesh"/>.
    /// </summary>
    public static Cycle[] GenerateFromMesh(
        Mesh          mesh,
        float         width,
        float[]       directions = null,
        DirectionMode mode       = DirectionMode.Extrinsic,
        bool          resample   = false,
        bool          repulse    = false,
        bool          stitch     = true,
        bool          quiet      = true)
    {
        Vector3[] verts = mesh.vertices;
        int[]     tris  = mesh.triangles;

        float[] positions = new float[verts.Length * 3];
        for (int i = 0; i < verts.Length; i++)
        {
            positions[3*i+0] = verts[i].x;
            positions[3*i+1] = verts[i].y;
            positions[3*i+2] = verts[i].z;
        }

        uint[] triangles = new uint[tris.Length];
        for (int i = 0; i < tris.Length; i++)
            triangles[i] = (uint)tris[i];

        return Generate(
            positions, (uint)verts.Length,
            triangles, (uint)(tris.Length / 3),
            width, directions, mode,
            resample, repulse, stitch, quiet);
    }
}
