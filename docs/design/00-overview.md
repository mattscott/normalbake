# NormalBake — Project Overview & Research Index

> **Status: research / pre-scaffold.** This directory holds the background research the tool
> will be built on. The implementation roadmap lives in [`../Plans`](../Plans).

## 1. What NormalBake is

**NormalBake** transfers high-frequency surface detail from a **high-resolution** triangle mesh
onto a **low-resolution** quad-remeshed mesh, by writing that detail into a **normal map** in the
low-poly's **UV space**. The low-poly then *renders* as if it had the high-poly's detail, for a
tiny fraction of the triangle count.

This is the standard "high-to-low" or "detail transfer" bake used throughout games and real-time
3D. NormalBake is the **baking** stage of a three-tool pipeline — a **separate UV-unwrap tool** does
the UV unwrap, so NormalBake assumes the low-poly already carries UVs:

```
  high-poly  ──►  quad-remesher ──►  UV-unwrap tool   ──►  NormalBake   ──►  normal map (+ AO, curvature, …)
  (sculpt)        (retopology)       (separate tool)       (this tool)
                  clean quad LP      LP + UVs              bakes HP detail into the LP's UV map
       │                                                        ▲
       └──────────────────────────  high-poly  ─────────────────┘
```

The quad-remesher answers *"what is the clean low-poly?"* NormalBake answers *"how do I make that
low-poly look like the original?"*

## 2. Design philosophy

NormalBake holds to a deliberate engineering stance:

| Principle | Consequence for NormalBake |
|---|---|
| **Managed C# / .NET 8** | Pure managed core; `System.Numerics` / SIMD intrinsics for the hot loops. |
| **Dependency-light, no native deps** | The ray-tracing BVH, tangent basis, rasterizer, and image I/O are implemented in-repo or via pure-managed packages — no Embree/native DLLs. |
| **MIT/BSD-clean, clean-room** | Algorithms reimplemented from papers and public docs; reference tools used only to *generate golden outputs*, never linked or copied. |
| **Library + CLI** | `NormalBake.Core` (engine) + `NormalBake.Cli` (`normalbake high.obj low.obj -o normal.png …`). |
| **Deterministic, golden-tested** | A reference baker (candidate: headless **Blender**, with **xNormal** as a cross-check) produces golden images we validate against with a per-pixel / SSIM tolerance. |

See [`09-architecture-and-roadmap.md`](09-architecture-and-roadmap.md) for the proposed module
layout and MVP plan.

## 3. The core idea in one paragraph

A normal map is a texture where each texel stores a **surface normal** instead of a colour. To
bake one, NormalBake walks every texel of the low-poly's UV atlas, recovers the 3D point and
**tangent frame (TBN)** that texel corresponds to on the low-poly surface, casts a ray from there
(guided by a **cage**) until it hits the high-poly, reads the high-poly's smooth interpolated
normal at the hit, expresses that normal **relative to the low-poly's tangent frame**, and writes
it to the texel. Renormalize, antialias, dilate past the island edges, and save. The whole game
is (a) getting the tangent frames to match what the *renderer* will use, and (b) tracing millions
of rays fast.

## 4. The two things that decide whether a bake looks right

Almost every baking artifact traces back to one of these, so they recur throughout the docs:

1. **Tangent-space synchronization.** The map only decodes correctly if the renderer reconstructs
   the *exact same* TBN basis the baker used. This is why **MikkTSpace** exists and why it is the
   industry default. See [`03-tangent-space-and-mikktspace.md`](03-tangent-space-and-mikktspace.md).
2. **The cage / projection envelope.** How far and in what direction rays are cast controls
   whether they hit the right high-poly surface — bad cages cause skewed/"swimming" normals,
   holes, and bleeding between parts. See [`04-cage-and-projection.md`](04-cage-and-projection.md).

## 5. Scope

**In scope for the MVP:** tangent-space normal map bake from a high-poly tri mesh to a UV-unwrapped
low-poly (quad or tri), with a cage/ray-distance envelope, supersampling AA, and edge-padding
(dilation), emitting PNG. Object-space normals as a secondary output.

