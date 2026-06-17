# Acceleration & Performance — the baker is a ray tracer

A normal-map baker is, at its core, a **ray-cast engine**. It builds an acceleration structure over
the high-poly **once**, then queries it `width × height × samples` times. A 4K map at 4× supersampling
is **~268M closest-hit rays**, fired at a high-poly that is routinely **multi-million-triangle**. So
the two performance pillars are a fast, **watertight BVH ray tracer** and short, cage-bounded rays.
Because the build happens once and the queries number in the hundreds of millions, the economics are
the *opposite* of a real-time renderer: **spend the budget on traversal quality and compact node
layout, not on build speed.**

Everything here is implementable in **pure managed C#** with `System.Runtime.Intrinsics` — no native
dependency, matching NormalBake's (and Quadremesh's) stance.

## 1. The BVH

### 1.1 Surface Area Heuristic (SAH)
The SAH estimates the cost of splitting a node into children A, B as

```text
c(A,B) = t_trav + p_A · Σ t_isect(a_i) + p_B · Σ t_isect(b_i)
```

where `p_A = S_A / S_node` is the probability (by geometric/integral-geometry argument) that a random
ray hitting the parent also hits child A — **surface area, not volume**, is the correct measure
because an infinitely thin box is still hittable. Dropping the constant traversal term and assuming
uniform intersection cost gives the practical per-candidate form `C = N_L·A_L + N_R·A_R`. Build is a
greedy top-down recursion: evaluate split candidates per axis, pick the SAH minimum, partition,
recurse; make a leaf when the best split's cost exceeds the no-split cost `N·A_node`.[^pbrt][^bikker2]

### 1.2 Builder choices (ascending quality)
| Builder | Build speed | Traversal quality | Mechanism |
|---|---|---|---|
| Median / midpoint | fastest | poor | sort on longest axis, split at median |
| **LBVH / Morton** | very fast (parallel/GPU) | low–med | Z-order Morton code → radix sort → split at top differing bit[^karras] |
| HLBVH | fast | med–high | SAH on top levels + LBVH below[^hlbvh] |
| **Binned SAH** | moderate | high | project centroids into K bins, SAH only at K−1 boundaries via prefix scans[^wald2007] |
| Full-sweep SAH | slow | high | every centroid, 3 axes |
| **SBVH** (spatial splits) | slowest | **highest** | object **or** reference-clipping spatial split per node, chosen by SAH[^sbvh] |

- **Binned SAH** turns the O(N log N) sweep into a near-O(N) scatter+scan, trivially parallel, with
  negligible quality loss (PBRT uses 12 buckets; Bikker reports ~30× faster builds, same render
  time).[^wald2007][^bikker3]
