# Test corpus (mesh pairs)

Each baking test runs against **mesh pairs** — a detailed high-poly plus its UV-unwrapped low-poly.

## Tier A — procedural (committed here)

Generated deterministically in code (`tests/NormalBake.Tests/Corpus/Primitives.cs`) and committed as
OBJ so the oracle (Blender) and golden tests have stable inputs. Regenerate with:

```sh
NORMALBAKE_WRITE_CORPUS=1 dotnet test --filter FullyQualifiedName~WriteCorpusFiles
```

| Pair | High-poly | Low-poly (UVs) | Exercises |
|------|-----------|----------------|-----------|
| `sphere`   | fine UV sphere            | coarse lat-long UVs (1 island)        | smooth curvature |
| `box`      | rounded/beveled box       | flat cube, 6 per-face islands         | hard edges + UV seams + gutters |
| `cylinder` | fine capped cylinder      | side rectangle + 2 cap disks (3 isl.) | rim hard edges + multi-island packing |

Files: `tests/corpus/<name>/<name>_high.obj` and `<name>_low.obj`.

## Tier B — downloaded (git-ignored)

Real assets fetched by [`tools/reference-oracle/download-corpus.ps1`](../../tools/reference-oracle/download-corpus.ps1)
into `tests/corpus/_downloads/` (git-ignored): glTF `NormalTangentTest` / `NormalTangentMirrorTest`
(tangent-space + mirrored-UV correctness), `DamagedHelmet`, and Stanford high-poly sources. Dense scan
meshes have no low-poly+UV counterpart, so they need a one-time scripted Blender prep (decimate → smart
UV unwrap → export) before baking. Don't block P4 on tier B.
