# GlobalGridManager

`GlobalGridManager` was removed in the v6 world-scoping break.

This page remains only as a migration note for older links and references.

## What Replaced It

Use `GridWorld` directly.

That means:

- `TryAddGrid(...)`
- `TryRemoveGrid(...)`
- `TryGetGrid(...)`
- `TryGetGridAndVoxel(...)`
- `TryGetVoxel(...)`
- `FindOverlappingGridsInto(...)`
- `SpatialGridCellSize`
- construct and own a `GridWorld`
- register grids with explicit `GridConfiguration.TopologyMetrics`
- route tracing, blockers, occupants, and queries through that world
- treat `WorldVoxelIndex` as the exact transient cross-system runtime identity

`SpatialGridCellSize` is optional tuning for ordinary, similarly sized grids.
GridForge automatically routes large spatial footprints away from that path, so
do not size it to the largest grid a streamed world may ever load.

## Read This Next

- [Architecture Overview](Architecture-Overview.md) for the real system map
- [Getting Started](Getting-Started.md) for the recommended explicit-world entry
  path
- [Core Concepts](Core-Concepts.md) for world-scoped identity and ownership
