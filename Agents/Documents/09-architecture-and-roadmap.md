# Architecture & Roadmap

This synthesizes the research into a concrete, **managed-C#/.NET 8, dependency-light, MIT-clean**
architecture for NormalBake, and an MVP → vNext roadmap. It is a *proposal* for the `Agents/Plans`
roadmap to refine, not a final spec.

## 1. Constraints

- **.NET 8, pure managed.** Hot loops on `System.Numerics` + `System.Runtime.Intrinsics` (runtime ISA
  dispatch).
- **No native dependencies.** The BVH ray tracer, tangent basis, rasterizer, dilation, and image I/O
  are all in-repo or pure-managed packages — no Embree/xatlas/MikkTSpace native DLLs.
- **MIT/BSD-clean, clean-room.** Reimplement from papers/docs; reference bakers only generate goldens.
- **Deterministic & golden-tested.** CPU, fixed accumulation order; Blender headless as the oracle.
- **Library + CLI.**

## 2. The pipeline (data flow)

```text
 high.obj ─► load ─► build BVH (binned-SAH / SBVH) ──────────────┐
                                                                 │
 low.obj  ─► load ─► validate UVs (required; from the UV-unwrap tool) ─► triangulate (shortest-diag,
                     MikkTSpace-consistent) ─► MikkTSpace tangents ─► build cage/envelope
                                                                 │
                     for each UV texel (SSAA sub-grid):          ▼
                       recover P,N,T,w ─► cast cage ray ─► closest front hit ─► sample HP
                       smooth normal ─► to tangent frame ─► encode (Y-flip) ─► accumulate
                                                                 │
                     resolve samples over coverage mask ─► renormalize ─► Extend-dilate
                                                                 │
                                                                 ▼
                          encode 16-bit PNG (default) / EXR / DDS-BC5  +  emit tangents
```

Each stage maps to a research doc: BVH → [`06`](06-acceleration-and-performance.md); tangents/MikkTSpace
→ [`03`](03-tangent-space-and-mikktspace.md); cage → [`04`](04-cage-and-projection.md); per-texel bake →
[`02`](02-baking-algorithm.md); UVs/dilation → [`05`](05-uvs-and-dilation.md); AA → [`07`](07-quality-artifacts-and-aa.md);
encode/formats → [`01`](01-normal-mapping-fundamentals.md).

## 3. Proposed module layout

```text
src/
  NormalBake.Core/            zero third-party deps
    Mesh/                     half-edge / indexed mesh, attributes, deterministic triangulation
    Math/                     vec/mat, SIMD helpers
    Tangents/                 MikkTSpace-faithful basis (clean-room port; the correctness keystone)
    Acceleration/            BVH (binned-SAH + optional SBVH), Woop watertight ray-tri, packets
    Baking/                  UV rasterizer, cage/envelope, projection, sampling, SSAA, dilation
    Encoding/                normal<->RGB, Y-flip, 8/16-bit, BC5/BC7 (BCnEncoder.NET)
  NormalBake.IO/              OBJ first; PNG/TGA/EXR/DDS
  NormalBake.Cli/             normalbake high.obj low.obj -o normal.png ...
tests/
  NormalBake.Tests/           unit + golden-image tests
  corpus/                     curated mesh pairs + committed goldens
tools/
  reference-oracle/           Blender-headless scripts that generate goldens (xNormal cross-check)
```

A conventional `Core` / `IO` / `Cli` split keeps the algorithm core free of IO/CLI concerns, with a
separate test corpus and an offline reference-oracle harness for goldens.

## 4. Dependency decisions (from [`08`](08-prior-art-tools-and-libraries.md))

