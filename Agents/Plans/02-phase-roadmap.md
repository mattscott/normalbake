# 02 — Phase roadmap

Each phase lists **tasks**, **deliverables**, and **acceptance tests**. A phase is "done" only when
its acceptance tests pass on the shared corpus (`tests/corpus/`). Effort is rough relative sizing, not
calendar time. Doc references point at [../Documents/](../Documents/).

---

## P0 — Scaffold  *(effort: S)*  ✅ done

**Tasks**
- `NormalBake.sln` + three `src/` projects + `tests/` project (.NET 8), classic `.sln` (not `.slnx`).
- Shared `Directory.Build.props` / `Directory.Packages.props` / `global.json` / `.editorconfig`
  (analyzers on, style enforced, warnings fatal, deterministic).
- CI: build + test on push (GitHub Actions, Windows + Linux).
- MIT `LICENSE` + `NOTICE` (baking papers, MikkTSpace zlib, Blender/xNormal oracle).
- **Reference-oracle harness** stub under `tools/reference-oracle/` (Blender-headless plan; §08).

**Deliverables**: empty-but-building solution; green CI; oracle README in place.
**Acceptance**: `dotnet build` + `dotnet test` pass with placeholder tests (✅ 2/2, 0 warnings).

---

## P1 — Mesh & image I/O + core types  *(effort: M)*

**Tasks**
- `Mesh` / `UvMesh` / `BakedMap` POCOs (SoA).
- `NormalBake.IO`: **OBJ reader with UVs + normals** (and writer); **16-bit PNG writer** (linear /
  non-color, no sRGB chunk) via the stb ports.
- **UV-contract validation** (`ValidateUvContract`): non-overlap in the active tile, seams at hard
  edges, inter-island gutters ≥ 2× padding, sane texel density → fail fast with a clear message (§05).
- Assemble the **mesh-pair corpus** (sphere, cube, hard-edge bevel, cylinder, one dense real mesh),
  each with a UV-unwrapped low-poly.

**Deliverables**: OBJ(+UVs) → in-memory mesh → PNG round-trips; corpus committed.
**Acceptance**: OBJ round-trip preserves positions/UVs/normals; PNG is byte-stable and linear-tagged;
the validator accepts good UVs and rejects overlapping/gutter-poor UVs with actionable errors.

---

## P2 — Tangent basis: MikkTSpace  *(effort: M — kill-point #1)*

**Tasks**
- Deterministic **shortest-diagonal triangulation** of the low-poly, shared downstream.
- **Clean-room C# port of MikkTSpace** (`Tangents/`): order-independent, degenerate-tolerant,
  shortest-diagonal quads; per-vertex tangent with handedness `w`; zlib notice kept (§03).
- Wire tangents into `UvMesh`; expose them for emit-with-mesh.

**Deliverables**: per-vertex MikkTSpace tangents on any UV mesh.
**Acceptance**: tangent output is **bit-close to the C reference** (`mikktspace.c`) on the corpus
(within a tight tolerance); mirrored-UV handedness signs are correct; degenerate triangles inherit
neighbours' frames.

> **Kill-point:** if a faithful, reference-matching MikkTSpace can't be achieved in managed C#, revisit
> the whole approach before building the bake — every tangent-space map depends on this.

---

## P3 — BVH ray tracer  *(effort: L — kill-point #2)*

**Tasks**
- `Acceleration/`: **binned-SAH BVH** build (default) + optional **SBVH** high-quality build;
  compact SoA node layout.
- **Woop watertight** ray-triangle intersection, two-sided; **closest-hit** + **any-hit** queries;
  short cage-bounded `t_max` (§06).
- Brute-force reference tracer for validation.

**Deliverables**: closest-hit queries against a multi-million-tri high-poly.
**Acceptance**: BVH hits match the brute-force reference (same triangle + barycentrics within ε) on
randomized ray sets including **shared-edge/vertex grazing rays** (watertightness — no false misses /
double hits); build + query throughput meets an interim budget on a multi-million-tri mesh.

---

## P4 — Projection bake: tangent-space normal map  *(effort: L — #1 product risk)* → **M4 demoable**

**Tasks**
- `Baking/`: **UV-space rasterizer** (texel → surface point via barycentrics); **distance-envelope
  cage** `(frontal, rear)` along averaged normals; **closest front-most** hit + **nearest-point
  fallback**; sample HP **smooth** normal; transform into the low-poly tangent frame with the
  unnormalized interpolated `N,T` and `B = w·(N×T)` (§02, §03).
