# Grid Diagnostics and Geometry

Grid diagnostics are an engine-agnostic projection layer for tools, overlays,
tests, and adapters. The core library does not draw gizmos, own meshes, choose
colors, or depend on Unity. It emits deterministic descriptors that another
layer can turn into renderer data.

Use this surface when a tool needs to inspect grid cells or geometry. Use
[Diagnostics and Logging](Diagnostics-and-Logging.md) when code only needs to
emit messages through `GridForgeLogger`.

## Query Surfaces

`GridDiagnostics` reads one active `GridWorld` and exposes cells through two
caller-owned paths:

| API | Use When |
| --- | --- |
| `GetCellsInto(...)` | You want a `SwiftList<GridDiagnosticCell>` filled for inspection or test assertions. The list is cleared before each query. |
| `VisitCells(...)` | You want the hot path for adapters that write directly into their own buffers. Returning `false` from the visitor stops traversal. |

`GridDiagnosticScratch` owns reusable temporary state for queries. Keep one
scratch object with the adapter or tool loop when running repeated queries.

`GridDiagnostics.TryResolvePhysicalCell(...)` resolves a physical diagnostic
descriptor back to its active `VoxelGrid` and `Voxel`. It rejects stale world or
grid tokens and always returns `false` for missing sparse address descriptors.

## Address Modes

Physical cells are the default output. Missing sparse address cells are opt-in
so sparse grids keep their configured-only runtime behavior.

| Address Mode | Output |
| --- | --- |
| `PhysicalOnly` | Dense cells or configured sparse cells only. |
| `PhysicalAndMissing` | Configured sparse cells plus missing sparse address descriptors inside the query range. |
| `MissingOnly` | Missing sparse address descriptors only. Dense grids emit no missing cells. |

Missing sparse address cells use `GridDiagnosticCellKind.MissingSparseAddress`
and the `MissingSparseAddress` state flag. They are not `Voxel` instances, do
not allocate or configure storage, and do not change lookup, coverage, blocker,
occupant, partition, or neighbor semantics.

Sparse-hole queries require bounds or `AllowFullAddressSpaceScan = true`.
`GridDiagnosticQuery.MaxCells` defaults to `65536`, and traversal stops with
`MaxCellsExceeded` when the budget is reached.

## Cell State

Physical descriptors can include:

- `Empty`
- `Occupied`
- `Blocked`
- `Boundary`
- `Partitioned`

Missing sparse address descriptors include `MissingSparseAddress` and may also
include `Boundary` when the address lies on the grid boundary. State filters are
applied after the cell state is derived.

## Geometry

`GridDiagnosticGeometry` writes fixed-point world-space geometry from a
`GridDiagnosticCell`.

| Topology | Vertices | Edges |
| --- | --- | --- |
| Rectangular-prism | 8 prism corners | 12 wireframe edges |
| Hex-prism | 12 prism corners | 18 wireframe edges |

Rectangular geometry uses the cell width, layer height, and cell length from
the descriptor's `GridTopologyMetrics`. Hex geometry uses radius, layer height,
and the grid's `FlatTop` or `PointyTop` orientation.

The core API uses `Fixed64` and `Vector3d`. Adapters should convert to engine
numeric types only at the adapter boundary, then keep those converted values out
of core GridForge queries.

## Caller-Owned List Example

```csharp
using FixedMathSharp;
using GridForge.Diagnostics;
using SwiftCollections;

SwiftList<GridDiagnosticCell> cells = new SwiftList<GridDiagnosticCell>();
GridDiagnosticScratch scratch = new GridDiagnosticScratch();

GridDiagnosticQuery query = new GridDiagnosticQuery(
    addressMode: GridDiagnosticAddressMode.PhysicalAndMissing,
    boundsMin: new Vector3d(0, 0, 0),
    boundsMax: new Vector3d(16, 0, 16),
    maxCells: 4096);

GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
    world,
    query,
    cells,
    scratch);
```

## Vertex Example

```csharp
using System;
using FixedMathSharp;
using GridForge.Diagnostics;
using GridForge.Grids.Topology;

int vertexCapacity = GridDiagnosticGeometry.GetVertexCount(cell.TopologyKind);
Span<Vector3d> vertices = stackalloc Vector3d[vertexCapacity];
int vertexCount = GridDiagnosticGeometry.WriteVertices(in cell, vertices);

ReadOnlySpan<GridDiagnosticEdge> edges = GridDiagnosticGeometry.GetEdges(
    cell.TopologyKind);
```

For fixed-topology adapter code, `RectangularPrismVertexCount` and
`HexPrismVertexCount` are also available as constants.

## Dirty Sessions

`GridDiagnosticSession` captures coalesced dirty changes for one active
`GridWorld`. It subscribes to world grid events plus obstacle and occupant
events, so adapters can rebuild only the grids or cells that changed.

Captured changes include:

- grid added, removed, changed, and world reset
- obstacle changes
- occupant changes
- sparse voxel add/remove changes
- sparse address range changes for hole-rendering adapters

`GetDirtyChangesInto(...)` fills caller-owned storage in deterministic order.
`ClearDirtyChanges()` resets the captured state. Dispose the session when the
adapter or tool stops observing the world.

World reset supersedes pending changes until the adapter drains or clears the
reset marker.

## Adapter Handoff

Adapters should treat diagnostic descriptors as read-only snapshots of active
GridForge state:

- keep renderer buffers, colors, materials, cameras, and meshes outside core
- prefer `VisitCells(...)` for repeated runtime or editor overlay updates
- keep a reusable `GridDiagnosticScratch` per adapter loop
- call `TryResolvePhysicalCell(...)` only when a descriptor needs live voxel
  state and expect it to fail for missing sparse address cells or stale tokens
- convert `Vector3d` and `Fixed64` to engine types at the adapter boundary

Future v6-to-v7 `MIGRATION.MD` note: old dense debugger loops over
`Width * Height * Length` should move to `GridDiagnostics.VisitCells(...)` so
sparse and hex grids use the same adapter path.

## Read This Next

- [Sparse Grid Storage](Sparse-Grid-Storage.md) for sparse runtime semantics
- [VoxelGrid and Voxel Model](VoxelGrid-and-Voxel-Model.md) for physical cell ownership
- [Testing and Benchmarking](Testing-and-Benchmarking.md) for the `grid-diagnostics` benchmark alias
- [Diagnostics and Logging](Diagnostics-and-Logging.md) for `GridForgeLogger`
