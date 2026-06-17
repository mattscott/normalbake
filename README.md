# NormalBake

A standalone, permissively-licensed, dependency-light, cross-platform **managed-C# (.NET 8)** library +
CLI that bakes a **normal map** from a high-resolution mesh into the UV space of a low-resolution
(quad-remeshed) mesh.

It is a **clean-room** implementation: the projection bake (Cignoni et al. 1998), a
MikkTSpace-faithful tangent basis, and a managed BVH ray tracer — **no native dependencies**,
MIT-clean.

> **Status: P0 — scaffold.** The solution builds and tests run; the baker is not yet implemented.
> See [`Agents/Plans`](Agents/Plans) for the roadmap and [`Agents/Documents`](Agents/Documents) for
> the research it is based on.

## Pipeline

NormalBake is the **baking** stage of a three-tool chain (the upstream remesh and UV unwrap are
separate tools):

```
quad-remesher (retopology) ─► UV-unwrap tool ─► NormalBake (this tool) ─► normal map
```

The low-poly **must already carry UVs**; NormalBake validates them and bakes the high-poly's surface
detail into the UV-space normal map. The two levers that decide whether a bake looks right are a
**MikkTSpace-synced tangent basis** and a good **cage/projection** envelope.

## Layout

```
src/
  NormalBake.Core/   the library (BVH, tangents, projection, encoding) — dependency-light
  NormalBake.IO/     mesh + image readers/writers (OBJ + PNG first)
  NormalBake.Cli/    `normalbake high.obj low.obj -o normal.png ...`
tests/
  NormalBake.Tests/  unit + golden-image tests
tools/
  reference-oracle/  Blender-headless scripts that generate golden images (xNormal cross-check)
```

## Build & test

```sh
dotnet build
dotnet test
```

Requires the .NET 8 runtime (the SDK may be newer; `global.json` rolls forward).

## License

MIT — see [LICENSE](LICENSE). Algorithmic attributions are in [NOTICE](NOTICE).