- **Supersampling** over an N×N sub-grid, averaging **only hit samples** over a coverage mask;
  renormalize; **Extend / flood-fill dilation** (never adjacent-faces cross-seam) (§05, §07).
- **Encoding** (`Encoding/`): normal→RGB, OpenGL/DirectX **Y-flip**, 8/16-bit; default 16-bit PNG;
  **emit MikkTSpace tangents** with the output mesh.
- Wire `NormalBaker.Bake` end-to-end; CLI `normalbake high low -o out.png`.

**Deliverables**: **end-to-end** high-poly + UV low-poly → tangent-space normal map.
**Acceptance**: output matches a **Blender-headless golden** within per-pixel tolerance + an SSIM
threshold on the corpus; **shades seam-free** under a MikkTSpace-synced re-render; misses are filled
(no holes); flat regions ≈ (128,128,255); runs the full corpus without crashing.

---

## P5 — Robustness & quality  *(effort: M)* → **M5 production quality**

**Tasks**
- **Explicit cage mesh** input (same topo/UVs); **skew correction**; tune averaged-normal directions.
- **Match-by-name** per-part isolation (`_low`/`_high`) to stop proximity bleed; **UDIM** per-tile
  iteration; **mirrored / stacked UV** handling (bake/reuse + integer tile offset) (§04, §05).
- **Object-space** normal output; **BC5/BC7 DDS** + **EXR** writers (BCnEncoder.NET, Jither.OpenEXR);
  decode-compare BC outputs against the uncompressed golden.

**Deliverables**: production-grade bakes on multi-part, mirrored, UDIM assets.
**Acceptance**: no bleed between named parts; mirrored/UDIM shells bake correctly; BC5 round-trips
within tolerance; cage + skew fix the hard-surface corpus part's projection artifacts.

---

## P6 — The general baker  *(effort: M)* → **M6**

**Tasks**
- **Same single-ray core**: **Position / world-position**, **Object/Material ID** (AA off).
- **Hemisphere secondary-ray sampler**: **Ambient Occlusion**, **Thickness**, **Bent Normals**.
- **Post-process passes**: **Curvature**, **Height/Displacement**, **Derivative**; optional
  **Toksvig/variance roughness** output for specular-AA-correct mips (§07 §2, §07 §6).

**Deliverables**: a multi-map baker driven by `--map`.
**Acceptance**: each map matches a Blender/Substance-style reference within tolerance; AO/thickness
sampler converges; ID maps have hard (un-antialiased) boundaries.

---

## P7 — Performance & determinism  *(effort: M)*

**Tasks**
- **Wide-node SIMD BVH** (BVH4/BVH8 via `System.Runtime.Intrinsics`, runtime ISA dispatch); **packet**
  traversal for coherent UV tiles; parallel texel/tile partitioning.
- **Determinism hardening:** audit supersample/reduction accumulation for float non-associativity;
  enforce fixed/sequential or compensated order so output is bit-exact for fixed params (§01 Determinism).
- Benchmark harness + perf-regression gate in CI; fuzz/robustness pass over dirty inputs.

**Deliverables**: meets the ray-throughput target; deterministic, crash-free on the dirty corpus.
**Acceptance**: perf budget met on reference hardware; bit-exact output for fixed params across
Windows + Linux; robustness corpus passes.

---

## P8 — Packaging, CLI, API polish  *(effort: S–M)* → **M7 ship**

**Tasks**
- `NormalBake.Cli` full option surface + `--help`; progress reporting; clear UV-contract errors.
- Public API docs (XML docs + README usage); `IProgress<>`/cancellation.
- NuGet package for `NormalBake.Core` + `NormalBake.IO`; semantic versioning.
- Sample gallery (before/after renders from the corpus).

**Deliverables**: installable package + working CLI + docs.
**Acceptance**: `dotnet tool`/NuGet install works clean on Windows + Linux; a new user can bake a
normal map from the CLI following the README alone.

---

## Cross-cutting (every phase)

- Update the **test corpus** and golden images as stages stabilize.
- Validate bake stages against **reference Blender-headless** outputs (per-pixel + SSIM + FLIP map),
  with **xNormal** as a historical cross-check (`tools/reference-oracle/`).
- Keep `NormalBake.Core` dependency-light and **clean-room**; permissive deps only (no ImageSharp split
  license); attribute influences in `NOTICE`.
- CPU + fixed accumulation order for determinism; no native binaries.
