# UVs & Dilation — the input precondition and the output skirt

> **Read this first:** a tangent-space normal baker **requires the low-poly to already have a UV
> layout.** Baking writes the captured normal into *the texel addressed by the low-poly's UV
> coordinate* — no UVs, no texel address, nothing to write. This is a hard input precondition, not a
> quality knob.

## 1. UVs are a required input — a sister project produces them

Auto-retopology engines — QuadriFlow, Instant Meshes, and Quadremesh — emit clean quad **topology**
but **no texture parameterization**; UV unwrapping is a separate downstream step. Commercial Quad
Remesher describes its output as *"ready for proper UV mapping"* — i.e. topology *suitable for*
unwrapping, not a finished UV set.[^qr-faq] QuadriFlow outputs no UVs at all.

**Decision: NormalBake requires the low-poly to arrive already UV-unwrapped.** Producing those UVs is
the job of a **separate sister project** (an xatlas-class auto-unwrapper), keeping NormalBake focused
on baking. The toolchain is therefore three stages: **Quadremesh** (retopology) → **UV-unwrap tool**
(sister project) → **NormalBake** (this tool). NormalBake's responsibility at the UV boundary is to
**consume and validate** the incoming UVs, not to create them.

The unwrap tool still has to satisfy a contract, and NormalBake should **check it and fail fast** —
these are the requirements at the UV boundary:

- a UV set that is **non-overlapping in the active tile** (§2);
- **UV seams at every hard edge** (§2, and [`07`](07-quality-artifacts-and-aa.md) §3);
- **inter-island gutters ≥ 2× the dilation padding** (§4);
- a sensible, consistent **texel density** (§2); and
- a **triangulation** NormalBake can reproduce exactly — it owns triangulation + MikkTSpace tangents at
  bake time ([`02`](02-baking-algorithm.md) §4, [`03`](03-tangent-space-and-mikktspace.md)), so the
  unwrap tool should either emit triangles or a deterministic, shortest-diagonal-compatible quad mesh.

(If the two tools are ever fused, NormalBake could bundle the unwrapper — **xatlas** is the MIT-clean
candidate, *"a small C++11 library … that generates unique texture coordinates suitable for baking,"*
pipeline chart→parameterize→pack[^xatlas]; see [`08`](08-prior-art-tools-and-libraries.md) §3.2 — but
that is explicitly out of scope here.)

## 2. UV requirements for a clean bake

- **Non-overlapping within the active tile.** Only the **0–1 UV tile is baked**; anything outside it is
  ignored. If two shells occupy the same 0–1 region, projection rays hit both stacked shells and you get
  z-fighting / ray conflicts.[^poly-overlap] For a *unique* bake, islands must not overlap inside the
  active tile.
- **UV seams required at every hard edge.** *"For tangent-space normal mapping, split the UVs for every
  hard edge … Hard edges need to be separated in UV space, to allow the tangent basis to twist around
  appropriately."*[^poly-nm][^poly-sg] This is the crux of the seam-free rule in
  [`07`](07-quality-artifacts-and-aa.md) §3.
- **Texel density** = texture pixels per unit of real surface (e.g. 100 cm on a 256-px texture →
  2.56 px/cm). Keep it consistent so neighbouring assets match in sharpness; common targets ≈512 px/m
  (third-person), ≈1024 px/m (first-person).[^poly-texel][^poly-td] Set at unwrap/pack time.

### 2.1 Mirrored / stacked / UDIM shells
- **Mirrored / stacked UVs.** Standard workflow: **offset the duplicated shell by exactly one full UDIM
  tile (1.0 in U), bake, then move it back** to overlap — because the baker only samples the 0–1 tile,
  only one copy is baked and the mirror reuses those texels.[^poly-overlap][^poly-mirror] Caveat:
  mirroring puts a **tangent-space seam down the mirror line** (handedness flips), which the `w` sign
  and a synced basis must handle.[^poly-mirrorseam]
- **UDIM** (tile index `1001 + u + 10·v`): iterate **per tile** — offset each tile's UVs into 0–1 and
  bake separately.

**Design:** support a per-shell **bake / reuse** flag and an integer **UDIM offset** so callers can
stack instances without the baker double-sampling, plus **per-tile** iteration for UDIM.

## 3. Why dilation (edge padding) is mandatory

After baking, island colours must be **dilated outward past every UV island edge**, because GPU
**bilinear filtering and mipmapping sample beyond island borders**; without a skirt of matching colour,
the empty gutter bleeds in and creates **seams**.[^poly-edgepad][^blender-bake][^adobe-dilate]

**The mip mechanism (why a fixed pixel count matters).** Each mip halves resolution, so the *effective*
gutter shrinks with it; padding must be wide enough to survive several mip levels. Roughly **N px of
padding survives ~log₂(N) mip levels** before the seam returns (~12 px clears mip bleed on 1024²;
~6 px on 512²).[^rageworks]

## 4. Dilation: amounts, algorithm, and ordering

### 4.1 Recommended padding by resolution (polycount table)
| Texture res | Edge padding |
|---|---|
| 256 | **2 px** |
| 512 | **4 px** |
| 1024 | **8 px** |
| 2048 | **16 px** |
| 4096 | **32 px** *(extrapolated/forum; not in the wiki table)* |

**Gutter between islands should be ≥ 2× the padding** so two opposing skirts don't collide.[^poly-edgepad]
(The 4096→32 figure is forum-sourced, flagged.)

