# Surface Filling Curve

Unity package for **Field-Aligned Surface-Filling Curve via Implicit Stitching**
([Cocco & Chermain, Eurographics 2026](https://xavierchermain.github.io/publications/surface-filling-curve)).

The native library must be built from source before use.
See [`PLUGIN.md`](../../PLUGIN.md) at the repository root for full build and integration instructions.

## Quick start

```csharp
using UnityEngine;

public class CurveExample : MonoBehaviour
{
    public float spacing = 0.05f;

    void Start()
    {
        var mesh   = GetComponent<MeshFilter>().sharedMesh;
        var cycles = SurfaceFillingCurve.GenerateFromMesh(mesh, spacing);

        foreach (var cycle in cycles)
            for (int i = 0; i < cycle.Positions.Length - 1; i++)
                Debug.DrawLine(cycle.Positions[i], cycle.Positions[i + 1],
                               Color.green, duration: 60f);
    }
}
```

## License

MIT — except `disk.cpp` in the native library which is MPL v2.0 (derived from [libigl](https://github.com/libigl/libigl)).
