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

## Snapping Starts At Registration Time

`GridConfiguration` orders incoming bounds on construction, and `GridWorld` snaps them during registration.

That snapped result affects:

- grid dimensions
- duplicate grid detection
- world-space containment tests
- tracer coverage
- blocker coverage
- local voxel index resolution

## Voxel Size Is A World-Level Assumption

`GridWorld` establishes voxel size for that world instance.

That means:

- all later grid configuration snapping in that world depends on that value
- all later voxel index math in that world depends on that value
- changing voxel size is a world-level choice, not a local tweak

When tests or tools need a different voxel size, create a separate world or reset and recreate the current one.

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

1. Was the current `GridWorld` created with the voxel size you think it was?
2. What are the snapped bounds after normalization?
3. Is the queried world-space position exactly on a boundary?
4. Are you looking at a pooled object or temporary collection after its intended lifetime?
5. Did a previous test, tool run, or benchmark leave world state active longer than intended?
