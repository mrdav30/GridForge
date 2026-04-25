# Scan Cells and Query Flow

This page covers the scan side of GridForge: why scan cells exist, how occupants are indexed into them, and how query APIs use that structure to avoid expensive whole-grid scans.

If voxels are the primary cell model, scan cells are the query acceleration overlay built on top of that model.

## Why Scan Cells Exist

A pure voxel-by-voxel occupant scan does not scale well. Many queries only need to inspect regions that might contain occupants at all.

Scan cells solve that by grouping voxels into larger buckets:

- `ScanCellSize` is measured in voxels
- one scan cell represents a region of neighboring voxels
- occupants are indexed into the scan cell that contains their voxel
- grids only track scan cells that are currently active

This lets many queries start with "which scan cells matter?" instead of "which voxels exist?"

## How The Overlay Is Built

Each `VoxelGrid` generates its scan-cell overlay at initialization time.

The grid derives:

- scan-cell width
- scan-cell height
- scan-cell length
- a linear `CellKey` for each scan cell

Every voxel then stores its `ScanCellKey`, which makes later occupant registration and scan retrieval straightforward.

## What A `ScanCell` Stores

`ScanCell` owns four important pieces of runtime state:

| Member | Purpose |
| --- | --- |
| `GridIndex` | Which grid this scan cell belongs to |
| `CellKey` | Grid-local scan-cell identity |
| `CellOccupantCount` | How many occupants are currently indexed here |
| `_voxelOccupants` | Buckets of occupants grouped by `WorldVoxelIndex` |

That last point matters a lot: a scan cell is not just a flat bag of occupants. It preserves which voxel each occupant came from.

## The Occupant Registration Flow

When an occupant is added through `GridOccupantManager`, the flow looks like this:

```text
occupant position or voxel target
  -> resolve target voxel
  -> resolve target scan cell
  -> add occupant into scan-cell bucket keyed by WorldVoxelIndex
  -> store occupant ticket
  -> increment voxel occupant count
  -> ensure the grid marks that scan cell as active
  -> publish occupant-added notifications
```

The ticket returned by the scan-cell bucket is important because it enables targeted retrieval later without rescanning the entire bucket.

## Active Scan Cells

`VoxelGrid.ActiveScanCells` is the summary structure that tells the grid which scan cells currently matter.

This has two architectural benefits:

- the grid can quickly tell whether it is occupied at all
- later query flows can focus on scan cells with occupants instead of empty ones

When the last occupant leaves a scan cell, that scan cell is removed from the active set. When the grid no longer has any active scan cells, the active-set collection itself is released.

## Query Flow For Radius Scans

`GridScanManager.ScanRadius(...)` is the clearest example of the scan architecture in action.

At a high level it does this:

1. derive a bounding box from center and radius
2. use `GridTracer.GetCoveredScanCells(...)` to find candidate scan cells
3. skip scan cells that are not occupied
4. enumerate occupants from the remaining scan cells
5. apply optional occupant and group filters
6. apply the final squared-distance check

So the query gets cheaper in layers:

- coarse world-space region
- candidate scan cells
- only occupied scan cells
- optional filters
- exact distance

## Query Types Built On The Overlay

`GridScanManager` exposes several flavors of occupant retrieval:

| Query Shape | Example |
| --- | --- |
| Radius scan | `ScanRadius(...)` |
| Type-filtered radius scan | `ScanRadius<T>(...)` |
| Occupants at one voxel | `GetOccupants(...)` |
| Type-filtered occupants at one voxel | `GetVoxelOccupantsByType<T>(...)` |
| Predicate-based occupants at one voxel | `GetConditionalOccupants(...)` |
| Ticket-based occupant lookup | `TryGetVoxelOccupant(...)` |

These all build on the same underlying relationship:

voxel -> scan cell -> occupant bucket -> optional filters

## Group Filtering

The scan system has a dedicated concept for occupant groups through `OccupantGroupId`.

Architecturally, this means grouping is not an afterthought layered on top of the results. It is part of the occupant contract itself and can be used directly during scan-cell enumeration.

Good uses for group filtering:

- factions or teams
- sensor channels
- unit categories
- domain-specific query partitions

## Ticket-Based Retrieval

When an occupant is stored in a scan-cell bucket, it receives a ticket. That ticket, combined with `WorldVoxelIndex`, can later be used to retrieve the exact occupant directly.

GridForge tracks that relationship internally:

- it remembers the voxel identity
- it remembers the ticket used inside the scan-cell bucket

That design makes removal and exact lookup much cheaper than searching the whole scan cell by value, without forcing every `IVoxelOccupant` implementation to carry a parallel mutable map. When you need the tracked relationship explicitly, use `GridOccupantManager.GetOccupiedIndices(...)` and `GridOccupantManager.TryGetOccupancyTicket(...)`.

## Why Query APIs Still Check Distance

Scan cells are acceleration structures, not exact geometry answers.

A scan-radius query first asks:

- which scan cells overlap the area?

Then it still asks:

- which occupants inside those scan cells are actually within the radius?

That second step matters because a scan cell can contain many voxels and therefore many occupants that are nearby in bucket terms but not truly in range.

## Performance Characteristics

The scan architecture performs best when:

- `ScanCellSize` is tuned to your occupancy patterns
- occupancy is sparse enough that active scan cells are meaningfully fewer than all scan cells
- scans are common enough to benefit from the overlay

Tradeoffs to remember:

- smaller scan cells improve locality but increase overlay count
- larger scan cells reduce overlay count but increase per-query false positives

This is why scan-cell size belongs in `GridConfiguration` instead of being hardcoded globally.

## Where Scan State Lives

It helps to keep the ownership split clear:

- `Voxel` owns whether it is occupied and how many occupants it has
- `ScanCell` owns occupant buckets and tickets
- `VoxelGrid` owns the set of active scan cells
- `GridScanManager` owns query orchestration
- `GridOccupantManager` owns add/remove mutation workflows

No single type does all of it, and that separation is intentional.

## Practical Design Notes

- Scan cells only exist inside one grid. Cross-grid scan queries are assembled by tracer coverage, not by shared scan-cell storage.
- Empty scan cells still exist as part of the grid overlay, but only occupied ones participate in active occupancy summaries.
- The same scan cell can contain occupants from multiple voxels, which is why voxel-bucket grouping still exists inside the scan cell.

## Common Mistakes

- Treating `ScanCellSize` as world units instead of voxel count
- Forgetting that radius scans still apply an exact distance check after scan-cell enumeration
- Assuming a scan cell corresponds to exactly one voxel
- Re-creating your own occupant ticket map instead of using GridForge's tracked occupancy helpers when you need precise removal or retrieval

## Read This Next

- [VoxelGrid and Voxel Model](VoxelGrid-and-Voxel-Model) for how scan cells relate back to voxels and grid-local keys
- [GridTracer and Coverage](GridTracer-and-Coverage) for how region coverage finds candidate scan cells
- [Common Workflows](Common-Workflows) for usage-oriented occupant and scan examples
