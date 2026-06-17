# The Baking Algorithm — Projection / Ray-Cast Pipeline

This is the heart of NormalBake. The technique is variously called **projection baking**,
**transfer maps**, or, in Blender's terms, **"Selected to Active."** It works in the **UV space of
the low-poly**, not in screen space: for every texel of the low-poly's texture atlas we recover the
3D surface point it represents, shoot a ray to the high-poly, and record what we find there.

Blender's manual states the core mechanic plainly: *"Selected to Active … project details from your
selected high-poly model onto your active low-poly one,"* casting rays inward/outward along the
low-poly's normals.[^blender-bake]

## 1. Inputs and outputs

**Inputs**
- `lowMesh` — the low-poly (Quadremesh output), **triangulated**, with positions, vertex normals,
  **UVs**, and **per-vertex tangents** with a handedness sign `w` (see
  [`03-tangent-space-and-mikktspace.md`](03-tangent-space-and-mikktspace.md)).
- `highMesh` — the high-poly, with positions and **smooth (interpolated vertex) normals**, indexed
  into a **BVH** for fast ray queries (see [`06-acceleration-and-performance.md`](06-acceleration-and-performance.md)).
- `cage` — either an explicit cage mesh **or** `(frontalDist, rearDist)` envelope distances (see
  [`04-cage-and-projection.md`](04-cage-and-projection.md)).
- `atlasW, atlasH` — output texture resolution.
- `convention` — `OPENGL` (Y+) or `DIRECTX` (Y−).

**Output**
- A linear RGB normal map, antialiased and dilated past island edges.

## 2. The pipeline, step by step

```text
for each lowTri in lowMesh.triangles:
    # --- (A) rasterize the triangle in UV space ---
    map (uv0,uv1,uv2) -> texel coords  (uv * [atlasW, atlasH])
    for each texel center (px,py) inside the UV triangle:
        (a,b,c) = barycentric_weights(texelCenterUV, uv0, uv1, uv2)

        # --- (B) recover low-poly surface attributes by barycentric interpolation ---
        P = a*P0 + b*P1 + c*P2                 # 3D surface point
        N = normalize(a*N0 + b*N1 + c*N2)      # smooth low-poly normal
        T = a*T0 + b*T1 + c*T2                 # interpolated tangent (do NOT normalize yet — see doc 03)
        w = T0.w                               # handedness sign, constant per UV island

        # --- (C) build the ray from the cage / envelope (see doc 04) ---
        if explicit cage:
            Pcage  = barycentric_interp(cage triangle positions)
            dir    = normalize(P - Pcage)            # inward, toward the surface
            origin = Pcage
            maxDist= length(P - Pcage) + rearDist
        else:                                        # distance envelope
            dir    = N
            origin = P + N*frontalDist               # start OUTSIDE the surface
            maxDist= frontalDist + rearDist

        # --- (D) cast against the high-poly BVH (see doc 04 for hit selection) ---
        hit = raycast(highMesh.BVH, origin, -dir, maxDist, ignoreBackface)
        if not hit: mark_miss(px,py); continue       # fill later via dilation (doc 05)

        # --- (E) sample the high-poly SMOOTH normal at the hit ---
        Nhi = normalize(hit.a*Nh0 + hit.b*Nh1 + hit.c*Nh2)   # world/object space

        # --- (F) transform Nhi into the low-poly tangent frame at this texel ---
        B = w * cross(N, T)                          # bitangent from sign (MikkTSpace convention)
        nT = ( dot(Nhi, normalize(T)),
               dot(Nhi, normalize(B)),
               dot(Nhi, N) )
        nT = normalize(nT)

        # --- (G) encode to RGB (see doc 01) ---
        if convention == DIRECTX: nT.y = -nT.y        # Y-down green channel
        rgb = nT*0.5 + 0.5                            # [-1,1] -> [0,1], LINEAR (never sRGB)
        accumulate_sample(px,py, rgb)                # average over supersamples, then store

post-process:
    resolve supersamples -> renormalize
    dilate / edge-pad island colors outward          # avoid mip/seam bleed (doc 05)
```

## 3. Why each step is the way it is

These details are load-bearing; getting any of them wrong produces a recognizable artifact.

- **Interpolate the *smooth* low-poly normal/TBN, not the face normal.** The bake must be performed
  against the *exact* tangent frame the renderer will reconstruct per-pixel. Using the interpolated
  `N`/`T`/`B` is precisely what lets the baked map "undo" the low-poly's faceting and reproduce the
  high-poly's look. Mikkelsen's runtime convention is that the pixel shader decodes the tangent-space
  normal using the **unnormalized, interpolated** vertex normal and tangent — so the baker must match
  that.[^blender-mikk] (Full treatment in [`03`](03-tangent-space-and-mikktspace.md).)
