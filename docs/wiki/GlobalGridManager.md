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
- `VoxelSize`
- `SpatialGridCellSize`
- construct and own a `GridWorld`
- register grids against that world
- route tracing, blockers, occupants, and queries through that world
- treat `WorldVoxelIndex` as the stable cross-system identity

## Read This Next

- [Architecture Overview](Architecture-Overview) for the real system map
- [Getting Started](Getting-Started) for the recommended explicit-world entry path
- [Core Concepts](Core-Concepts) for world-scoped identity and ownership
