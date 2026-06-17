# Tangent Space & MikkTSpace — the #1 thing to get right

> A tangent-space normal map decodes correctly **only if the renderer reconstructs the exact same
> tangent frame the baker used.** Mismatched tangents are the single most common cause of "my bake
> looks subtly wrong / has seams" — more than cages, more than UVs. This document explains the frame,
> the classic math, and why **MikkTSpace** is the standard that makes baker and renderer agree.

## 1. The TBN frame

At any surface point, the **tangent frame** is a basis of three vectors:

- **T** — *tangent*, pointing along increasing **U** (texture right).
- **B** — *bitangent*, pointing along increasing **V** (texture up).
- **N** — the surface *normal* (out of the surface).

Stacked as columns, `[T B N]` is the **TBN matrix**: it maps a tangent-space vector into
object/world space. At **bake time** we need the opposite direction — world → tangent — to express
the high-poly's normal in the low-poly's frame. If TBN is orthonormal, that inverse is just the
**transpose**.[^learnopengl] In the bake pseudocode this is the three dot products in step (F):
projecting the high-poly normal onto T, B, and N.

A flat tangent-space normal — one that points straight out — is `(0,0,1)`, which encodes to
`RGB(128,128,255)`, the familiar "bluish" look of a normal map.

## 2. Three storage spaces (and why baking defaults to tangent space)

| Space | Encoding relative to | Look | Deformable? | Tiling / mirroring? | Use |
|---|---|---|---|---|---|
| **Tangent** | per-pixel TBN frame | mostly blue | ✅ yes | ✅ yes | **default for baked game assets** |
| **Object** | model's local frame | rainbow | ❌ breaks under animation | ❌ no | static, unique-UV props; no runtime TBN needed |
| **World** | world frame | rainbow | ❌ only fully static | ❌ no | rare; debug/intermediate |

Tangent space wins for baked assets because the map is **reusable on deforming/animated meshes** and
on **tiled or mirrored UVs**, and is robust to the object being moved.[^3dcoat] Object space avoids an
entire class of tangent-basis mismatch (no runtime TBN at all) and can be marginally cheaper, but
**breaks under deformation and cannot be tiled or mirrored** — so NormalBake treats tangent space as
the primary output and object space as a secondary option. (Conversion between them: see
[`08-prior-art-tools-and-libraries.md`](08-prior-art-tools-and-libraries.md), "Handplane.")

## 3. The classic per-vertex tangent math (Lengyel)

Eric Lengyel's method is the canonical way to derive per-vertex tangents from UVs.[^lengyel] Per
triangle, relate the two edge vectors and their UV deltas to T and B, accumulate per shared vertex,
then orthonormalize:

```text
# edges and uv deltas relative to P0
(x1,y1,z1) = P1-P0 ;  s1=u1-u0, t1=v1-v0
(x2,y2,z2) = P2-P0 ;  s2=u2-u0, t2=v2-v0

r = 1 / (s1*t2 - s2*t1)
sdir = ( (t2*x1 - t1*x2)*r, (t2*y1 - t1*y2)*r, (t2*z1 - t1*z2)*r )   # tangent dir
tdir = ( (s1*x2 - s2*x1)*r, (s1*y2 - s2*y1)*r, (s1*z2 - s2*z1)*r )   # bitangent dir

# accumulate sdir->tan1[v], tdir->tan2[v] for the 3 verts; then per vertex v with normal n:
tangent[v].xyz = normalize( tan1[v] - n * dot(n, tan1[v]) )          # Gram-Schmidt vs normal
tangent[v].w   = ( dot( cross(n, tangent[v].xyz), tan2[v] ) < 0 ) ? -1 : +1   # handedness
# bitangent reconstructed at runtime:
B = tangent[v].w * cross(N, tangent[v].xyz)
```

LearnOpenGL gives the equivalent edge form `f = 1/(dU1·dV2 − dU2·dV1)`,
`T = f·(dV2·E1 − dV1·E2)`, `B = f·(dU1·E2 − dU2·E1)`.[^learnopengl]

**Limitations Lengyel himself flags:** after per-vertex averaging the tangents are *not* generally
perpendicular to each other or to the normal, so the TBN inverse is not exactly its transpose (hence
the Gram-Schmidt step, and reconstructing B as `w·(N×T)` rather than storing it). Degenerate UVs make
`r → ∞`; mirrored UVs flip handedness (the `w` sign exists precisely to encode that); and the
**averaging/seam behavior is implementation-specific** — which is exactly why two programs that each
roll their own tangent code will disagree.[^lengyel]

## 4. MikkTSpace — the standard that fixes the disagreement

**MikkTSpace** is Morten S. Mikkelsen's standalone, two-file (`mikktspace.h` / `mikktspace.c`)
reference implementation of a per-vertex tangent algorithm.[^mikk-repo] It became the de-facto
industry standard because it computes a tangent basis **completely independent of vertex/face
ordering** (important for mirroring normal maps across a seam) and was explicitly designed "to make
the generation of tangent space as resilient as possible to a 3D model being moved from one
application to another," making it "a perfect candidate for an implementation standard."[^mikk-thesis][^polycount-tech]

