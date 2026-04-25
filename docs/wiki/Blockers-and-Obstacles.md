# Blockers and Obstacles

This page covers two related but different ideas in GridForge:

- obstacle state on a voxel
- blocker workflows that apply or remove that state from world-space bounds

If you keep that distinction clear, the rest of the subsystem makes much more sense.

## The Core Difference

### Obstacles

An obstacle is voxel-level state.

At runtime, a `Voxel` is considered blocked when its `ObstacleCount` is greater than zero. That is the low-level fact other systems react to.

### Blockers

A blocker is a higher-level helper.

It starts with world-space bounds, traces the covered voxels, and then applies or removes obstacle state across all touched grids. In other words, blockers are built on top of obstacle mutation, not the other way around.

## The Obstacle Layer

`GridObstacleManager` is the main mutation service for obstacle state. Its job is to centralize:

- per-grid locking
- obstacle token tracking
- obstacle count updates on voxels
- obstacle summary updates on the owning grid
- event publication
- grid version and active-grid change notifications

Architecturally, this matters because obstacle mutation is not just flipping a boolean. It is coordinated state that affects:

- `Voxel.IsBlocked`
- `Voxel.HasVacancy`
- `VoxelGrid.ObstacleCount`
- grid versioning
- listeners that care about obstacle changes

## Why Obstacles Use Tokens Instead Of A Simple Bool

Obstacle state is keyed by a `BoundsKey` token rather than stored as a single "blocked/unblocked" flag.

That gives the system the behavior it needs for stacked coverage:

- multiple blockers can cover the same voxel
- a manual obstacle and a blocker can coexist
- removing one token does not automatically clear the others
- duplicate application of the same token should not keep increasing the count

This is why a voxel can be blocked even after one blocker is removed: some other obstacle token may still be present.

## The Blocker Abstraction

`Blocker` is the reusable base class for "apply obstacle coverage to a region" behavior.

It owns:

- active vs inactive status
- the cached bounds used for the current blockage
- the derived `BlockageToken`
- optional caching of covered voxel identities
- the logic for applying, removing, and reapplying coverage
- static blocker-level events for apply and remove notifications

`BoundsBlocker` is the concrete world-space implementation currently in the repo. It wraps a `BoundingArea` and supplies the min and max bounds that the base class needs.

## Apply Flow

When `ApplyBlockage()` succeeds, the flow is roughly:

1. reject the call if the blocker is inactive or already blocking
2. cache the current min and max bounds
3. derive a `BoundsKey` token from those bounds
4. optionally initialize covered-voxel caching
5. register this blocker as a watcher for later grid add or remove events
6. enumerate `GridTracer.GetCoveredVoxels(...)`
7. apply the blocker token to every covered voxel through grid obstacle APIs
8. record which grids were covered so reapply logic knows what to watch
9. publish the blocker-level apply event if coverage was found and applied

The important architectural detail is that blocker coverage is resolved at apply time. The blocker does not store direct voxel references unless you explicitly opt into caching.

## Remove Flow

`RemoveBlockage()` clears only the obstacle state introduced by that blocker token.

There are two removal strategies:

### Retrace removal

If covered-voxel caching is disabled, the blocker re-runs `GridTracer.GetCoveredVoxels(...)` over the cached bounds and removes its token from the currently covered voxels.

### Cached-index removal

If `CacheCoveredVoxels` is enabled, the blocker stores stable `WorldVoxelIndex` values when it applies. Removal then uses those identities directly instead of retracing.

That is a good trade when:

- the blocker will be toggled often
- the covered region is large
- you want removal to stay stable even if pooled runtime objects are reused

The tradeoff is memory: you are keeping an extra list of covered voxel identities alive for the blocker.

## Grid Watcher Behavior

One of the more important design details lives in the world binding for `Blocker`.

The blocker base subscribes to:

- `GridWorld.OnActiveGridAdded`
- `GridWorld.OnActiveGridRemoved`
- `GridWorld.OnReset`

That lets active blockers react when the registered grid set changes inside their owning world.

### Why this exists

A bounds-based blocker may span multiple grids, and those grids are allowed to load or unload over time. If blocker coverage only ran once, the world could drift out of sync as grids change.

### What happens on grid add

If a newly added grid overlaps the blocker's cached bounds, the blocker reapplies itself so the new grid receives the correct obstacle state.

### What happens on grid removal

If a removed grid was one the blocker had previously covered, the blocker reapplies itself across the remaining world state.

### What happens on reset

World reset clears blocker watcher registration and clears active blocking state. Treat reset as the hard boundary for blocker lifetime.

## Stacking Behavior

The tests make the intended stacking semantics pretty clear:

- multiple blockers can cover the same voxel
- removing one blocker does not clear the others
- edge voxels should still resolve correctly
- one blocker can cover multiple grids
- active blockers can reapply when overlapping grids are re-added later

This is why blockers are safe to use for temporary world-space effects like:

- doors or gates
- placed structures
- streamed-in map hazards
- editor-authored blocked regions

## Obstacles And Occupancy Interact

Obstacle state affects vacancy checks.

`Voxel.HasVacancy` requires the voxel to be unblocked and below the occupant count limit. In practice that means a blocked voxel is not considered available for occupant placement through the normal occupant manager flow.

That interaction is worth remembering whenever occupancy registration "mysteriously" starts failing after blocker work.

## Events

There are two event layers in this subsystem.

### Blocker-level events

`Blocker.OnBlockageApplied` and `Blocker.OnBlockageRemoved` describe the lifecycle of a specific blocker coverage operation.

### Obstacle-level events

Obstacle mutation also produces lower-level notifications through the obstacle APIs and the affected `Voxel` instances.

The architecture uses both layers for different purposes:

- blocker events describe region-level intent
- obstacle events describe voxel-level mutation

## When To Use A Blocker Vs Direct Obstacle Mutation

Use a blocker when:

- you start with world-space bounds
- coverage may span multiple grids
- you want easy reapply behavior as grids load or unload
- you need to remove exactly the region-level mutation later

Use direct obstacle APIs when:

- you already know the exact target voxel
- the mutation is truly voxel-local
- you do not need blocker lifecycle behavior or bounds tracing

## Common Pitfalls

- Treating blockers as if they were the stored obstacle state instead of a producer of that state
- Forgetting that stacked tokens can keep a voxel blocked after one blocker is removed
- Using cached covered voxels everywhere without considering memory cost
- Assuming blocker coverage is limited to a single grid
- Forgetting that world reset or disposal is the authoritative cleanup boundary for active blockers

## Read This Next

- [GridTracer and Coverage](GridTracer-and-Coverage) for how covered voxels are resolved
- [Occupants and Partitions](Occupants-and-Partitions) for the other major mutation layer that sits on top of voxel state
- [Diagnostics and Logging](Diagnostics-and-Logging) for how blocker and obstacle failures are surfaced safely
