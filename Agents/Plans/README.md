# NormalBake — Implementation Plans

The build plan for a managed-C# normal-map baker, based on the research in [../Documents/](../Documents/).
Direction in one line: **for each low-poly UV texel, cage-cast a ray to the high-poly and write its
smooth normal in the low-poly's MikkTSpace tangent frame** — a clean-room reimplementation of the
classic projection bake (Cignoni et al. 1998) with a managed BVH and a MikkTSpace-faithful basis.

| Doc | Contents |
|-----|----------|
| [00-master-plan.md](00-master-plan.md) | Vision, scope, non-goals, milestones, success criteria, risks |
| [01-architecture.md](01-architecture.md) | Project/module layout, core data structures, public API, determinism |
| [02-phase-roadmap.md](02-phase-roadmap.md) | Phase-by-phase tasks, deliverables, acceptance tests |

**Phase ladder at a glance**

```
P0 Scaffold ─► P1 Mesh+Image IO ─► P2 Tangent basis (MikkTSpace) ─► P3 BVH ray tracer ─►
P4 Projection bake (normal map) ─► P5 Robustness+quality ─► P6 General baker ─►
P7 Perf+determinism ─► P8 Packaging/CLI/API
```

The first *demoable* milestone (a baked tangent-space normal map out) is end of **P4**.
**P2 (MikkTSpace) and P3 (BVH)** are the de-risking **kill-points** — each is validated in isolation
(against the C reference / against brute force) before P4 integrates them.

**Pipeline context.** NormalBake is the **baking** stage of a three-tool chain — *quad-remesher
(retopology) → UV-unwrap tool → NormalBake*. The low-poly **arrives with UVs**; NormalBake validates
them and bakes. The upstream quad-remesher and UV-unwrap tool are separate tools; UV unwrapping is
explicitly **out of scope** (see [../Documents/05-uvs-and-dilation.md](../Documents/05-uvs-and-dilation.md) §1).