**What the reference guarantees** (from the header):[^mikk-header]
- **Order independence** — faces and their vertices can arrive in any order, same result.
- Internal vertex welding.
- **Degenerate primitives don't pollute results** — they inherit neighbors' tangent space.
- Consistent triangle **and** quad handling, provided **quads split along the shortest diagonal**
  (order-independent, mirror-safe).

### 4.1 The baker == renderer rule (the whole point)

MikkTSpace specifies an exact **runtime** convention, and the baker must mirror it. Per Mikkelsen
(as documented in Blender's port): the **vertex shader keeps the vertex normal and tangent
normalized**, but the **pixel shader decodes the tangent-space normal using the *unnormalized*,
interpolated vertex normal and tangent**, reconstructing the bitangent as `B = sign · cross(N, T)`
from the stored handedness sign.[^blender-mikk]

Polycount states the consequence bluntly: *"the shader must use the same tangent basis as the normal
map baker, otherwise you'll get incorrect lighting, especially across the seams between UV shells,"*
and *"a normal map baked in one application probably won't shade correctly in another."*[^polycount-tech]

### 4.2 What this means concretely for NormalBake

1. Compute the low-poly's per-vertex tangents with a **MikkTSpace-compatible** algorithm (port it, or
   use a managed reimplementation), on the **same shortest-diagonal triangulation** used to rasterize.
2. Bake using those tangents, decoding/encoding with the **unnormalized interpolated** N and T and
   `B = w·(N×T)` — matching the renderer's per-pixel reconstruction (see bake step (F) in
   [`02-baking-algorithm.md`](02-baking-algorithm.md)).
3. **Emit the tangents with the output mesh** so the consuming engine uses the same basis. glTF stores
   `TANGENT` as a `vec4` (xyz + `w` handedness) for exactly this reason. Document that the consumer
   must use a MikkTSpace-compatible basis.

This is the difference between a map that's portable and one that only looks right in the tool that
made it.

### 4.3 License (good news)

The MikkTSpace reference is **zlib-licensed** — permissive, MIT-compatible, commercial use allowed.
Verbatim from the header: *"Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter and redistribute it freely, subject to the following
restrictions: 1. The origin of this software must not be misrepresented … 2. Altered source versions
must be plainly marked as such … 3. This notice may not be removed or altered."*[^mikk-header]

For a pure-C# project you may **port the algorithm** (keeping the notice and not misrepresenting
origin) or call a managed reimplementation; the zlib terms permit both, and this is compatible with
NormalBake's MIT-clean stance. A faithful C# port (validated against the C reference's output, the way
Quadremesh validates against QuadriFlow) is the recommended path — it keeps the "no native deps" rule.

## 5. Checklist

- [ ] One deterministic, shortest-diagonal triangulation, shared by tangent solver and rasterizer.
- [ ] MikkTSpace-compatible per-vertex tangents with handedness `w`.
- [ ] Bake decode/encode uses unnormalized interpolated N, T; `B = w·(N×T)`.
- [ ] Output mesh carries the tangents (e.g. glTF `TANGENT` vec4).
- [ ] Documented requirement: consumer engine uses a MikkTSpace-compatible basis.

---

[^learnopengl]: LearnOpenGL — Normal Mapping (TBN, transpose for world→tangent, edge-form tangents). https://learnopengl.com/Advanced-Lighting/Normal-Mapping
[^3dcoat]: 3DCoat — Types of Normal Maps (tangent vs object vs world; deformation & tiling tradeoffs). https://3dcoat.com/documentation/2025/01/13/types-of-normal-maps/
[^lengyel]: Eric Lengyel — Computing Tangent Space Basis Vectors for an Arbitrary Mesh (formulas, handedness `w`, stated limitations). https://terathon.com/blog/tangent-space.html
[^mikk-repo]: MikkTSpace repository. https://github.com/mmikk/MikkTSpace
[^mikk-header]: `mikktspace.h` — guarantees and zlib license. https://github.com/mmikk/MikkTSpace/blob/master/mikktspace.h
[^mikk-thesis]: M. S. Mikkelsen, *Simulation of Wrinkled Surfaces Revisited* (MSc thesis, 2008). PDF: http://image.diku.dk/projects/media/morten.mikkelsen.08.pdf · abstract: https://core.ac.uk/outputs/103577394/
[^polycount-tech]: Polycount wiki — Normal Map Technical Details (baker/renderer must share the tangent basis). http://wiki.polycount.com/wiki/Normal_Map_Technical_Details
[^blender-mikk]: Blender — Tangent Space Normal Maps (Mikkelsen's pixel-shader decode convention). https://archive.blender.org/wiki/2015/index.php/Dev:Shading/Tangent_Space_Normal_Maps/
