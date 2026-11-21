# Quality, Artifacts & Anti-Aliasing

This document covers the quality passes (**anti-aliasing**, **mip/specular-AA**), the **artifact
taxonomy** with fixes, the **seam-free correctness rules**, and **QC/validation**. Projection-specific
artifacts (skew, holes, cage self-intersection) are introduced in [`04`](04-cage-and-projection.md);
here we focus on what they *look like*, how to prevent them, and how to verify a finished map.

## 1. Anti-aliasing

**Why baked normals alias badly.** Each texel is a *point sample* of one ray cast at the high-poly.
Along the high-poly silhouette (where it overlaps itself) and along hard-edge / UV-island borders,
single-sample baking produces jagged stair-stepping. Multi-ray sampling fixes it.[^poly-bake]

**Supersampling (SSAA N×N) is the standard mechanism** — cast N×N rays per texel and average:
- **Substance:** Subsampling **2×2 … 8×8** ("faster but may produce less accurate results … 8×8 also
  increases bake time").[^adobe-mm]
- **Marmoset:** Samples Per Pixel **1×/2×/4×/8×/16×/32×** (64× added in 3.05 "for challenging areas such
  as acute lines and fine detail").[^marmoset-bake]

Cost scales **N²** (4×4 = 16 rays/texel, 8×8 = 64). Distribution can be a regular grid or a
**rotated/jittered grid** (better edge AA per sample). A trivial first implementation — the
"poor-man's SSAA" — is to **bake at 2× target resolution then downscale 50%**, which is exactly
box-filter 2×2 SSAA.[^poly-bake] **Raytraced/analytic** AA modes exist (Substance) but SSAA dominates
because it is robust against arbitrary ray-hit discontinuities.

**Two gotchas (both feed the implementation order):**
1. **AA × dilation.** Averaging samples at an island border pulls in empty-gutter colour → dark
   streaks. **Order: track a coverage/hit mask → average only hit samples → then dilate**
   ([`05`](05-uvs-and-dilation.md) §4.3).[^poly-bake]
2. **AA × ID/colour maps.** Disable AA (Samples = 1×) for **ID / object / material** maps so the baker
   doesn't blend two ID colours into a third nonexistent one at boundaries.[^marmoset-tips]

**For NormalBake:** default SSAA **≥4×4** for normals, jittered grid, averaging **only hit samples**
over a coverage mask, then dilate. Expose `--samples`. Start with bake-big-downsample, then move to
per-texel sub-ray sampling.

## 2. Mip-mapping & specular anti-aliasing

Naively box-averaging and renormalizing normals across a mip level **discards sub-texel normal
variance**, so a region of many divergent micro-normals collapses to one smooth normal with a sharp
highlight that **shimmers/aliases** as the surface moves. Standard MSAA/post-AA can't fix it — the
aliasing is in the BRDF response, not geometric edges.[^selfshadow]

The unifying fix is **variance → roughness**: fold the lost normal spread into the roughness/gloss used
at that mip, broadening the highlight instead of aliasing it.
- **Toksvig (2004):** don't renormalize the averaged normal — its **length shrinks with divergence**;
  use that length to lower the specular exponent. Cheap; can be **baked into a gloss/roughness map** at
  mip-gen time.[^toksvig][^selfshadow]
- **LEAN / CLEAN (2010–11):** store first+second moments of the normal distribution for **anisotropic**
  highlight broadening; linearly mippable; higher storage.[^selfshadow]
- **vMF / Karis (UE4):** model the normal lobe as a von Mises–Fisher distribution → roughness increase
  ("Geometric Specular AA").[^karis]

**For NormalBake (roadmap):** if NormalBake generates mip chains (or feeds an engine that does), at
minimum preserve un-normalized averaged normals or **emit a Toksvig-derived roughness term** per mip so
downstream rendering gets specular AA for free; document that consumers should use Toksvig/LEAN/vMF mip
filtering rather than naive box-mip + renormalize.

## 3. The seam-free correctness rules

These are the highest-leverage rules; most "my bake has seams" reports trace here.

### 3.1 The rule: 1 smoothing group + UV seams on hard edges
With a **synced tangent basis** you can use **a single smoothing group** over the whole mesh and place
**UV seams at the sharp angles**; the tangent-space normal map then **compensates for the low-poly
faceting** so the surface shades smooth.[^poly-hard] Marmoset: *"Use hard edges along your UV splits …
your normal map has to do less heavy lifting … You should have a UV seam where you have a hard
edge."*[^marmoset-tips] (Splitting UVs creates coincident vertices that break tangency, so a seam is
*unavoidable* at a UV split — which is why hard edges and UV seams must coincide.)[^poly-tech]

### 3.2 Why it only works with a synced basis
Tangent-space normals shade correctly **only when the baker and renderer compute the tangent basis
identically**: *"the shader must use the same tangent basis as the normal map baker, otherwise you'll
get incorrect lighting, especially across the seams between UV shells."*[^poly-tech] The de-facto
standard is **MikkTSpace** (Unity, Unreal, xNormal, Substance, Toolbag, glTF). When the basis is
synced, the gradient the map encodes at a hard-edge seam is reproduced exactly by the renderer, so the
faceting cancels and the seam disappears. Full treatment: [`03`](03-tangent-space-and-mikktspace.md).

### 3.3 Triangulation must be locked
*"A triangulation mismatch causes x-shaped smoothing errors … essentially a type of tangent-space
mismatch."*[^marmoset-tips] Because the low-poly is **quads from a remesher**, NormalBake must
**triangulate deterministically (shortest-diagonal, MikkTSpace-consistent) and bake against that exact
triangulation**, then export triangles or guarantee the consumer triangulates identically
([`02`](02-baking-algorithm.md) §4).

### 3.4 Handedness / green channel (the most common "looks wrong")
Y+ (OpenGL: Blender, Maya, Modo, Marmoset, Unity) vs Y− (DirectX: 3ds Max, CryEngine, Source, Unreal).
*"Handedness is perhaps the most common issue artists face."*[^marmoset-tan] Expose a green-channel
flip (default OpenGL/Y+, DirectX preset); see [`01`](01-normal-mapping-fundamentals.md) §4.

## 4. Artifact taxonomy (symptom → cause → fix)

| Symptom | Cause | Fix |
|---|---|---|
| **Skewed / wavy / "swimming" normals** | low-poly averaged normals not ⊥ to surface; too few sides to track high-poly curvature → off-axis capture[^80lv][^poly-wavy] | add supporting geometry; **face-weighted vertex normals**; smoothed cage / **skew painting** |
| **Holes / ray misses** | split vertex normals + distance raycast miss the high-poly; envelope too small | **smoothed cage** (continuous direction across hard edges); tune max distance; nearest-point fallback; flat-blue + no-hit mask |
| **Wrong-surface pickup** | max distance too large → grabs adjacent part (e.g. neighbouring finger) | shrink max distance; per-part **name-match** isolation |
| **Cage self-intersection** | inflated cage crosses itself on concave regions | local cage offset (Paint Offset); per-region max distance |
| **Seams at UV/smoothing/tangent splits** | unsynced basis, or hard edge without UV seam | MikkTSpace sync; **UV seam at every hard edge** (§3) |
| **X-shaped shading errors** | triangulation mismatch baker↔renderer | lock triangulation (§3.3) |
| **Inverted bumps** | wrong green-channel convention | flip Y (§3.4) |
| **Proximity bleed** | interpenetrating/close parts capture each other | **match by mesh name** (`_low`/`_high`); per-part cages[^poly-nm] |
| **Banding on smooth gradients** | 8-bit precision | bake/store 16-bit; BC5 for delivery ([`01`](01-normal-mapping-fundamentals.md) §5) |
| **Dark streaks in padding** | AA averaged background into edge texels | coverage mask; dilate after AA ([`05`](05-uvs-and-dilation.md) §4.3) |

**Skew painting (the targeted cage fix).** Marmoset's skew maps *"create a per-pixel blend between the
face normals of the low-poly mesh and the cage normals"* — black = full correction, white = none —
letting you "massage the direction to fix projection errors" without remodeling.[^marmoset-proj]

## 5. QC / validation

- **Flat-bluish appearance (tangent space).** A correct tangent-space map is predominantly blue-purple;
  the neutral normal is **RGB(128,128,255)** = (0,0,1). Use **(128,128,255), not (127,127,255)**, for
  the flat value to avoid mirror seams.[^poly-nm][^danthree] **Histogram check:** R and G centred ≈128,
  B high. A *rainbow* result means it's object-space, not tangent-space.
- **Channel / swizzle checks.** Verify R=X, G=Y, B=Z and the correct green-channel handedness for the
  target engine; B should be near-white everywhere (mostly outward), R/G encode tilt.[^danthree]
- **Re-render comparison (authoritative).** Apply the map to the low-poly, light it, and compare the
  shaded result against the high-poly and against a **reference baker**. Seams that survive a
  synced-basis re-render indicate a genuine bake error (ray miss / cage / padding), not a viewer
  mismatch.[^marmoset-tut]
- **Golden vs reference baker.** Use a golden-image discipline: a deterministic oracle (Blender
  headless) produces golden maps; CI diffs NormalBake's output per-texel + SSIM/PSNR + a ꟻLIP error
  map. All major bakers use MikkTSpace, so a synced NormalBake should match within AA/dither noise;
  large per-channel deltas localize the bug (handedness, triangulation, cage, padding). Details:
  [`08`](08-prior-art-tools-and-libraries.md) §4.

## 6. Other maps in the same projection pass (roadmap)

The single-ray projection core is reused, capturing a different quantity:[^marmoset-maps][^adobe-bakers]

- **Same single-ray core (cheap fast-follow):** **Position / world-position**, **Object/Material ID**
  (bake AA off), **object-space normal**.
- **Needs a hemisphere ray sampler (secondary rays — larger phase):** **Ambient Occlusion**, **Thickness**
  (inward rays), **Bent Normals** (average unoccluded direction).
- **Post-process over the normal/height result:** **Curvature**, **Height/Displacement**, **Derivative**.

So **Normal + Position + ID** are the natural MVP+1; AO/Thickness/Bent are phase 2; Curvature/Derivative
are cheap post-passes.

---

[^poly-bake]: Polycount wiki — Texture Baking (SSAA; bake-big-downsample; AA×padding streaks). http://wiki.polycount.com/wiki/Texture_Baking
[^adobe-mm]: Adobe — Mesh map settings (Subsampling 2×2…8×8). https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/baking/mesh-map-settings
[^marmoset-bake]: Marmoset — Baking (Samples Per Pixel). https://marmoset.co/toolbag/baking/ · https://marmoset.co/posts/free-update-toolbag-3-05/
[^marmoset-tips]: Marmoset — Tips & Tricks (UV seam = hard edge; triangulate first; ID AA off). https://docs.marmoset.co/docs/tips-tricks/
[^selfshadow]: Self Shadow — *Specular Showdown in the Wild West* (normal-map mip filtering / specular AA survey). https://blog.selfshadow.com/2011/07/22/specular-showdown/
[^toksvig]: M. Toksvig — *Mipmapping Normal Maps* (NVIDIA, 2004). https://www.researchgate.net/publication/220494180_Mipmapping_Normal_Maps
[^karis]: B. Karis — Normal map filtering using vMF / Geometric Specular AA. http://graphicrants.blogspot.com/2018/05/normal-map-filtering-using-vmf-part-3.html
[^poly-hard]: Polycount — hard edges / UVs / normal maps / vertex counts (1 smoothing group + seams). https://polycount.com/discussion/107196/
[^poly-tech]: Polycount wiki — Normal Map Technical Details (baker/renderer must share basis; UV split = seam). http://wiki.polycount.com/wiki/Normal_Map_Technical_Details
[^marmoset-tan]: Marmoset — Tangents & Handedness. https://docs.marmoset.co/docs/tangent-handedness/
[^80lv]: 80.lv — How Normal Maps Work & the Baking Process (skew). https://80.lv/articles/tutorial-how-normal-maps-work-baking-process
[^poly-wavy]: Polycount — Understanding averaged normals and ray projection (waviness). https://polycount.com/discussion/81154/
[^marmoset-proj]: Marmoset — Projection Tools (skew painting). https://docs.marmoset.co/docs/projection-tools/
[^poly-nm]: Polycount wiki — Normal map (flat = 128,128,255; proximity bleed). http://wiki.polycount.com/wiki/Normal_map
[^danthree]: danthree studio — Normal Map glossary (channel/QC checks). https://www.danthree.studio/en/glossary/normal-map
[^marmoset-tut]: Marmoset — Baking Tutorial (bake + lit preview validation). https://marmoset.co/posts/toolbag-baking-tutorial/
[^marmoset-maps]: Marmoset — Map Types. https://docs.marmoset.co/docs/map-types/
[^adobe-bakers]: Adobe Substance 3D — Bakers. https://helpx.adobe.com/substance-3d-bake/home.html
