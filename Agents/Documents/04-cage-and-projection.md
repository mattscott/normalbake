# Cage & Projection — controlling where rays go

The bake casts a ray from each low-poly texel and records what it hits on the high-poly
([`02-baking-algorithm.md`](02-baking-algorithm.md), steps C–D). **Where those rays start and which
direction they travel** is the *projection*, and it is — alongside tangent-space sync — one of the two
decisions that determine whether a bake looks right. This document covers the envelope (the **cage**),
ray direction, hit selection, and the artifacts bad projection produces.

## 1. Why naive normal-casting fails

Shooting each ray straight along the raw low-poly vertex normal breaks in three ways:

1. **Divergence / crossing.** On convex or concave regions, neighbouring rays fan apart or cross, so a
   ray misses its intended high-poly surface or hits a neighbouring part.
2. **Skew at hard edges / UV seams.** Where per-face normals disagree, ray directions snap and the
   projection skews.
3. **Self-projection.** Without a distance limit, rays on concave faces "project onto themselves or
   other faces" — Marmoset's stated reason cages exist.[^marmoset-cage]

The fixes are: cast along **smoothed/averaged** normals (or a cage's smoothly-varying normals), and
**bound** the ray with a maximum distance.

## 2. Two ways to define the projection envelope

### 2.1 Distance-based (frontal + rear)
No explicit cage geometry. Inflate the low-poly surface point along its **averaged vertex normal** by
an outer (**frontal**) distance to put the ray start *outside* the high-poly, and let the ray travel a
**rear** distance below the surface. Substance: *"the distance is computed based on the setting and the
direction given by the **averaged normal**,"* with **Max Frontal Distance** and **Max Rear
Distance**.[^substance-mm] Blender's equivalents are **Extrusion** (push the low-poly out along
normals so the high-poly is enclosed) and **Ray Distance** (the inward cast length).[^blender-bake]

```text
dir    = averagedVertexNormal(P)
origin = P + dir * frontalDist            # start outside the surface
maxDist= frontalDist + rearDist           # travel from +frontal to -rear
hit    = raycast(BVH, origin, -dir, maxDist)
```

### 2.2 Explicit cage mesh
A **"ballooned-out" copy of the low-poly** with identical topology and UVs, shaped to fully enclose
the high-poly. Rays originate on the cage surface and shoot **inward toward the low-poly surface
point**; the cage supplies both the ray *origin* and *direction*.[^blender-bake] Because the cage
shares topology, its interpolated normals vary smoothly, removing the divergence/skew of raw normals.

```text
Pcage  = barycentric_interp(cageTri positions)   # per-texel, same UVs as low-poly
dir    = normalize(P - Pcage)                     # inward, smoothly varying
origin = Pcage
maxDist= length(P - Pcage) + rearDist
```

### 2.3 Ray direction: averaged normals vs cage offset
- **Averaged / smoothed normals** make the projection direction continuous even across hard edges —
  Substance "computes the averaged normal between the split vertex normals when the low-res mesh has a
  hard edge."[^substance-mm] This is the cheap skew fix when no cage is authored.
- **Cage-offset direction** = `normalize(P_lowpoly − P_cage)`: artist-controlled and smooth by
  construction. Marmoset additionally offers **Paint Skew** — *"skew maps create a per-pixel blend
  between the face normals of the low-poly mesh and the cage normals"* — to correct off-axis
  projection locally.[^marmoset-cage]

### 2.4 How the reference tools expose this
- **Marmoset Toolbag** — internal cage via **Min Offset / Max Offset** sliders, plus **Paint Offset**
  (per-vertex cage push) and **Paint Skew** (per-pixel direction fix).[^marmoset-cage][^marmoset-proj]
- **Substance 3D** — **Max Frontal / Max Rear Distance**, distance-based or explicit cage file, and
  **Match By Name** to pair high/low parts (e.g. `_high`/`_low`) so geometry doesn't bleed between
  separate objects.[^substance-match]
- **Blender** — `cage_object`, `cage_extrusion`, `max_ray_distance` on the bake settings.[^blender-bake]

## 3. Hit selection

For each ray, choose one high-poly intersection:

- **Closest / front-most.** Standard choice is the **nearest hit** measured from the ray origin (the
  cage / outer envelope), within `maxDist`. Casting from outside inward makes "first front-facing hit"
  equal "the visible high-poly surface this texel should sample."
