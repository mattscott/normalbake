# NormalBake — Research & Design Docs

Background research for **NormalBake**: a managed-C#/.NET 8, dependency-light, MIT-clean library+CLI
that bakes a **normal map** from a high-resolution mesh into the UV space of a low-resolution
(quad-remeshed) mesh — the baking partner to [Quadremesh](../../../Quadremesh).

Start with **[`00-overview.md`](00-overview.md)**. Suggested reading order:

| # | Doc | What it answers |
|---|---|---|
| 00 | [Overview & index](00-overview.md) | What NormalBake is, how it pairs with Quadremesh, scope, glossary. |
| 01 | [Normal mapping fundamentals](01-normal-mapping-fundamentals.md) | What a normal map is; encoding; OpenGL/DirectX; linear-not-sRGB; bit depth; BC5/BC7; formats. |
| 02 | [The baking algorithm](02-baking-algorithm.md) | The end-to-end projection / ray-cast pipeline (with pseudocode). |
| 03 | [Tangent space & MikkTSpace](03-tangent-space-and-mikktspace.md) | TBN math, Lengyel, MikkTSpace, baker↔renderer sync — the #1 correctness lever. |
| 04 | [Cage & projection](04-cage-and-projection.md) | Cages, search distance, ray direction, hit selection, projection artifacts. |
| 05 | [UVs & dilation](05-uvs-and-dilation.md) | The UV input contract (required; a sister tool unwraps), texel density, edge padding. |
| 06 | [Acceleration & performance](06-acceleration-and-performance.md) | BVH (SAH/SBVH), watertight ray-tri, SIMD, why a baker is ray-bound. |
| 07 | [Quality, artifacts & AA](07-quality-artifacts-and-aa.md) | Supersampling, mip/specular AA, the seam-free rules, artifact table, QC. |
| 08 | [Prior art, tools & libraries](08-prior-art-tools-and-libraries.md) | Tool survey, the golden oracle (Blender headless), license-checked managed libs. |
| 09 | [Architecture & roadmap](09-architecture-and-roadmap.md) | Proposed module layout, dependency decisions, CLI, MVP → vNext. |
|    | [references.md](references.md) | Consolidated, grouped bibliography with URLs. |

## TL;DR — the load-bearing conclusions

1. **The core method is decades old and well-understood.** For each low-poly UV texel: recover its 3D
   point + tangent frame, cast a cage-guided ray to the high-poly, sample the high-poly's smooth normal,
   express it in the low-poly's tangent frame, encode, antialias, dilate. (Cignoni et al. 1998; doc 02.)
2. **Two things decide whether it looks right:** (a) **tangent-space sync** — bake with a
   **MikkTSpace**-faithful basis and *emit the tangents*, or you get seams (doc 03); (b) the **cage** —
   averaged-normal/cage-guided rays with a bounded distance, or you get skew/holes (doc 04).
3. **The low-poly must have UVs, and quad remeshers don't produce them — so a separate sister project
   unwraps.** NormalBake **requires** input UVs and validates the UV contract; it does not unwrap (doc 05).
4. **It's a ray tracer.** A 4K @ 4× bake is ~268M rays against a multi-million-tri mesh — needs an
   in-repo managed **BVH** (binned-SAH/SBVH) with **watertight** ray-tri intersection (doc 06).
5. **A fully managed, MIT-clean, zero-native build is feasible end-to-end.** Pure-C# libraries already
   cover BC5/BC7, PNG/TGA/EXR, DDS decode; the only nontrivial clean-room ports are **MikkTSpace** (low
   effort, the keystone) and **xatlas** (only for the no-UV path) (doc 08).
6. **Validate like Quadremesh validates against QuadriFlow:** generate goldens with **Blender headless**
   and diff per-pixel + SSIM/PSNR + FLIP maps; CPU + fixed accumulation order for determinism (docs 06, 08).

## How this was produced

Synthesized from four parallel research passes over primary sources (seminal SIGGRAPH/IEEE-Vis papers,
the MikkTSpace reference, Marmoset/Substance/Blender baker docs, the polycount wiki, BVH/ray-tracing
literature, and library license files). Every nontrivial claim is cited inline; full list in
[`references.md`](references.md). A handful of figures behind fetch-blocked hosts are flagged in-doc.