### 4.2 How it works, and the one variant a normal baker must use
Dilation *"duplicates the pixels along the inside of the UV edge and spreads those colors outward,
forming a skirt"* — i.e. a **flood-fill / radial dilation from boundary pixels.**[^poly-edgepad] There
are two margin algorithms, and **only one is correct for tangent-space normals**:

- **Extend** — pure radial flood-fill of border pixels outward. **Use this.**
- **Adjacent Faces** — fills the margin with pixels from the face on the *other* side of the UV seam.
  Default in Blender ≥3.1 for *colour* maps, but **WRONG for tangent-space normals**: those normals are
  relative to each island's UV frame, so copying values across a UV boundary introduces artifacts.
  Blender devs explicitly say *"always use 'extend' in the specific case of normal baking in tangent
  space."*[^blender-margin][^blender-issue]

So **NormalBake's dilation must be Extend / flood-fill, never cross-seam copy.**

**Infinite vs fixed.** Substance dilates *"until a pixel reaches another UV island or the borders of
the texture"*; Marmoset offers presets up to **Extreme** plus a custom pixel value.[^adobe-dilate][^marmoset-bake]
**Recommendation:** default to **flood-fill-to-collision (infinite) Extend** when gutters are adequate;
expose a fixed-pixel override using the table above.

### 4.3 Ordering with anti-aliasing (gotcha)
Supersampling at island borders can pull empty-gutter colour into the average, producing dark streaks
in padded pixels.[^poly-bake] **The order must be:** track a per-texel **coverage / hit mask** →
average **only hit samples** → **then dilate** from the covered region. Never dilate before resolving
AA, and never average background into edge texels. (AA details: [`07`](07-quality-artifacts-and-aa.md) §1.)

## 5. Checklist for NormalBake

- [ ] **Require UVs as input** (a sister tool produces them); validate the UV contract — non-overlap,
      seams at hard edges, gutter ≥ 2× padding, sane texel density — and **fail fast** on violations.
- [ ] Enforce non-overlap in the active tile; support per-shell bake/reuse + UDIM offset + per-tile iteration.
- [ ] Place UV seams at hard edges (the seam-free rule, [`07`](07-quality-artifacts-and-aa.md) §3).
- [ ] **Extend / flood-fill** dilation only — never adjacent-faces cross-seam copy.
- [ ] Default infinite (to-collision) padding; fixed-pixel override per the resolution table; gutter ≥ 2× padding.
- [ ] Resolve AA over a coverage mask **before** dilating.

---

[^qr-faq]: Quad Remesher FAQ (topology "ready for proper UV mapping"). https://www.blendernation.com/2019/10/18/quad-remesher-auto-retopologizer-for-blender-unofficial-faq/ · https://80.lv/articles/quad-remesher-for-blender-released
[^xatlas]: xatlas — unique UVs for baking; chart→parameterize→pack; packer knobs. https://github.com/jpcy/xatlas
[^poly-overlap]: Polycount — Overlapping UVs with a normal map (only 0–1 tile baked; offset-one-tile trick). https://polycount.com/discussion/102872/overlapping-uvs-with-a-normal-map
[^poly-nm]: Polycount wiki — Normal map (split UVs at every hard edge). http://wiki.polycount.com/wiki/Normal_map
[^poly-sg]: Polycount wiki — Smoothing Groups (hard edges separated in UV space). http://wiki.polycount.com/wiki/Smoothing_Groups
[^poly-texel]: Polycount wiki — Texel (definition; 2.56 px/cm worked example). http://wiki.polycount.com/wiki/Texel
[^poly-td]: Polycount — Texel Density standards. https://polycount.com/discussion/227863/texel-density-5-12-10-24 · https://www.beyondextent.com/deep-dives/deepdive-texeldensity
[^poly-mirror]: Polycount — A solution to normal maps with mirrored UVs. https://polycount.com/discussion/116922/
[^poly-mirrorseam]: Polycount — Normal mapping seam on mirrored UVs. https://polycount.com/discussion/169594/
[^poly-edgepad]: Polycount wiki — Edge padding (why; px table; gutter 2×; flood-fill; tool list). http://wiki.polycount.com/wiki/Edge_padding
[^blender-bake]: Blender Manual — Render Baking (margin avoids seam/mip discontinuities). https://docs.blender.org/manual/en/latest/render/cycles/baking.html
[^adobe-dilate]: Adobe — Texture dilation / Padding (dilate until island/border). https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/technical-support/workflow-issues/export-issues/texture-dilation-or-padding
[^rageworks]: RageWorks — Mip mapping and the importance of pixel padding. https://therageworks.wordpress.com/2013/02/28/mip-mapping-and-the-importance-of-pixel-padding/
[^blender-margin]: Blender — bake margin types (Extend vs Adjacent Faces). https://docs.blender.org/api/current/bpy_types_enum_items/bake_margin_type_items.html
[^blender-issue]: Blender issue #96977 — use "Extend" for tangent-space normal baking. https://projects.blender.org/blender/blender/issues/96977
[^marmoset-bake]: Marmoset — Setting up a Bake Project (Padding presets). https://docs.marmoset.co/docs/setting-up-a-bake-project/
[^poly-bake]: Polycount wiki — Texture Baking (supersampling streaks in padding; bake-big-downsample). http://wiki.polycount.com/wiki/Texture_Baking