- **SBVH** is the de-facto highest-quality *offline* BVH (Embree's "high-quality" builder). Object
  splits can't stop sibling-AABB **overlap** when large/tilted triangles straddle the plane; spatial
  splits clip a primitive *reference* into both children (controlled duplication, bounded by an
  α-threshold).[^sbvh]

**Recommendation for NormalBake:** **binned-SAH by default** (fast, parallel, high quality), with an
optional **SBVH** "high quality" path for production bakes — appropriate because we build once and cast
hundreds of millions of rays.

### 1.3 Wide nodes & memory layout
A binary BVH wastes SIMD lanes. Store **N children per node** (BVH4 fills SSE/4-wide, BVH8 fills
AVX2/8-wide) so one SIMD instruction tests N child boxes and the tree is shallower. **QBVH** quantizes
child AABBs to low-bit offsets from a per-node origin/extent — smaller nodes, less bandwidth.
Incoherent traversal is **bandwidth-bound**, so use **Structure-of-Arrays** node storage
(`min_x[0..N]`, then `min_y`, …) for single-load SIMD box tests, keep nodes compact so more of the
tree fits in L1/L2, and lay nodes out in DFS order.[^embree-paper]

## 2. Ray–triangle intersection — use the watertight one

- **Möller–Trumbore (1997)** is the classic minimum-storage test; it yields `t` and barycentrics
  `(u,v)` together via Cramer's rule. But it is **not watertight**: two triangles sharing an edge
  compute that edge from different vertex pairings, so floating-point signs can disagree — a ray
  through a shared edge/vertex can **leak through a crack (false miss)** or **double-hit**. In baking
  that shows up as **speckle / black pixels / leaked geometry**.[^mt]
- **Woop watertight intersection (2013)** is the one a baker should use. It transforms ray + vertices
  into a ray-aligned space (permute to dominant axis, shear/scale), then uses three 2D edge functions
  with a consistent sign test, so a shared edge is evaluated from the **same two vertices in the same
  order** on both triangles → bit-identical → provably no cracks and no double-hits. It falls back to
  double precision on exact-zero edges (failure ~1 in 50M).[^woop]

**Use two-sided (non-culling) intersection** — cage rays legitimately hit back-faces; culling at the
math level creates leaks. Reject back-faces as a *selection policy* instead (see
[`04`](04-cage-and-projection.md) §3).

## 3. Traversal modes
- **Single-ray + wide node** — best for *incoherent* rays.
- **Packet (N rays vs 1 box)** — best for *coherent* rays.
- **Ray-stream / hybrid** — regroup active rays to keep SIMD lanes full as coherence decays.

Baking rays come from a **UV grid along smoothly-interpolated normals**, so neighbours are coherent.
Trace in **UV-tile/packet order** (not random texel order) so lanes share BVH nodes and cache lines;
fall back to single-ray/stream at seam and silhouette texels. Stack-based traversal is simplest and
fastest on CPU; stackless matters mainly for GPU.[^embree-paper]

## 4. What makes a *baker* specifically fast

1. **Closest-hit vs any-hit.** The normal pass needs **closest hit** (keep running `t_max`, early-out
   below it). AO / bent-normal / thickness passes use cheap **any-hit / occlusion** queries that stop
   at the first hit.
2. **Short rays bounded by the cage — the single biggest win.** Set the ray's `t_max` from the
   cage/extrusion thickness (never infinity); the BVH then culls whole subtrees whose entry distance
   exceeds `t_max` immediately.
3. **Ray coherence.** Trace UV tiles in packets so neighbouring rays share traversal.
4. **Front-to-back ordered traversal + early-out** within the wide-node SIMD test, combined with the
   short-ray `t_max`.
5. **Embarrassingly parallel.** Partition texels/tiles across threads; the BVH is read-only during the
   pass. Pin the per-thread reduction order for **determinism** (§6).

## 5. CPU vs GPU

GPU baking (xNormal's GPU baker, Marmoset's GPU baker, OptiX/DXR) is far faster at huge ray counts but
adds heavy native + driver dependencies and is hard to make **deterministic** (FP/scheduling/driver
variance) — a poor fit for a cross-platform, zero-native, golden-tested tool. **CPU baking is the
correct default:** portable, dependency-light, deterministic, CI-friendly. Offer GPU later as an
optional accelerator behind a flag, never as a core dependency.

## 6. Determinism (a hard requirement, inherited from Quadremesh)

- **CPU, fixed accumulation order.** Pin per-thread reduction order or accumulate in a fixed order so
  supersample averaging is bit-reproducible — the same discipline Quadremesh uses for bit-exact flow.
- **Fix every variance source:** sample pattern/seed, supersample grid, cage params, tangent basis
  (MikkTSpace), and normal-map convention (OpenGL/DirectX). These feed the golden-image tests in
  [`08`](08-prior-art-tools-and-libraries.md) §5 and [`07`](07-quality-artifacts-and-aa.md).

## 7. Managed-SIMD toolbox (.NET 8)

- `System.Numerics.Vector2/3/4` and `Vector<T>` — portable SIMD for math and box tests.
- `System.Runtime.Intrinsics` — `Vector128/256/512<T>` plus `System.Runtime.Intrinsics.X86`
  (`Sse`, `Sse2`, `Sse41`, `Avx`, `Avx2`, `Avx512F`, `Fma`) and `.Arm` (`AdvSimd` NEON) for explicit
  4/8/16-wide node tests with **runtime ISA dispatch** — the managed analogue of Embree's runtime ISA
  selection.[^dotnet-intrin] This is the same toolchain Quadremesh's faithful integer back-end already
  uses; it extends directly to a BVH.

---

[^pbrt]: PBRT-v4 — Bounding Volume Hierarchies (SAH cost model). https://pbr-book.org/4ed/Primitives_and_Intersection_Acceleration/Bounding_Volume_Hierarchies
[^bikker2]: J. Bikker — *How to Build a BVH, part 2: Faster Rays*. https://jacco.ompf2.com/2022/04/18/how-to-build-a-bvh-part-2-faster-rays/
[^karras]: T. Karras, *Maximizing Parallelism in the Construction of BVHs, Octrees, and k-d Trees*, HPG 2012. https://research.nvidia.com/sites/default/files/pubs/2012-06_Maximizing-Parallelism-in/karras2012hpg_paper.pdf
[^hlbvh]: Pantaleoni & Luebke, *HLBVH*, HPG 2010. https://research.nvidia.com/publication/2010-06_hlbvh-hierarchical-lbvh-construction-real-time-ray-tracing
[^wald2007]: I. Wald, *On fast Construction of SAH-based Bounding Volume Hierarchies*, 2007. https://www.sci.utah.edu/~wald/Publications/2007/ParallelBVHBuild/fastbuild.pdf
[^bikker3]: J. Bikker — *How to Build a BVH, part 3: Quick Builds* (binning). https://jacco.ompf2.com/2022/04/21/how-to-build-a-bvh-part-3-quick-builds/
[^sbvh]: Stich, Friedrich & Dietrich, *Spatial Splits in Bounding Volume Hierarchies*, HPG 2009. https://dl.acm.org/doi/10.1145/1572769.1572771
[^embree-paper]: Wald et al., *Embree: Efficient Ray Tracing Kernels*, JCGT 4(4), 2015. https://jcgt.org/published/0004/04/05/
[^mt]: Möller & Trumbore, *Fast, Minimum Storage Ray-Triangle Intersection*, 1997. https://www.graphics.cornell.edu/pubs/1997/MT97.html
[^woop]: Woop, Benthin & Wald, *Watertight Ray/Triangle Intersection*, JCGT 2(1), 2013. https://jcgt.org/published/0002/01/05/
[^dotnet-intrin]: .NET 8 Hardware Intrinsics. https://devblogs.microsoft.com/dotnet/dotnet-8-hardware-intrinsics/ · https://learn.microsoft.com/dotnet/api/system.runtime.intrinsics