**Likely fast-follow (same projection pass):** ambient occlusion, curvature, world/object position,
thickness, and object/material ID maps — these reuse the exact same ray-cast infrastructure and
are cheap to add once the core works. See the roadmap.

**Explicitly out of scope:** UV *unwrapping* — NormalBake **requires the low-poly to arrive already
UV-unwrapped**, produced by a **separate UV-unwrap tool** (see
[`05-uvs-and-dilation.md`](05-uvs-and-dilation.md)); GPU baking; displacement/height as a primary
deliverable; in-app skew painting UI.

## 6. The research document set

| Doc | Topic |
|---|---|
| [`01-normal-mapping-fundamentals.md`](01-normal-mapping-fundamentals.md) | What a normal map is; tangent vs object vs world space; encoding & conventions (DirectX vs OpenGL); shader reconstruction. |
| [`02-baking-algorithm.md`](02-baking-algorithm.md) | The end-to-end projection / ray-cast bake pipeline, with pseudocode. |
| [`03-tangent-space-and-mikktspace.md`](03-tangent-space-and-mikktspace.md) | TBN math, Lengyel's basis, MikkTSpace, and baker/renderer sync (the #1 artifact source). |
| [`04-cage-and-projection.md`](04-cage-and-projection.md) | Cages, search distance, ray direction, hit selection, and the artifacts bad projection causes. |
| [`05-uvs-and-dilation.md`](05-uvs-and-dilation.md) | UV requirements, padding/margin, edge dilation, UDIM, texel density, mirrored UVs. |
| [`06-acceleration-and-performance.md`](06-acceleration-and-performance.md) | BVH (SAH), ray–triangle intersection, SIMD, and why a baker is ray-bound. |
| [`07-quality-artifacts-and-aa.md`](07-quality-artifacts-and-aa.md) | Anti-aliasing, the smoothing-group rule, common artifacts and fixes, QC/validation. |
| [`08-prior-art-tools-and-libraries.md`](08-prior-art-tools-and-libraries.md) | xNormal, Marmoset, Substance, Blender, Knald, Handplane; reusable libraries + licenses; oracle choice. |
| [`09-architecture-and-roadmap.md`](09-architecture-and-roadmap.md) | Proposed C#/.NET 8 module layout, dependency decisions, CLI design, MVP → vNext roadmap. |
| [`references.md`](references.md) | Consolidated bibliography (papers, docs, repos) with URLs. |

## 7. Glossary

- **High-poly (HP):** the detailed source mesh (sculpt, scan, CAD) — millions of triangles, no UVs needed.
- **Low-poly (LP):** the simplified render mesh — the quad-remesher's output; **must have UVs** to be baked onto.
- **Normal map:** RGB texture encoding per-texel surface normals. **Tangent-space** = relative to the
  surface's local TBN frame (deformable, the default); **object-space** = relative to the model origin.
- **TBN / tangent frame:** the orthonormal-ish basis (**T**angent, **B**itangent, **N**ormal) at a
  surface point that defines "which way is right/up/out" for decoding a tangent-space normal.
- **Cage:** an inflated copy of the low-poly that defines per-point ray directions and how far to
  search for the high-poly; prevents skew and misses.
- **Search/ray distance (frontal/rear):** how far rays travel outward/inward looking for a HP hit.
- **Dilation / edge padding / bleed:** copying baked colour outward past UV island borders so
  bilinear filtering and mipmaps don't pull in the empty background.
- **Supersampling (SSAA):** baking at NxN samples per texel and averaging, to antialias.
- **Texel density:** texture pixels per unit of model surface area; governs how much detail the map can hold.
- **MikkTSpace:** Morten Mikkelsen's standard algorithm/convention for computing tangent bases so
  that bakers and renderers agree.
- **Oracle / golden:** a trusted reference baker whose outputs we diff against in tests.
