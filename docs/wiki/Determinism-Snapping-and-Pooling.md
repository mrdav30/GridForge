# Determinism, Snapping, and Pooling

This page covers the three invariants that shape most of GridForge's implementation choices:

- deterministic math and ordering
- snapped spatial boundaries
- aggressive object and collection reuse

## Determinism Is A Design Goal

GridForge is built around fixed-point math and explicit ordering.

In practice that means:

- core spatial math uses `Fixed64`, `Vector2d`, and `Vector3d`
- grid creation and tracing logic work from snapped fixed-point bounds
- behavior must stay stable across both `netstandard2.1` and `net8.0`

`Vector2d` APIs use the same fixed-point math as the 3D APIs. They project XZ
coordinates onto a chosen `layerY` and then flow through the same world, grid,
voxel, tracer, scan, obstacle, and blocker systems.

## Snapping Starts At Registration Time

`GridConfiguration` orders incoming bounds on construction, and `GridWorld` snaps them during registration.

That snapped result affects:

- grid dimensions
- duplicate grid detection
- world-space containment tests
- tracer coverage
- blocker coverage
- local voxel index resolution

## Cell Metrics Are A Grid-Level Assumption

`GridConfiguration.TopologyMetrics` establishes deterministic cell geometry for
the grid being registered.

That means:

- grid configuration snapping depends on the normalized topology metrics
- voxel index math for that grid depends on those metrics
- changing cell geometry is a grid-configuration choice, not a hidden world-wide
  scalar

When tests or tools need different cell geometry, create the grid with explicit
topology metrics and keep expectations local to that grid.

## Pooling Is A First-Class Constraint

Pooling in GridForge is not an optimization sprinkled on top. It shapes the object lifecycle.

The internal pools cover types such as:

- `VoxelGrid`
- `Voxel`
- `ScanCell`
- scan-cell maps
- neighbor arrays
- temporary query lists and hash sets

Every new mutable field introduced into a pooled type needs a matching reset story.

## Practical Debugging Checklist

1. Was the grid created with the topology metrics you think it was?
2. What are the snapped bounds after normalization?
3. Is the queried world-space position exactly on a boundary?
4. Are you looking at a pooled object or temporary collection after its intended lifetime?
5. Did a previous test, tool run, or benchmark leave world state active longer than intended?
