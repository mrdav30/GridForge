# Occupants and Partitions

This page covers the two main extension-oriented ways to attach runtime meaning to a voxel:

- occupants, which represent dynamic presence in the grid
- partitions, which attach typed metadata or behavior directly to a voxel

They solve different problems, and using the right one keeps both the code and the mental model cleaner.

## The Core Difference

### Occupants

Occupants answer "what dynamic thing is currently here?"

They are built for entities that move, enter and leave voxels, participate in scan queries, and need runtime lookup by position, group, or ticket.

### Partitions

Partitions answer "what typed metadata or behavior is attached to this voxel?"

They are built for durable voxel-local extensions such as terrain tags, domain-specific rules, or custom behaviors that should live with the voxel itself instead of inside the scan system.

## The Occupant Contract

`IVoxelOccupant` is the interface that scan and occupant workflows depend on.

It requires:

- `GlobalId` for stable identity
- `Position` for world-space resolution
- `OccupantGroupId` for grouped query filtering

GridForge owns occupancy bookkeeping internally. If you need to inspect what GridForge is currently tracking for an occupant, use:

- `GridOccupantManager.GetOccupiedIndices(...)`
- `GridOccupantManager.TryGetOccupancyTicket(...)`

That keeps the tracked `WorldVoxelIndex` plus scan-cell ticket relationship inside GridForge instead of forcing every consumer implementation to mirror it manually.

## How Occupants Are Stored

Occupants are managed through `GridOccupantManager` and stored indirectly through `ScanCell`.

The flow looks like this:

1. resolve the target voxel
2. find the voxel's owning scan cell
3. insert the occupant into the scan cell bucket for that voxel's `WorldVoxelIndex`
4. record the returned ticket in GridForge's internal occupancy registry
5. increment the voxel's occupant count
6. ensure the scan cell is tracked in `grid.ActiveScanCells`
7. publish manager-level and voxel-level occupant events

That means occupants are both:

- voxel-local for exact placement
- scan-cell indexed for efficient query flow

## Add And Remove Behavior

### Add behavior

The add path rejects a few important cases:

- null occupants
- duplicate registration to the same voxel identity
- voxels without vacancy
- voxels whose scan cell cannot be resolved

If the add succeeds, the target voxel's `OccupantCount` increases and the owning scan cell becomes active for later scans.

### Remove behavior

Removal uses GridForge's tracked occupancy record to recover the scan-cell ticket that was assigned at add time.

If removal succeeds:

- the scan cell bucket entry is removed
- the tracked registry entry for that voxel is removed
- the voxel's `OccupantCount` decreases
- empty scan cells are dropped from `grid.ActiveScanCells`
- the active scan-cell set itself is released back to a pool when the grid becomes unoccupied

That last point is worth calling out because it is easy to forget: active scan-cell tracking is intentionally allocation-conscious and can disappear entirely when no occupants remain.

## Why Group Ids Exist

`OccupantGroupId` is the lightweight grouping hook used by scan queries.

Typical examples:

- ally vs enemy teams
- factions or ownership groups
- sensor or channel ids
- simulation categories

This lets query code filter cheaply without forcing every caller to bolt on its own secondary indexing scheme.

## Occupants And Scan Queries

Occupants matter architecturally because they are what give `ScanCell` its purpose.

Instead of scanning every voxel in a region and then asking each voxel for a list of entities, GridForge can:

1. find relevant scan cells
2. inspect only active cells
3. enumerate occupants from those cells
4. apply optional group or occupant predicates

That is why the occupant system is tightly coupled to scan-cell activation state.

## Common Occupant Entry Points

Use `GridOccupantManager.TryRegister(occupant)` when:

- the occupant already knows its world-space `Position`
- you want the manager to resolve the correct grid and voxel automatically

Use `grid.TryAddVoxelOccupant(...)` overloads when:

- you already know the target grid
- you already know the target `Voxel` or `VoxelIndex`
- you want to avoid repeating global lookup

Use `TryDeregister(...)` or `TryRemoveVoxelOccupant(...)` when:

- cleaning up an entity
- moving an occupant from one voxel to another
- unloading game state tied to a grid

`TryDeregister(...)` is especially useful when an occupant has already moved in world space. GridForge removes the occupant from the voxel registrations it is actually tracking rather than relying on the occupant's current `Position`.

## The Partition Contract

`IVoxelPartition` is much smaller because partitions do not live in scan cells.

It requires:

- `WorldIndex` for the parent voxel identity
- `SetParentIndex(...)` so the voxel can assign ownership
- `OnAddToVoxel(...)`
- `OnRemoveFromVoxel(...)`

A partition is attached directly to a `Voxel`, not routed through a manager-wide query overlay.

## How Partitions Are Stored

Partitions are stored in a `PartitionProvider<IVoxelPartition>` on the voxel.

The provider keys partitions by their exact concrete `Type`, which has two important consequences:

- only one partition of a given concrete type can be attached at once
- two different concrete types can coexist even if they happen to share the same simple type name

This exact-type rule shows up clearly in the tests and is an important part of the mental model.

## Partition Lifecycle Behavior

`Voxel.TryAddPartition(...)` does more than just store the instance.

It:

1. rejects null
2. reserves the concrete type key in the provider
3. sets the partition's parent index
4. calls `OnAddToVoxel(...)`

If `OnAddToVoxel(...)` throws, the voxel rolls the provider state back and logs the failure. That is a good safety property: failed attachment does not leave behind a half-registered partition entry.

`Voxel.TryRemovePartition<T>()` behaves a little differently:

1. it removes the partition from the provider first
2. it calls `OnRemoveFromVoxel(...)`
3. if the callback throws, the failure is logged but the partition remains removed

That makes removal resilient and prevents a bad cleanup callback from trapping the voxel in a stale attached state.

## When To Use An Occupant Vs A Partition

Use an occupant when:

- the thing is dynamic
- it should participate in scan queries
- it needs grouped runtime filtering
- it may move, register, deregister, or be tracked across more than one occupied voxel over time

Use a partition when:

- the data is voxel-local metadata or behavior
- you want typed retrieval directly from the voxel
- the concept does not belong in scan results
- the attachment should live with the voxel rather than with a moving entity

If you find yourself inventing a fake occupant just to tag a voxel with metadata, that usually wants to be a partition instead.

## Common Pitfalls

- Treating occupants as durable metadata instead of dynamic runtime presence
- Forgetting that blocked voxels do not have vacancy for normal occupant registration
- Re-implementing your own occupancy bookkeeping when GridForge already tracks the voxel/ticket relationship
- Assuming `TryDeregister(...)` only works while `occupant.Position` still points at the registered voxel
- Assuming partitions are resolved by interface hierarchy instead of exact concrete type
- Expecting partition remove callbacks to keep the partition attached when they throw

## Read This Next

- [Scan Cells and Query Flow](Scan-Cells-and-Query-Flow) for the query side of occupant storage
- [VoxelGrid and Voxel Model](VoxelGrid-and-Voxel-Model) for voxel-local state and lifecycle
- [Diagnostics and Logging](Diagnostics-and-Logging) for how occupant and partition failures are surfaced
