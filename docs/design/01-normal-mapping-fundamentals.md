# Normal Mapping Fundamentals — the map as data

This document covers what a normal map *is* and how it is **encoded, stored, and compressed** — the
data artifact NormalBake produces. The geometry of *how we fill it* is in
[`02-baking-algorithm.md`](02-baking-algorithm.md); the tangent frame it's relative to is in
[`03-tangent-space-and-mikktspace.md`](03-tangent-space-and-mikktspace.md).

## 1. What a normal map is

A **normal map** is a texture whose texels store **surface normals** (unit 3D directions) instead of
colour. At render time, the shader reads the texel, decodes it back to a direction, and uses that as
the per-pixel surface normal in the lighting equation — so a flat low-poly triangle can be lit as if
it had the fine bumps and curvature of the original high-poly. The idea was introduced as
**"Appearance-Preserving Simplification"** (Cohen, Olano & Manocha, SIGGRAPH 1998), which coined the
term by *"decoupling"* surface attributes and *"storing the colors and normals in texture and normal
maps."*[^cohen] (Full academic lineage in [`references.md`](references.md).)

## 2. The three spaces (summary)

A normal can be stored relative to different frames; full treatment in
[`03`](03-tangent-space-and-mikktspace.md). In brief:

- **Tangent space** — relative to the per-pixel TBN frame. *Mostly blue.* Deformable, tileable,
  mirrorable. **NormalBake's default.**
- **Object space** — relative to the model origin. *Rainbow.* No runtime tangent basis needed (so no
  baker/renderer tangent mismatch), but breaks under deformation and can't tile/mirror.
- **World space** — relative to the world. Rare; only valid for fully static geometry.

## 3. Encoding: unit normal ⇄ RGB

A unit normal has components in `[-1, 1]`; an unsigned texture channel holds `[0, 1]`. The standard
range remap is per-channel:

```text
encode:  rgb = n * 0.5 + 0.5        # n in [-1,1]  ->  rgb in [0,1]   (X->R, Y->G, Z->B)
decode:  n   = normalize(rgb*2 - 1)
```

So a tangent-space normal pointing straight out, `(0,0,1)`, encodes to `(0.5, 0.5, 1.0)` =
**RGB(128,128,255)** — the familiar flat "light-blue" of a neutral normal map.[^learnopengl]

**Renormalize on decode — always.** Bilinear filtering and mip averaging interpolate *between* unit
vectors, and the linear interpolant is shorter than unit length; 8-bit quantization also perturbs
length. Skipping the `normalize` over-darkens (|n|<1) or over-brightens (|n|>1) the lighting.[^poly-renorm]

### 3.1 Two-channel storage + Z reconstruction
For **tangent-space** maps, the basis guarantees `Z ≥ 0`, so you can store only X and Y and
reconstruct Z in the shader:

```text
z = sqrt( saturate(1 - x*x - y*y) )      # clamp radicand >= 0
n = normalize( float3(x, y, z) )
```

This (a) frees a channel, enabling 2-channel **BC5** compression (§5), and (b) is *more accurate*
after compression, because the reconstructed Z stays exactly consistent with the (independently
compressed) X and Y instead of drifting.[^unity-recz][^nvidia-nc]

## 4. The two conventions and the one rule everyone forgets

### 4.1 OpenGL (+Y) vs DirectX (−Y) — green channel only
The two normal-map conventions differ **only in the sign of the green (Y) channel**:

- **OpenGL:** Y+ — green points up. Used by Blender, Unity, Godot, most GL engines.
- **DirectX:** Y− — green inverted. Used by Unreal, 3ds Max, Source.

Apply the wrong one and lighting inverts — *bumps become dents.*[^adobe-gldx] Red (X) and blue (Z)
are identical between conventions; conversion is a single green-channel flip. NormalBake must expose a
**Y-flip / convention flag** and label its default clearly. In the bake this is the one line
`if DIRECTX: n.y = -n.y` before packing (bake step (G) in [`02`](02-baking-algorithm.md)).

### 4.2 Normal maps are LINEAR data — never sRGB
This is the **single most common baking bug.** A normal map's RGB triples *are a direction vector*
consumed directly in lighting math — not a colour to be perceived. If the texture is tagged/sampled
as sRGB, the GPU applies the sRGB→linear transfer curve and **non-linearly distorts every component**,
tilting every normal.[^wiki][^gpugems-linear] Therefore NormalBake must:

1. Write pixels in **linear** encoding (no sRGB OETF applied).
2. For PNG/TGA, emit **no sRGB/gamma chunk** (or explicitly mark the output non-color).
3. Document that consumers must import the map as **Linear / Non-Color**.

## 5. Bit depth & block compression

### 5.1 8-bit vs 16-bit (banding)
8-bit = 256 levels/channel, which **bands** visibly on smooth, low-curvature gradients (large flat
panels, soft bevels) where adjacent texels differ by less than 1/256 and quantize to the same value,
giving stair-step shading.[^poly-band] 16-bit (65,536 levels) is smooth. **Recommendation:** compute
and store at **16-bit internally**; emit 16-bit by default; offer dithered 8-bit for delivery.

