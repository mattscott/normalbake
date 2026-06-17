# Prior Art — Tools, Reference Oracle & Reusable Libraries

This surveys the existing normal-baking tools (to learn from and to pick a **golden-output oracle**),
then catalogues **reusable, license-checked libraries** for a managed, MIT-clean implementation that
validates against a reference oracle and reuses only permissive components.

## 1. Existing bakers

All modern bakers share two concepts: a **cage / ray-distance** envelope ([`04`](04-cage-and-projection.md))
and a **MikkTSpace-synced tangent basis** ([`03`](03-tangent-space-and-mikktspace.md)).

| Tool | Known for | Cage handling | Tangent sync | Cost / License | Headless / scriptable |
|---|---|---|---|---|---|
| **xNormal** | The historical **free reference** baker | cage mesh + front/rear max ray distance; bundled ray-distance calc | MikkTSpace-compatible; *is itself* a named reference basis ("xNormal/Mikk") | **Free**, **Windows-only**, lightly maintained | Yes — **XML config → `xnormal.exe`** batch; SDK/plugins |
| **Marmoset Toolbag** | Best-in-class **cage editing + skew painting**, real-time lit preview, GPU bake | custom cages, cage offset, **Paint Skew** per-pixel fix | per-pixel Mikk; R/G/B flip toggles | **Paid** | Python API (`mset`), GUI-centric |
| **Adobe Substance 3D** (`sbsbaker`) | `_low`/`_high` name matching, averaged normals | distance-based or cage; Max Frontal/Rear | TS/world; Mikk | **Paid** | **`sbsbaker` CLI** + `pysbs` |
| **Blender (Cycles)** | Free/open "selected to active" | `cage_object`, `cage_extrusion`, `max_ray_distance` | **Canonical MikkTSpace** | **Free, GPL** | **Excellent** — `blender --background --python`, full `bpy` |
| **Knald** | Standalone **GPU** baker (normal/AO/curvature/bent at once) | ray-based | Mikk, synced 3D preview | **Paid** | GUI-centric |
| **Handplane** | **Free** object→tangent **converter** (not a from-mesh baker) | n/a | knows many bases (Mikk, Max, Maya, Lengyel…) | **Free** | limited |
| **Houdini** (Labs/Games Baker) | procedural high→low | cage, ray bias/distance | MikkT option | needs **paid Houdini**; **bakers deprecated** | `hython`, but fragile |
| **ZBrush** | Multi Map Exporter from subdiv levels | no game-style low/high cage | TS (Local) / World | **Paid** | ZScript only — poor oracle |
| **MeshLab** | general mesh processing; **not** a game baker | none | none | **Free, open** | unstable attribute→texture path |

Sources: xNormal[^xnormal]; Marmoset[^marmoset-cage][^marmoset-tan]; Substance[^substance-nm][^sbsbaker]; Blender[^blender-bake][^blender-api]; Knald[^knald]; Handplane[^handplane]; Houdini[^houdini]; ZBrush[^zbrush].

## 2. Reference oracle: **Blender (Cycles) headless**, with **xNormal** as cross-check

The oracle must be **free + scriptable/headless + deterministic + real cage/ray-distance +
MikkTSpace-synced**. That eliminates the paid tools, Houdini (deprecated bakers + paid host),
Handplane (converter, not baker), and MeshLab (not a game baker). The two real candidates:

- **Blender — primary.** `blender --background --python script.py` + `bpy` gives a fully programmatic,
  **cross-platform** pipeline (generate mesh pairs; set `cage_object` / `cage_extrusion` /
  `max_ray_distance`; bake; write PNG), uses the **canonical MikkTSpace** basis, is **open/inspectable**
  (you can explain a golden), and is **deterministic** with pinned samples + compute device.
- **xNormal — secondary cross-validator.** The long-standing industry reference basis (tools ship an
  "xNormal/Mikk" preset) and free + XML-scriptable; its disqualifier as *primary* CI oracle is being
  **Windows-only** and lightly maintained. Use it to confirm Blender goldens match the historical
  standard.

## 3. Reusable libraries (license-verified)

**Legend.** *Managed* = pure C#, directly usable. *Native* = C/C++ → needs a P/Invoke wrapper (ships a
native DLL, **breaks zero-native**) or a clean-room port.

### 3.1 Pure-managed, MIT-clean — use directly
| Library | Purpose | License | Notes |
|---|---|---|---|
| **BCnEncoder.NET**[^bcn] | **BC1–BC7 encode/decode**, write DDS/KTX | MIT **or** Unlicense | **The pure-C# answer for BC5/BC7** — the only managed BCn *encoder* found. Replaces all native encoders. |
| **StbImageSharp** / **StbImageWriteSharp**[^stb] | PNG/TGA/JPEG/BMP/HDR read+write | Public-Domain **or** MIT | Pure-C# stb ports. **Best fit** for PNG/TGA I/O. |
| **Pfim**[^pfim] | DDS + TGA **decode** (incl. BC/DXT) | MIT | decode-only; pair with BCnEncoder.NET for writing DDS. |
| **Jither.OpenEXR**[^jither] | EXR read; **write (scanline)** | MIT | pure-managed EXR; Half/Float/UInt, RLE/ZIP/PIZ. |