- **Sample the high-poly's *smooth* normal** (a barycentric blend of its vertex normals at the hit),
  not its flat face normal. Transferring the high-poly's continuous curvature into the low-poly's flat
  triangles is the entire point of the bake.
- **The ray starts *outside* the surface and travels inward.** Starting at `P + N·frontalDist` (or on
  the cage) and casting toward the surface lets us consistently take the **front-most** high-poly hit,
  rather than punching through to a back wall. (Hit selection: [`04`](04-cage-and-projection.md).)
- **Misses are left empty, not defaulted.** A ray that finds nothing within `maxDist` marks the texel
  as a miss; it is filled afterward by **dilation** from its written neighbors. Writing a default
  (0,0,1) into misses would create visible flat patches and seams.
- **Output is linear.** The encoded normal is *vector* data — write it with no sRGB transfer and tag
  the texture Linear/Non-Color. (Encoding details: [`01`](01-normal-mapping-fundamentals.md).)

## 4. UV-space rasterization & barycentric recovery

Each low-poly UV triangle `(uv0,uv1,uv2)` is scaled by the atlas resolution and rasterized with
standard triangle scan/edge-function rasterization; every texel center inside it is shaded. For each
covered texel, compute its **barycentric weights** with respect to the UV triangle, then reuse those
weights to interpolate the 3D attributes:

```text
# barycentric of p in UV triangle (uvA,uvB,uvC)
v0 = uvB-uvA;  v1 = uvC-uvA;  v2 = p-uvA
d00=dot(v0,v0); d01=dot(v0,v1); d11=dot(v1,v1)
d20=dot(v2,v0); d21=dot(v2,v1)
den = d00*d11 - d01*d01
beta  = (d11*d20 - d01*d21)/den
gamma = (d00*d21 - d01*d20)/den
alpha = 1 - beta - gamma          # weights for (A,B,C)
```

Because a UV unwrap is an **affine map per triangle**, the barycentric weights computed in 2D UV
space equal those of the corresponding 3D point — that identity is exactly what lets a 2D texel index
recover a unique 3D surface point and tangent frame.[^learnopengl] Interpolate
`P = α·P0+β·P1+γ·P2`, and likewise `N`, `T`, and `UV`.

### Quad faces → triangles
GPUs and ray casters work on triangles, so quad low-poly faces (Quadremesh's native output) are split
into two triangles before rasterization **and** before the tangent solve. The split rule matters:
**MikkTSpace splits quads along the shortest diagonal, consistently**, because that choice is
order-independent and mirror-safe.[^mikk-header] If NormalBake triangulates differently from the
engine/MikkTSpace, the tangent bases disagree and seams appear.
**Rule: triangulate exactly once, shortest-diagonal & deterministic, and feed that same triangulation
to the tangent solver and the rasterizer.**

## 5. Antialiasing and post-processing (pointers)

- **Supersampling.** Sample each texel on an N×N sub-grid, cast a ray per sub-sample, average the
  resulting *tangent-space normals*, then renormalize. Without this, baked normals alias badly along
  high-curvature silhouettes. Details and sample counts in
  [`07-quality-artifacts-and-aa.md`](07-quality-artifacts-and-aa.md).
- **Edge padding / dilation.** After the pass, bleed island colors a few texels outward so runtime
  bilinear filtering and mipmapping don't sample the empty background across UV seams. Details in
  [`05-uvs-and-dilation.md`](05-uvs-and-dilation.md).

## 6. Implementation notes for NormalBake

- The per-texel work is embarrassingly parallel — partition texels (or triangles) across threads;
  the BVH is read-only during the pass.
- Keep an explicit **coverage mask** so the dilation pass knows real texels from background.
- Determinism (a Quadremesh requirement) means fixed sample patterns and a stable triangle/texel
  iteration order — avoid order-dependent floating-point accumulation where it affects output bytes.

---

[^blender-bake]: Blender Manual — Render Baking (Selected to Active, Ray Distance, Extrusion, Cage). https://docs.blender.org/manual/en/2.80/render/cycles/baking.html
[^blender-mikk]: Blender — Tangent Space Normal Maps (Mikkelsen's runtime convention: pixel shader decodes using the unnormalized interpolated normal/tangent; `B = sign·cross(N,T)`). https://archive.blender.org/wiki/2015/index.php/Dev:Shading/Tangent_Space_Normal_Maps/
[^learnopengl]: LearnOpenGL — Normal Mapping (TBN from edges, barycentric attribute interpolation). https://learnopengl.com/Advanced-Lighting/Normal-Mapping
[^mikk-header]: MikkTSpace `mikktspace.h` (quads split along the shortest diagonal; order-independence). https://github.com/mmikk/MikkTSpace/blob/master/mikktspace.h