### 5.2 BCn block compression (GPU formats)
For engine delivery, normal maps ship as GPU block-compressed textures (4×4 texel blocks, so
dimensions must be multiples of 4):[^ms-bcn][^reed-bcn]

| Format | bpp | Channels | Normal-map fit |
|---|---|---|---|
| **BC5** (DXN / 3Dc) | 8 | 2 (R,G), each an independent 8-bit BC4 block | **Ideal for tangent-space normals.** Store X,Y; reconstruct Z. Two uncorrelated channels, no wasted blue. The industry default. |
| **BC7** | 8 | RGB(A), 8 modes, high endpoint precision | Best for **object-space** (true 3-channel) normals or colour. |
| BC3 (DXT5) | 8 | RGB+A | Legacy "DXT5n" swizzle hack (X in alpha, Y in green). Superseded by BC5. |
| BC1 (DXT1) | 4 | RGB | **Never for normals** — 5:6:5 endpoints + 4-colour palette per block ⇒ heavy banding; X/Y/Z don't lie on one RGB line. |

**Rules:** tangent-space → **BC5**; object-space → **BC7**; never BC1; clamp `z ≥ 0` and renormalize
*before* compressing; always renormalize after sampling a compressed map.[^nvidia-nc]

## 6. Output file formats

| Format | Depth | Use for normals |
|---|---|---|
| **16-bit PNG** | 16-bit int | **Default lossless output** — no banding, universally readable, pure-managed encoders exist. Mark/treat as **linear**. |
| OpenEXR | 16/32-bit float | **Archival / high precision** (object-space, displacement, float pipelines). Heavier dependency. |
| DDS (BCn) | BC5 / BC7 | **Engine delivery** — GPU-ready, stores mips, small VRAM. BC5 for tangent-space. |
| TGA / 8-bit PNG | 8-bit | Legacy; only as an explicit dithered down-output (banding risk). Avoid as default. |

**NormalBake defaults:** emit **16-bit PNG** (linear, no sRGB tag); `--format exr` for float;
`--format dds --compress bc5|bc7` for engine targets; always provide the **Y-flip** option.

## 7. Mip-mapping (pointer)
Naively box-averaging and renormalizing normals across a mip level discards sub-texel normal
*variance* and causes **specular aliasing/shimmer**. The fix is to fold that lost variance into
roughness (Toksvig / LEAN / vMF). Because that is a *quality* concern, it lives in
[`07-quality-artifacts-and-aa.md`](07-quality-artifacts-and-aa.md).

## 8. Decode reference (shader side, for context)

A typical tangent-space decode the renderer performs (and that NormalBake must bake the inverse of):

```glsl
vec3 n_ts = texture(normalMap, uv).xyz * 2.0 - 1.0;   // linear sample, unpack
// (2-channel variant: n_ts.z = sqrt(saturate(1 - dot(n_ts.xy, n_ts.xy))) )
vec3 N = normalize(vNormal);                          // unnormalized-interp in, normalize here
vec3 T = normalize(vTangent.xyz);
vec3 B = vTangent.w * cross(N, T);                    // handedness sign
vec3 n_world = normalize(mat3(T, B, N) * n_ts);       // tangent -> world
```

The TBN reconstruction here **must** match the basis NormalBake used — see
[`03`](03-tangent-space-and-mikktspace.md).

---

[^cohen]: Cohen, Olano, Manocha, *Appearance-Preserving Simplification*, SIGGRAPH 1998. https://history.siggraph.org/learning/appearance-preserving-simplification-by-cohen-olano-and-manocha/
[^learnopengl]: LearnOpenGL — Normal Mapping. https://learnopengl.com/Advanced-Lighting/Normal-Mapping
[^poly-renorm]: Polycount — renormalization discussion. https://polycount.com/discussion/78637/
[^unity-recz]: Unity Shader Graph — Normal Reconstruct Z node. https://docs.unity3d.com/Packages/com.unity.shadergraph@6.9/manual/Normal-Reconstruct-Z-Node.html
[^nvidia-nc]: NVIDIA Texture Tools — Normal Map Compression (clamp z, renormalize, reconstruct). https://github.com/castano/nvidia-texture-tools/wiki/NormalMapCompression
[^adobe-gldx]: Adobe — Difference between OpenGL and DirectX normal format (Y+ vs Y−). https://experienceleague.adobe.com/en/docs/substance-3d/bakers/common-questions/what-is-the-difference-between-the-opengl-and-directx-normal-format
[^wiki]: Wikipedia — Normal mapping (linear / non-color requirement). https://en.wikipedia.org/wiki/Normal_mapping
[^gpugems-linear]: NVIDIA GPU Gems 3 — *The Importance of Being Linear*. https://developer.nvidia.com/gpugems/gpugems3/part-iv-image-effects/chapter-24-importance-being-linear
[^poly-band]: Polycount — *Of Bit Depths, Banding and Normal Maps*. https://polycount.com/discussion/148303/
[^ms-bcn]: Microsoft — Block Compression (BCn). https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-block-compression
[^reed-bcn]: Nathan Reed — *Understanding BCn Texture Compression Formats*. https://www.reedbeta.com/blog/understanding-bcn-texture-compression-formats/