### 3.2 Permissive but native — bind or port
| Library | Purpose | License | Implication |
|---|---|---|---|
| **MikkTSpace**[^mikk] | the tangent-basis keystone | zlib (MIT-compatible) | **Native C, single file.** No pure-managed port exists. **Clean-room port → low effort**; keep the notice. *Correctness keystone.* |
| **xatlas**[^xatlas] | UV unwrap / atlas | MIT | **Native C++**, no deps. *Out of NormalBake's scope* — UVs are an input; the **separate UV-unwrap tool** owns this (clean-room port or wrapper there). |
| MS **UVAtlas**[^uvatlas] | UV unwrap (alternative; separate tool) | MIT | native C++ (pulls DirectXMath/Mesh). |
| **Embree**[^embree] | reference CPU ray tracer | Apache-2.0 | native; binding makes it a native dep (contra philosophy). Reimplement the BVH in managed C# instead ([`06`](06-acceleration-and-performance.md)). |

### 3.3 Avoid for the MIT-clean core
- **ImageSharp** (SixLabors) — pure-managed and capable, **but licensed under the Six Labors Split
  License**, *not* plain Apache-2.0: for-profit ≥ $1M revenue needs a paid commercial license. Prefer
  the **stb ports** to stay unconditionally permissive.[^imagesharp]

### 3.4 Test-only (not shipped)
- **NVlabs FLIP**[^flip] — BSD-3-Clause perceptual image-difference evaluator; emit its error map as a
  CI artifact on golden-test failures (§4). Fine as a dev/test dependency.

**Net porting picture.** Pure-managed, MIT-clean coverage already exists for BC5/BC7 + DDS/KTX, PNG/TGA/JPEG,
EXR write, DDS decode. With UV unwrapping handed to the **separate UV-unwrap tool**, NormalBake's
**only** nontrivial clean-room port is **MikkTSpace** (*low effort*, single C file — the correctness
keystone); its zlib source is MIT-compatible, and the port is validated against the C reference's
output. (**xatlas/UVAtlas** ports belong to the separate UV-unwrap tool, not here.)

## 4. Golden-image testing

Generate references with **Blender headless**, bake the same inputs with NormalBake, diff within
tolerance. Determinism first (CPU, fixed accumulation order, pinned sample pattern, MikkTSpace basis,
fixed OpenGL/DirectX convention — see [`06`](06-acceleration-and-performance.md) §6). Tiered metrics:

1. **Per-pixel tolerance (primary gate)** — max-abs / count-of-failing-pixels per channel, with a small
   ε and a small allowed failing fraction (pure equality is brittle to rounding/compression jitter).
2. **PSNR** — cheap global tripwire; poor perceptual correlation — trend gate only.
3. **SSIM** — structural similarity; catches structural regressions PSNR misses — secondary gate.
4. **ꟻLIP** — perceptual per-pixel error *map*, emphasizing edges/seams (where normal errors show);
   emit as a CI artifact on failure.[^flip]

Curate small mesh pairs (sphere, cube, hard-edge bevel, cylinder, plus one dense real mesh), commit
Blender-baked PNG goldens, **hard-gate on per-pixel tolerance**, soft-gate on SSIM/PSNR trend, and
decode-compare BC5/BC7 outputs against the *uncompressed* golden so compression error isn't conflated
with bake error.

---

[^xnormal]: xNormal. https://xnormal.net/
[^marmoset-cage]: Marmoset Toolbag — Cage. https://docs.marmoset.co/docs/cage/
[^marmoset-tan]: Marmoset Toolbag — Tangents & Handedness. https://docs.marmoset.co/docs/tangent-handedness/ · Python API https://marmoset.co/python/reference.html
[^substance-nm]: Adobe — Normal Map from Mesh. https://experienceleague.adobe.com/en/docs/substance-3d/bakers/bakers-settings/normal-map-from-mesh
[^sbsbaker]: Adobe — `sbsbaker` CLI. https://substance3d.adobe.com/documentation/sat/command-line-tools/sbsbaker/sbsbaker-command-line-options
[^blender-bake]: Blender Manual — Render Baking. https://docs.blender.org/manual/en/latest/render/cycles/baking.html
[^blender-api]: Blender — `BakeSettings` API. https://docs.blender.org/api/current/bpy.types.BakeSettings.html
[^knald]: Knald — Normal Maps. https://docs.knaldtech.com/doku.php?id=normal_maps_knald_1.1
[^handplane]: Handplane. https://handplane3d.com/
[^houdini]: SideFX — Labs Games Baker. https://www.sidefx.com/docs/houdini/nodes/out/labs--games_baker-2.0.html
[^zbrush]: Maxon ZBrush — Map Generation. https://www.maxon.net/en/zbrush/features/map-generation
[^bcn]: BCnEncoder.NET. https://github.com/Nominom/BCnEncoder.NET
[^stb]: StbImageSharp / StbImageWriteSharp. https://github.com/StbSharp/StbImageSharp · https://github.com/StbSharp/StbImageWriteSharp
[^pfim]: Pfim. https://github.com/nickbabcock/Pfim
[^jither]: Jither.OpenEXR. https://github.com/Jither/Jither.OpenEXR
[^mikk]: MikkTSpace. https://github.com/mmikk/MikkTSpace
[^xatlas]: xatlas. https://github.com/jpcy/xatlas
[^uvatlas]: Microsoft UVAtlas. https://github.com/microsoft/UVAtlas
[^embree]: Embree (RenderKit). https://github.com/RenderKit/embree · https://www.embree.org · LICENSE (Apache-2.0) https://github.com/RenderKit/embree/blob/master/LICENSE.txt
[^imagesharp]: SixLabors ImageSharp — Split License. https://github.com/SixLabors/ImageSharp/blob/main/LICENSE · https://sixlabors.com/pricing/
[^flip]: NVlabs FLIP (BSD-3-Clause). https://github.com/NVlabs/flip · https://dl.acm.org/doi/10.1145/3406183
