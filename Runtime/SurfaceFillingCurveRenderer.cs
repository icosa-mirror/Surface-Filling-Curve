// MIT License
// Copyright 2026 Giovanni Cocco and Inria

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// Generates surface-filling curves from the <see cref="MeshFilter"/> on this
/// GameObject and renders each closed cycle as a <see cref="LineRenderer"/>.
///
/// Generation runs on a dedicated background thread so the main thread is never
/// blocked. Results are applied automatically when the thread finishes.
///
/// The component re-generates automatically when the MeshFilter mesh is replaced.
/// Right-click the component header and choose <b>Regenerate</b> to re-run manually.
///
/// NOTE — exiting play mode while generation is in progress: Unity cannot
/// interrupt a native P/Invoke call. OnDestroy joins the thread (up to 120 s)
/// before returning so domain reload can proceed cleanly.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[AddComponentMenu("Surface Filling Curve/Surface Filling Curve Renderer")]
public class SurfaceFillingCurveRenderer : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Curve")]
    [Min(0.0001f)]
    [Tooltip("Target spacing between parallel curve segments, in local units.")]
    public float spacing = 0.3f;

    [Tooltip("How the direction field is initialised from the mesh.")]
    public SurfaceFillingCurve.DirectionMode directionMode = SurfaceFillingCurve.DirectionMode.Extrinsic;

    [Tooltip("Uniformly resample the output polyline (does not guarantee the curve stays on the surface).")]
    public bool resample = false;

    [Tooltip("Repulsion post-pass improves spacing near singularities (experimental).")]
    public bool repulse = false;

    [Tooltip("Suppress native progress output in the console. Disable to see region counts and stitching progress.")]
    public bool quiet = true;

    [Tooltip("Maximum triangle count the mesh may be subdivided to before generation. " +
             "-1 = unlimited (subdivides until edges are shorter than spacing/2). " +
             "Set to e.g. 100000 on large meshes to prevent exponential mesh inflation.")]
    public int maxTriangles = -1;

    [Header("Line Renderer")]
    [Tooltip("Shared material applied to every cycle's LineRenderer.")]
    public Material lineMaterial;

    [Min(0.00001f)]
    [Tooltip("Width of each rendered line, in local units.")]
    public float lineWidth = 0.002f;

    [Tooltip("Colour gradient applied along each cycle.")]
    public Gradient colorGradient = new Gradient();

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>True while a background generation thread is running.</summary>
    public bool IsGenerating => _thread != null && _thread.IsAlive;

    // ── Private ───────────────────────────────────────────────────────────────

    private MeshFilter _mf;
    private Mesh _watchedMesh;

    // Monotonically increasing per BeginGenerate call. The thread captures its
    // value at start and only writes results if it still matches, so a replaced
    // mesh discards the in-flight result rather than overwriting the new one.
    private int _generationId;

    private Thread _thread;
    // All threads ever started — needed so OnDestroy can join stragglers that were
    // replaced by a newer BeginGenerate before they finished.
    private readonly List<Thread> _allThreads = new List<Thread>();
    private volatile bool _destroyed;

    private SurfaceFillingCurve.Cycle[] _pendingCycles;
    private string _pendingError;
    private readonly object _pendingLock = new object();

    private readonly List<GameObject> _cycleObjects = new List<GameObject>();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Awake()
    {
        _mf = GetComponent<MeshFilter>();
    }

    // Start is called exactly once when the component first becomes active.
    // We intentionally do NOT generate in OnEnable: Unity sometimes calls
    // OnEnable → OnDisable → OnEnable during scene startup, which would spawn
    // multiple threads and produce null results as each generation races the next.
    void Start()
    {
        _watchedMesh = _mf.sharedMesh;
        Debug.Log($"[SFC] Start: mesh={((_watchedMesh != null) ? _watchedMesh.name : "null")}", this);
        if (_watchedMesh != null)
            BeginGenerate(_watchedMesh);
        else
            Debug.LogWarning("[SFC] No mesh assigned on Start.", this);
    }

    void OnDestroy()
    {
        _destroyed = true;
        SurfaceFillingCurve.Cancel();

        // Wait for ALL threads we ever started — not just the latest.
        // The native library persists across domain reloads so any thread still
        // in P/Invoke will block AppDomain.Unload until it exits.
        bool allDone = SurfaceFillingCurve.WaitIdle(5000);
        Debug.Log($"[SFC] OnDestroy: WaitIdle={allDone}", this);

        // Belt-and-suspenders: also join managed threads in case the native counter
        // is somehow off.
        foreach (var t in _allThreads)
            if (t.IsAlive) t.Join(TimeSpan.FromSeconds(1));
        _allThreads.Clear();

        ClearCycleObjects();
    }

    void Update()
    {
        // ── Watch for mesh replacement ────────────────────────────────────────
        var current = _mf.sharedMesh;
        if (current != _watchedMesh)
        {
            _watchedMesh = current;
            if (current != null)
                BeginGenerate(current);
            else
                ClearCycleObjects();
        }

        // ── Drain results from background thread ──────────────────────────────
        SurfaceFillingCurve.Cycle[] cycles;
        string error;
        lock (_pendingLock)
        {
            cycles         = _pendingCycles;
            error          = _pendingError;
            _pendingCycles = null;
            _pendingError  = null;
        }

        if (error != null)
            Debug.LogError("[SFC] " + error, this);

        if (cycles != null)
            ApplyCycles(cycles);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-runs generation using the current MeshFilter mesh and settings.
    /// Also available via right-click on the component header in the Inspector.
    /// </summary>
    [ContextMenu("Regenerate")]
    public void Regenerate()
    {
        var mesh = _mf != null ? _mf.sharedMesh : null;
        if (mesh == null)
        {
            Debug.LogWarning("[SFC] No mesh assigned.", this);
            return;
        }
        BeginGenerate(mesh);
    }

    // ── Generation ────────────────────────────────────────────────────────────

    private void BeginGenerate(Mesh mesh)
    {
        // Signal cancel to any running generation, then wait (via the native
        // active-generation counter) until it has actually stopped. Only then
        // reset the flag — this closes the race where Cancel()+ResetCancel()
        // clears the flag before the old thread sees it.
        SurfaceFillingCurve.Cancel();
        SurfaceFillingCurve.WaitIdle(3000); // generation should stop in < 100 ms
        SurfaceFillingCurve.ResetCancel();
        _allThreads.RemoveAll(t => !t.IsAlive); // prune finished threads

        int id = Interlocked.Increment(ref _generationId);
        ClearCycleObjects();

        // Capture all Unity objects on the main thread — they cannot be
        // accessed from a background thread.
        Vector3[] verts = mesh.vertices;
        int[]     tris  = mesh.triangles;

        if (verts.Length == 0 || tris.Length == 0)
        {
            Debug.LogWarning("[SFC] Mesh has no vertices or triangles.", this);
            return;
        }

        float sp   = spacing;
        var   mode = directionMode;
        bool  rs   = resample;
        bool  rp   = repulse;
        bool  qt   = quiet;
        int   mt   = maxTriangles;

        Debug.Log($"[SFC] BeginGenerate: id={id} verts={verts.Length} tris={tris.Length/3} spacing={sp} maxTriangles={mt}", this);
        _thread = new Thread(() => RunGeneration(id, verts, tris, sp, mode, rs, rp, qt, mt));
        _thread.IsBackground = true;
        _thread.Name = "SFC_Generate";
        _allThreads.Add(_thread);
        _thread.Start();
    }

    private void RunGeneration(
        int id, Vector3[] verts, int[] tris,
        float sp, SurfaceFillingCurve.DirectionMode mode, bool rs, bool rp, bool qt, int mt)
    {
        Debug.Log($"[SFC] RunGeneration started: id={id}");
        try
        {
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

            var cycles = SurfaceFillingCurve.Generate(
                positions, (uint)verts.Length,
                triangles, (uint)(tris.Length / 3),
                sp,
                directions:   null,
                mode:         mode,
                resample:     rs,
                repulse:      rp,
                stitch:       true,
                quiet:        qt,
                maxTriangles: mt);

            Debug.Log($"[SFC] RunGeneration complete: id={id} cycles={(cycles != null ? cycles.Length : -1)} destroyed={_destroyed}");
            if (_generationId == id && !_destroyed)
                lock (_pendingLock) { _pendingCycles = cycles; }
        }
        catch (DllNotFoundException)
        {
            if (_generationId == id && !_destroyed)
                lock (_pendingLock)
                {
                    _pendingError =
                        "Native library 'surface_filling_curve' could not be loaded.\n" +
                        "Run build_plugin.sh (macOS/Linux) or build_plugin.bat (Windows) " +
                        "from the repository root to build and sign the library, " +
                        "then restart Unity so it can load the updated binary.";
                }
        }
        catch (Exception ex)
        {
            if (_generationId == id && !_destroyed)
                lock (_pendingLock) { _pendingError = $"{ex.GetType().Name}: {ex.Message}"; }
        }
    }

    // ── Apply results ─────────────────────────────────────────────────────────

    private void ApplyCycles(SurfaceFillingCurve.Cycle[] cycles)
    {
        if (cycles.Length == 0)
        {
            Debug.LogWarning(
                "[SFC] Generation succeeded but produced 0 cycles. " +
                "Check that the mesh is manifold and has no vertex seams.", this);
            return;
        }

        for (int c = 0; c < cycles.Length; c++)
        {
            var cycle = cycles[c];
            if (cycle.Positions.Length < 2) continue;

            var go = new GameObject($"Cycle_{c:D3}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.hideFlags = HideFlags.DontSave;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace        = false;
            lr.loop                 = true;
            lr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows       = false;
            lr.generateLightingData = false;
            lr.startWidth           = lineWidth;
            lr.endWidth             = lineWidth;
            lr.colorGradient        = colorGradient;
            lr.positionCount        = cycle.Positions.Length;
            lr.SetPositions(cycle.Positions);

            if (lineMaterial != null)
                lr.sharedMaterial = lineMaterial;

            _cycleObjects.Add(go);
        }

        Debug.Log(
            $"[SFC] {cycles.Length} cycle(s) on \"{name}\" " +
            $"({TotalPoints(cycles)} total points).", this);
    }

    private void ClearCycleObjects()
    {
        foreach (var go in _cycleObjects)
        {
            if (go == null) continue;
#if UNITY_EDITOR
            DestroyImmediate(go);
#else
            Destroy(go);
#endif
        }
        _cycleObjects.Clear();
    }

    private static int TotalPoints(SurfaceFillingCurve.Cycle[] cycles)
    {
        int n = 0;
        foreach (var c in cycles) n += c.Positions.Length;
        return n;
    }
}
