# 01 — Architecture

## Solution layout

```
NormalBake.sln
├─ src/
│  ├─ NormalBake.Core/             # the library (no IO/CLI deps in the algorithm core)
│  │  ├─ Math/                     # Vector2/3 helpers, frames, SIMD utilities
│  │  ├─ Mesh/                     # Mesh POCOs, deterministic triangulation, attributes
│  │  ├─ Tangents/                 # MikkTSpace-faithful per-vertex tangent basis (clean-room port)
│  │  ├─ Acceleration/             # BVH (binned-SAH + SBVH), Woop watertight ray-triangle, packets
│  │  ├─ Baking/                   # UV rasterizer, cage/envelope, projection, sampling, SSAA, dilation
│  │  ├─ Encoding/                 # normal<->RGB, Y-flip, 8/16-bit, BC5/BC7
│  │  └─ NormalBaker.cs            # top-level orchestrator + NormalBakeOptions
│  ├─ NormalBake.IO/               # OBJ (with UVs) reader/writer; PNG/EXR/DDS image writers
│  └─ NormalBake.Cli/              # `normalbake high.obj low.obj -o normal.png ...`
└─ tests/
   ├─ NormalBake.Tests/            # unit + golden-image tests
   └─ corpus/                      # shared mesh pairs + committed goldens (tests/golden/)
```

`NormalBake.Core` keeps a **dependency-light** runtime graph: the BVH, tangent basis, rasterizer, and
encoding are in-repo; only pure-managed, MIT-clean packages enter `NormalBake.IO` (image/BCn I/O —
BCnEncoder.NET, the stb ports, Pfim, Jither.OpenEXR). No native dependencies anywhere
([../Documents/08-prior-art-tools-and-libraries.md](../Documents/08-prior-art-tools-and-libraries.md) §3).

## Core data structures

### Mesh (`Mesh/`)
Index-based, struct-of-arrays POCOs for the two inputs:
```
Mesh (high-poly)         : Vector3[] positions, normals;  int[] indices            // smooth normals required
UvMesh (low-poly)        : Vector3[] positions, normals;  Vector2[] uvs;
                           Vector4[] tangents (xyz + w);  int[] indices            // UVs required (validated)
```
The low-poly is **triangulated once**, shortest-diagonal & MikkTSpace-consistent, and that single
triangulation feeds both the tangent solver and the UV rasterizer (a mismatch causes X-shaped seams —
[../Documents/07-quality-artifacts-and-aa.md](../Documents/07-quality-artifacts-and-aa.md) §3.3).

### BVH (`Acceleration/`)
A compact, cache-friendly BVH over the high-poly, SoA node layout for single-load SIMD box tests:
```
BvhNode (SoA arrays)  : min/max per child (quantized for wide nodes), child/first-prim indices
Triangle refs         : sorted; leaves index into a flat primitive array
Query                 : closest-hit (normal pass) + any-hit/occlusion (AO/thickness, P6)
```
Built once (binned-SAH default; SBVH "high quality" option), queried `width·height·samples` times with
short, cage-bounded `t_max`. **Woop watertight** ray-triangle intersection, two-sided
([../Documents/06-acceleration-and-performance.md](../Documents/06-acceleration-and-performance.md)).

### Bake buffers (`Baking/`)
```
RgbF[] accum          : per-texel running sum of tangent-space normals (averaged over supersamples)
byte[] coverage       : per-texel hit mask  (resolve AA over hits only; dilate from covered region)
```

## Pipeline orchestration (`NormalBaker.cs`)

```csharp
public static class NormalBaker
{
    public static BakedMap Bake(Mesh highPoly, UvMesh lowPoly, NormalBakeOptions options);
}
```

Internal flow (maps 1:1 to phases):
```
(highPoly, lowPoly, options)
  → ValidateUvContract(lowPoly)                 (P1)   non-overlap, seams@hard-edges, gutter ≥ 2×pad
  → Triangulate(lowPoly) + MikkTSpace tangents  (P2)
  → BuildBvh(highPoly)                          (P3)
  → for each UV texel (SSAA sub-grid):          (P4)
        recover P,N,T,w  →  cage ray  →  closest front hit
        → sample HP smooth normal  →  to tangent frame  →  encode (Y-flip)  →  accumulate
  → ResolveSamples over coverage  →  renormalize  →  Extend-dilate   (P4)
  → Encode(16-bit PNG | EXR | DDS-BC5)  +  emit tangents             (P4)
  → BakedMap
```
Later maps (AO, curvature, position, ID, thickness, bent normals) reuse the same ray core and slot in
behind the same orchestrator (P6).

## Key interfaces (stable seams for testing & swaps)

```csharp
interface IRayTracer    { bool ClosestHit(in Ray ray, float tMax, out Hit hit); bool AnyHit(in Ray ray, float tMax); }
interface ITangentSpace { void Compute(UvMesh mesh); }          // MikkTSpace-faithful
interface IProjectionCage { Ray RayFor(int texelU, int texelV, in SurfacePoint sp); }  // distance-env or cage mesh
interface IMapEncoder   { void Write(ReadOnlySpan<RgbF> texels, NormalBakeOptions o, Stream dst); }
```

These seams let us validate each piece in isolation against reference data (MikkTSpace vs the C
reference; the BVH vs brute force; encoders round-trip), and swap the distance-envelope cage for an
explicit cage mesh, or add new map types, without touching the others.

## Determinism & parallelism

- The per-texel pass is **embarrassingly parallel**: partition texels/tiles across threads; the BVH is
  read-only during the bake. Trace **UV tiles in packets** so neighbouring rays share traversal/cache.
- **Bit-exact reproducibility caveat:** per-texel writes are independent, but supersample averaging is
  a reduction — floating-point addition is non-associative, so the sub-sample accumulation order must
  be **fixed** (or compensated/Kahan), never a nondeterministic parallel reduce. Sample patterns are
  fixed; no `Math.Random`/wall-clock in the hot path.
- CPU only for v1.0 (GPU would reintroduce FP/scheduling/driver nondeterminism and native deps).

## Public surface (v1.0)

- `NormalBaker.Bake(...)` (one-shot) and an `IProgress<>`/cancellation variant for long bakes.
- `Mesh` / `UvMesh` / `BakedMap` POCOs + `NormalBake.IO` loaders/writers.
- `NormalBakeOptions` (already in the scaffold) — resolution, samples, cage distances, convention,
  bit depth, format, threads.
- CLI mirrors the options.