| Need | Decision | Why |
|---|---|---|
| Ray tracing | **In-repo managed BVH** (binned-SAH + SBVH option), **Woop** watertight ray-tri | zero-native; Embree (Apache-2.0) is native → bind-only, rejected |
| Tangent basis | **Clean-room MikkTSpace port** (C# from the zlib C reference) | correctness keystone; no managed port exists; low effort |
| BC5/BC7 + DDS | **BCnEncoder.NET** (MIT/Unlicense, pure-managed) | only managed BCn *encoder*; replaces native encoders |
| PNG/TGA I/O | **StbImageSharp / StbImageWriteSharp** (PD/MIT) | unconditionally permissive; avoid ImageSharp's split license |
| EXR | **Jither.OpenEXR** (MIT, managed, scanline write) | float archival output |
| DDS decode | **Pfim** (MIT) | for round-trip/golden decode of BC outputs |
| UV unwrap | **Out of scope — separate tool** | NormalBake requires input UVs; a separate UV-unwrap tool (xatlas-class) produces them |
| Test diff | **FLIP** (BSD-3, test-only) + SSIM/PSNR | perceptual error maps as CI artifacts |

Avoid **ImageSharp** in the shipped core (Six Labors Split License → commercial fee ≥ $1M revenue).

## 5. CLI sketch

```text
normalbake <high> <low> -o <out> [options]

  --map normal|object-normal|position|id|ao|...   (normal = MVP; others phased)
  --res 2048            output resolution (multiple of 4 for BCn)
  --samples 4           SSAA grid (NxN); only hit samples averaged
  --cage <file>         explicit cage mesh (same topo/UVs); else distance envelope:
  --frontal <d> --rear <d>
  --convention opengl|directx   (green-channel; default opengl)
  --tangents mikktspace         (default; emitted with output mesh)
  --bitdepth 8|16       (default 16)
  --format png|exr|dds  --compress bc5|bc7
  --padding auto|<px>   (Extend/flood-fill; default to-collision)
  --match-by-name       per-part isolation (_low/_high)
  --udim                per-tile iteration
  --threads N --deterministic
```

Mesh-name matching, UDIM, and convention presets are the table-stakes ergonomics every production
baker exposes ([`04`](04-cage-and-projection.md), [`05`](05-uvs-and-dilation.md)).

## 6. Roadmap

### Phase 0 — spike / kill-points (de-risk first)
The two riskiest pieces, validated in isolation before committing to the full architecture:
1. **MikkTSpace port** — port the single C file to C#, validate tangent output **bit-close to the C
   reference** on a corpus (the correctness keystone; cheap to prove).
2. **BVH + watertight ray-tri** — managed binned-SAH BVH + Woop intersection; validate hit
   correctness/perf against a brute-force reference on a multi-million-tri mesh.

*If either can't be made faithful/fast in managed C#, rethink before building the rest.*

### Phase 1 — MVP: tangent-space normal bake (requires input UVs)
UV rasterizer → distance-envelope cage → closest-hit projection → SSAA over a coverage mask → **Extend**
dilation → 16-bit PNG, with OpenGL/DirectX flag and emitted MikkTSpace tangents. **Validation:** golden
images from Blender headless (sphere, cube, hard-edge bevel, cylinder, one dense mesh), per-pixel +
SSIM/PSNR + FLIP against the reference oracle.

### Phase 2 — robustness & quality
Explicit cage mesh + smoothed/averaged normals + nearest-point fallback; **skew correction**; per-part
**match-by-name**; **UDIM**; BC5/BC7 DDS output + EXR; mirrored-UV handling.

### Phase 3 — the general baker (reuse the ray core)
**Position** & **Object/Material ID** (same single-ray core); **AO / Thickness / Bent Normals**
(hemisphere secondary-ray sampler); **Curvature / Height / Derivative** (post-passes). Optional
**Toksvig/variance roughness** output for specular-AA-correct mips ([`07`](07-quality-artifacts-and-aa.md) §2).

> **Not a NormalBake phase:** UV unwrapping. A bare remeshed mesh (no UVs) is made bakeable by the
> **separate UV-unwrap tool**, which sits between the quad-remesher and NormalBake in the pipeline.

### Always-on
Determinism (CPU, fixed reduction order), golden corpus, no native deps, MIT-clean.

## 7. The three things most likely to bite (watch list)

1. **Tangent-basis sync.** Get MikkTSpace bit-faithful and *emit the tangents*, or every bake has subtle
   seams ([`03`](03-tangent-space-and-mikktspace.md)). The single highest-leverage correctness item.
2. **Triangulation agreement.** One deterministic shortest-diagonal triangulation shared by tangent
   solver, rasterizer, and the consumer — or you get X-shaped errors ([`07`](07-quality-artifacts-and-aa.md) §3.3).
3. **The UV precondition.** Quad-remesher output has no usable UVs; NormalBake **requires** them as input
   (the separate UV-unwrap tool produces them) and must **validate the UV contract and fail
   fast** — non-overlap, seams at hard edges, gutters ≥ 2× padding ([`05`](05-uvs-and-dilation.md) §1).
