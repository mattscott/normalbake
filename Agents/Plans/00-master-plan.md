# 00 — Master plan

## Vision

A permissively-licensed, dependency-light, cross-platform **managed-C# (.NET 8) library + CLI** that
bakes a **normal map** (and, later, AO/curvature/position/ID/…) from a high-resolution mesh into the
UV space of a low-resolution, quad-remeshed mesh — the baking partner to **Quadremesh**, in the same
clean-room, golden-tested, zero-native-dependency mold.

## Why this is achievable now

The research ([../Documents/](../Documents/)) confirms that high-to-low normal baking is a
**well-understood, decades-old method**, not open research: for each low-poly UV texel, recover its
surface point + tangent frame, cast a cage-guided ray to the high-poly, sample the high-poly's smooth
normal, and re-express it in the low-poly's tangent frame (Cignoni et al. 1998; Cohen/Olano/Manocha
1998). Every piece has a **pure-managed, MIT-clean** path: a BVH ray tracer in `System.Runtime.Intrinsics`,
a clean-room **MikkTSpace** tangent port, and existing managed libraries for image/BCn I/O. The only
hard problems are *fidelity* (tangent-space sync) and *speed* (ray throughput), both addressed head-on.

## Scope (v1.0)

**In:**
- Triangle high-poly + **UV-unwrapped** low-poly (quad or tri) input (OBJ first; glTF later).
- **Tangent-space normal map** bake (object-space as a secondary output).
- **MikkTSpace-faithful** per-vertex tangents, emitted with the output mesh.
- **Cage / projection envelope**: distance-based `(frontal, rear)` and explicit cage mesh; averaged
  normals; closest front-most hit with a nearest-point fallback.
- **Supersampling** AA over a coverage mask; **Extend** (flood-fill) edge dilation.
- Encoding: OpenGL/DirectX convention flag, 8/16-bit, **BC5/BC7 DDS**, EXR; default **16-bit PNG**.
- Per-part **match-by-name** isolation; **UDIM** / mirrored-UV handling.
- CLI tool + clean public C# API; deterministic output.

**Out (v1.0 non-goals):**
- **UV unwrapping** — the low-poly arrives with UVs from the separate sister project; NormalBake only
  validates them ([../Documents/05-uvs-and-dilation.md](../Documents/05-uvs-and-dilation.md) §1).
- GPU baking (CPU is portable + deterministic; GPU is a post-1.0 optional accelerator).
- In-app skew-painting UI.
- Displacement/height as a *primary* deliverable.

## Success criteria (v1.0 "done")

1. Bakes a tangent-space normal map from a multi-million-tri high-poly onto a UV-unwrapped low-poly at
   2K–4K in a reasonable time budget on a desktop CPU.
2. Output matches a **Blender-headless golden** within per-pixel tolerance + an SSIM threshold on the
   shared corpus — and shades **seam-free** under a MikkTSpace-synced renderer.
3. Emits **MikkTSpace tangents** with the mesh; a consumer engine reproduces the basis.
4. Robust: handles mirrored/UDIM UVs, multi-part assets (match-by-name), and ray misses
   (dilation + nearest-point fallback) without crashing; degrades to best-effort.
5. Reproducible: deterministic output for fixed params (CPU, fixed accumulation order).
6. Single managed artifact, no native binaries, MIT/permissive-clean dependency graph.

## Milestones

| Milestone | Phases | Outcome |
|---|---|---|
| **M1 — Skeleton** | P0 | Builds, green CI, license/NOTICE, oracle harness stub |
| **M2 — I/O + corpus** | P1 | OBJ (with UVs) load + PNG write; mesh-pair corpus + goldens |
| **M3 — Keystones cleared** | P2–P3 | MikkTSpace bit-close to C ref; BVH watertight + fast vs brute force |
| **M4 — First bake** | P4 | **Demoable** tangent-space normal map, validated vs Blender golden |
| **M5 — Production quality** | P5 | Cages, fallbacks, skew, match-by-name, UDIM, BC5/BC7/EXR |
| **M6 — General baker** | P6 | AO / curvature / position / ID / thickness / bent normals |
| **M7 — Ship** | P7–P8 | Perf + determinism hardened; CLI + API + NuGet package |

## Top risks

1. **Tangent-basis sync (MikkTSpace fidelity) — the #1 correctness risk.** A map only decodes
   correctly if the renderer rebuilds the *exact* basis the baker used. Mitigate: P2 ports MikkTSpace
   and validates it **bit-close to the C reference**; the baker decodes with the unnormalized
   interpolated frame and **emits the tangents** ([../Documents/03-tangent-space-and-mikktspace.md](../Documents/03-tangent-space-and-mikktspace.md)).
2. **BVH correctness & speed in managed C#.** A 4K@4× bake is ~268M rays at a multi-million-tri
   high-poly. Mitigate: **Woop watertight** ray-tri (no cracks/speckle), binned-SAH/SBVH, short
   cage-bounded `t_max`, wide-node SIMD in P7; validate vs brute force ([../Documents/06-acceleration-and-performance.md](../Documents/06-acceleration-and-performance.md)).
3. **Cage/projection artifacts** (skew, holes, bleed). Mitigate: averaged normals + nearest-point
   fallback + explicit cage + match-by-name ([../Documents/04-cage-and-projection.md](../Documents/04-cage-and-projection.md), [../Documents/07-quality-artifacts-and-aa.md](../Documents/07-quality-artifacts-and-aa.md)).
4. **Upstream UV-contract violations.** Mitigate: validate the incoming UVs (non-overlap, seams at
   hard edges, gutters ≥ 2× padding) and **fail fast** with a clear message.

## Working method

- Each phase has explicit **deliverables + acceptance tests** ([02-phase-roadmap.md](02-phase-roadmap.md)).
- `NormalBake.Core` is written **clean-room** — no code lifted from xNormal/Blender/Embree; the
  MikkTSpace C reference is ported faithfully (zlib notice kept) and validated, not copied verbatim.
- Validate bake stages against **reference Blender-headless outputs** on identical inputs (per-pixel +
  SSIM + a FLIP error map), with **xNormal** as a historical cross-check — mirroring how Quadremesh
  validates against QuadriFlow.
- Keep a **test corpus** of mesh pairs under `tests/corpus/` (sphere, cube, hard-edge bevel, cylinder,
  one dense real mesh) used by every phase, with committed goldens under `tests/golden/`.
- TDD where practical; golden-image tests for end-to-end stages; CPU + fixed accumulation order for
  determinism.
