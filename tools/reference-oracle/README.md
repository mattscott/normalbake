# Reference oracle (test-only)

NormalBake validates its output the same way Quadremesh validates against QuadriFlow: a trusted,
free, scriptable, deterministic reference baker generates **golden normal maps**, and CI diffs
NormalBake's output against them. None of these tools are shipped or linked — they are invoked as
external processes to emit committed golden images.

## Primary oracle — Blender (headless)

`blender --background --python <script>.py` drives a fully programmatic bake:

1. load/generate a corpus mesh pair (high-poly + UV-unwrapped low-poly),
2. set the bake to **Selected to Active**, normal map, tangent space (Blender uses the canonical
   **MikkTSpace** basis), with a cage object / `cage_extrusion` / `max_ray_distance`,
3. bake at a fixed sample count + device (deterministic),
4. write the result as a PNG golden under `tests/golden/`.

Blender is the primary oracle because it is **free, open, cross-platform, scriptable, deterministic,
and MikkTSpace-synced** — see [`../../Agents/Documents/08-prior-art-tools-and-libraries.md`](../../Agents/Documents/08-prior-art-tools-and-libraries.md) §2.

## Secondary cross-check — xNormal

xNormal is the historical industry reference basis ("xNormal/Mikk"). It is free and XML-scriptable but
**Windows-only**, so it is used only to cross-validate that the Blender goldens match the de-facto
standard — not as the primary CI oracle.

## Comparison metrics

Per the testing plan ([`08` §4](../../Agents/Documents/08-prior-art-tools-and-libraries.md)):
hard-gate on **per-pixel tolerance** (small epsilon + small allowed failing fraction), soft-gate on
**SSIM/PSNR** trend, and emit a **FLIP** perceptual error map as a CI artifact on failure. Decode-compare
BC5/BC7 outputs against the uncompressed golden so compression error is not conflated with bake error.

## Status

Scaffold only — scripts are added in **P0/P1** (see
[`../../Agents/Plans/02-phase-roadmap.md`](../../Agents/Plans/02-phase-roadmap.md)). The working tree
(`_work/`, logs) is git-ignored; the committed goldens under `tests/golden/` are not.
