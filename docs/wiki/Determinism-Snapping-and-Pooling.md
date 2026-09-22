# Determinism, Snapping, and Pooling

This page covers the three invariants that shape most of GridForge's
implementation choices:

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

`GridConfiguration` orders incoming bounds on construction, and `GridWorld`
snaps them during registration.

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

### Hex Projection And Rounding

Hex centers use a quantized fixed-point basis: the full width is
`CellRadius * Sqrt3`, and the row step is `CellRadius * 3 * Half`, evaluated in
that order. World-to-axial projection divides by these same quantized values. An
ideal irrational inverse is not interchangeable with this inverse, especially
for small positive raw radii or distant addresses.

The forward projection retains FixedMathSharp's round-half-to-even behavior. An
odd raw width on an odd row can round a center by half a raw unit, so the
continuous inverse need not be an exact integer. Lookup uses deterministic cube
rounding; bounds normalization recognizes exactly projected axial corners before
applying its existing outward ceiling/tolerance rule. Authored corners therefore
preserve their address dimensions when normalized repeatedly.

These guarantees require representable, unsaturated intermediate arithmetic: the
basis (including `CellRadius * 3`), axial projection, translated centers,
relative offsets, inverse coordinates, and cube coordinate `-q - r` must fit
`Fixed64`. Saturation is not an invertible coordinate mapping. No minimum radius
beyond a positive representable value is imposed; individual geometry queries
can have stricter prism-representability checks. Normalized address counts
remain limited to `Int32.MaxValue`, including sparse grids. This correction
preserves forward centers, but grids previously affected by inverse drift can
resolve different indices or normalize to different bounds; rebuild derived
dimensions and configuration keys from the authored input.

## Runtime Generations Validate Reuse

Pooling makes storage slots reusable, so `GridIndex` and `OccupantTicket.Slot`
are never sufficient identities by themselves. An active world receives a
process-unique token, each grid registration receives a nonrepeating generation
local to that world, and each occupant registration receives a process-unique
generation. `WorldVoxelIndex` and `OccupantTicket` carry those values so stale
references fail instead of aliasing replacement state.

These generations are transient validation metadata. They are not serialized,
durable across processes, or authoritative ordering inputs. Persist host-owned
content identity such as `IVoxelOccupant.GlobalId`, then resolve fresh runtime
handles when rebuilding a world.

## Pooling Is A First-Class Constraint

Pooling in GridForge is not an optimization sprinkled on top. It shapes the
object lifecycle.

The internal pools cover types such as:

- `VoxelGrid`
- `Voxel`
- `ScanCell`
- scan-cell maps
- neighbor arrays
- temporary query lists and hash sets

Every new mutable field introduced into a pooled type needs a matching reset
story.

## Practical Debugging Checklist

1. Was the grid created with the topology metrics you think it was?
2. What are the snapped bounds after normalization?
3. Is the queried world-space position exactly on a boundary?
4. Are you looking at a pooled object or temporary collection after its intended
   lifetime?
5. Did a previous test, tool run, or benchmark leave world state active longer
   than intended?