- **Bidirectional.** With distance envelopes, cast outward to `+frontalDist` and inward to `−rearDist`
  and take the nearest valid hit (Blender's Extrusion + Ray Distance bracket the high-poly).
- **Backface handling.** Provide an **Ignore Backfaces** toggle — skip hits where
  `dot(rayDir, hitFaceNormal) > 0` (the inside of a high-poly face) — essential for thin/concave parts
  (tubes) so rays don't grab the far wall. (Note: the *intersection routine* should still be two-sided;
  backface *rejection* is a selection policy, not a culling of the math — see
  [`06`](06-acceleration-and-performance.md).)
- **Ties.** Within an epsilon (scaled to mesh bounds), prefer the front-facing hit, then the nearest.
- **Misses.** No hit within `maxDist` ⇒ leave the texel **unwritten** (mark it) and fill later by
  **dilation** ([`05`](05-uvs-and-dilation.md)). Never write a default normal into a miss.

### 3.1 Nearest-point fallback (from the original algorithm)
Cignoni et al. (1998), the source algorithm for high→low attribute baking, noted that casting strictly
along the surface normal can simply have **no intersection**, and therefore sampled the
**nearest point on the high-poly** as the correspondence instead.[^cignoni] A robust baker keeps this
as a fallback: when a texel's ray misses but the high-poly clearly passes nearby (within the
envelope), snap to the closest high-poly point rather than leaving a hole. This trades a small amount
of skew for far fewer holes on awkward geometry.

## 4. Projection artifacts (caused here, often *seen* in [`07`](07-quality-artifacts-and-aa.md))

| Symptom | Cause | Fix |
|---|---|---|
| **Holes / black speckle** | envelope too small; high-poly pokes outside the cage; ray misses | increase frontal/rear distance or balloon the cage; enable nearest-point fallback |
| **Skewed / "swimming" normals** | ray direction off-axis (raw face normals at hard edges) | use averaged normals or an explicit cage; Paint Skew locally |
| **Bleeding between parts** | a ray from one part hits an unrelated nearby part | per-part cages + **Match By Name** isolation; tighter `maxDist` |
| **Wavy gradients on flats** | mismatched/intersecting cage | smooth the cage; reduce excessive offset |
| **Far-wall pickup in tubes** | nearest hit is a backface across a gap | enable Ignore Backfaces |

## 5. Recommendations for NormalBake

- Support **both** envelope modes: distance-based `(frontal, rear)` for the common case, and an
  **explicit cage mesh** (same topology/UVs as the low-poly) for hard cases.
- Default ray direction to **averaged vertex normals** (continuous across hard edges).
- Default hit policy: **closest front-most within `maxDist`, two-sided intersection + backface
  rejection toggle**, with a **nearest-point fallback** to kill holes.
- Support **per-part isolation** (name-suffix matching) so multi-part assets don't bleed.
- Derive `maxDist` from the envelope and pass it to the BVH as the ray's `t_max` — short rays are the
  baker's single biggest traversal speedup ([`06`](06-acceleration-and-performance.md)).

---

[^marmoset-cage]: Marmoset Toolbag — Cage Mesh (cages bound ray distance; Paint Skew). https://docs.marmoset.co/docs/cage/
[^marmoset-proj]: Marmoset Toolbag — Projection Tools. https://docs.marmoset.co/docs/projection-tools/
[^substance-mm]: Adobe Substance 3D — Mesh map settings (averaged normal; Max Frontal/Rear Distance). https://helpx.adobe.com/substance-3d-painter/baking/mesh-map-settings.html · mirror: https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/baking/how-to-bake-mesh-maps
[^substance-match]: Adobe Substance 3D — Matching by name. https://helpx.adobe.com/substance-3d-bake/features/matching-by-name.html
[^blender-bake]: Blender Manual — Render Baking (Extrusion, Ray Distance, Cage). https://docs.blender.org/manual/en/2.80/render/cycles/baking.html
[^cignoni]: Cignoni, Montani, Rocchini, Scopigno, *A general method for preserving attribute values on simplified meshes*, IEEE Vis 1998 (nearest-point sampling). https://ieeexplore.ieee.org/document/745285/
