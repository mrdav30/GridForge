# GridForge Wiki

GridForge is a deterministic voxel-world library for games, simulations,
tooling, and server runtimes. It gives you the grid infrastructure that often
gets rebuilt project by project: fixed-point snapping, world-scoped lookup,
conjoined grids, dense and sparse storage, spatial queries, blockers, occupants,
and diagnostic geometry.

The central idea is simple: a `GridWorld` owns one isolated spatial world. That
world may contain a single grid, many neighboring grids, or a changing set of
streamed regions. Higher-level concepts such as sectors, planets, and shards can
sit above GridForge without changing the voxel layer.

## Start here

If you are new to GridForge, follow this path:

1. [Getting Started](Getting-Started.md) — install the package and resolve your
   first voxel.
2. [Core Concepts](Core-Concepts.md) — learn the world, grid, voxel, identity,
   and ownership model.
3. [Common Workflows](Common-Workflows.md) — copy practical patterns for sparse
   grids, hex grids, occupants, blockers, tracing, and teardown.
4. [FAQ and Troubleshooting](FAQ-and-Troubleshooting.md) — diagnose the mistakes
   that are easiest to make early.

Already know what you need? Jump straight to the topic map below.

## Choose a topic

### Build a world

| Guide | Use it for |
| --- | --- |
| [VoxelGrid and Voxel Model](VoxelGrid-and-Voxel-Model.md) | Grid construction, physical voxels, lookup, neighbors, and reuse |
| [Sparse Grid Storage](Sparse-Grid-Storage.md) | Large address spaces where only selected voxels exist |
| [Architecture Overview](Architecture-Overview.md) | Ownership boundaries and subsystem flow |
| [Determinism, Snapping, and Pooling](Determinism-Snapping-and-Pooling.md) | Numerical and lifetime rules that keep behavior reproducible |

### Query and mutate it

| Guide | Use it for |
| --- | --- |
| [GridTracer and Coverage](GridTracer-and-Coverage.md) | Line, box, and XZ-area coverage across active grids |
| [Scan Cells and Query Flow](Scan-Cells-and-Query-Flow.md) | Nearby-occupant queries and scan-cell performance |
| [Blockers and Obstacles](Blockers-and-Obstacles.md) | Stackable blocked regions and direct obstacle state |
| [Occupants and Partitions](Occupants-and-Partitions.md) | Dynamic entities and typed voxel-local metadata |

### Build tools and maintain the library

| Guide | Use it for |
| --- | --- |
| [Grid Diagnostics and Geometry](Grid-Diagnostics-and-Geometry.md) | Renderer-neutral cells, topology geometry, and dirty changes |
| [Diagnostics and Logging](Diagnostics-and-Logging.md) | Runtime messages, logging adapters, and debugging patterns |
| [Recipes](Recipes.md) | End-to-end gameplay, simulation, and server examples |
| [Repository Layout and Build](Repository-Layout-and-Build.md) | Projects, packages, CI, DocFX, and release tooling |
| [Testing and Benchmarking](Testing-and-Benchmarking.md) | Test layout, coverage, and benchmark commands |

## The mental model

Most GridForge workflows follow the same shape:

1. Create an explicit `GridWorld`.
2. Describe a grid with `GridConfiguration`.
3. Register it through `GridWorld.TryAddGrid(...)`.
4. Resolve world positions into a `VoxelGrid` and `Voxel`.
5. Trace, scan, block, occupy, or attach partitions as the simulation runs.
6. Reset or dispose the world when its lifetime ends.

Each grid chooses its own topology and storage:

- **Rectangular-prism** grids use local `(x, y, z)` voxel indices.
- **Hex-prism** grids use axial `(q, layer, r)` values stored in the same
  `VoxelIndex` shape.
- **Dense** grids materialize every voxel in the normalized address space.
- **Sparse** grids materialize only explicitly configured voxels; a missing
  address is intentional absence, not an empty voxel.

Flat simulations can use `Vector2d` overloads. `Vector2d.X` maps to world X,
`Vector2d.Y` maps to world Z, and `layerY` selects world Y. These overloads are a
convenience over the same 3D runtime, not a separate grid implementation.

## Important lifetime rules

- `GridIndex` is a reusable storage slot, not durable identity.
- Use `WorldVoxelIndex` for exact references within the current runtime.
- `ObstacleToken` and `OccupantTicket` identify one transient registration
  lifetime; do not serialize them as content IDs.
- Consume pooled tracer and grouped query results within the operation that
  produced them.
- Keep deterministic runtime math in `Fixed64`, `Vector2d`, and `Vector3d`.
- Keep engine-specific authoring and rendering in adapters such as
  [GridForge-Unity](https://github.com/mrdav30/GridForge-Unity).

## Packages and compatibility

GridForge targets `netstandard2.1` and `net8.0` and is published in two variants:

| Package | Profile |
| --- | --- |
| `GridForge` | Standard package with MemoryPack support |
| `GridForge.Lean` | Same grid APIs without the MemoryPack runtime dependency |

Both variants use FixedMathSharp and SwiftCollections. Source builds use the
matching `Release` and `ReleaseLean` configurations.

## Project links

- [API reference](https://mrdav30.github.io/GridForge/api/GridForge.html)
- [Migration guide](https://github.com/mrdav30/GridForge/blob/main/docs/MIGRATION.md)
- [Coverage report](https://mrdav30.github.io/GridForge/coverage/)
- [Source repository](https://github.com/mrdav30/GridForge)
- [Unity packages](https://github.com/mrdav30/GridForge-Unity)

The Markdown files in `docs/wiki` are the source of truth for this wiki. Keep
links between wiki pages relative and include their `.md` extension; the sync
workflow rewrites only the routes needed by GitHub Wiki.
