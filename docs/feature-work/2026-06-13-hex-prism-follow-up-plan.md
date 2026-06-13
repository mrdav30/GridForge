# Hex Prism Follow-Up Plan

**Status:** Active

**Goal:** Track topology work intentionally deferred from the first hex-prism
release so the completed implementation plan can stay closed without losing
future design context.

**Scope:** This plan is for follow-up validation and design work only. The first
hex-prism release already supports rectangular and hex grids in one
`GridWorld`, topology-aware lookup, tracing, blockers, occupants, scans,
benchmarks, and documentation.

## Deferred Work

### 1. Mixed-Topology Voxel Neighbor Bridging

Status: planned.

Intent: decide whether rectangular-prism and hex-prism grids should ever expose
direct voxel-neighbor bridges across topology boundaries.

Current state:

- Same-topology voxel lookup is direction-based:
  `TryGetRectangularNeighbor(...)` uses `RectangularDirection`, and
  `TryGetHexNeighbor(...)` uses `HexDirection`.
- `VoxelGrid.Neighbors` stores same-topology conjoined grids by topology-local
  neighbor slot. Mixed rectangular/hex grids intentionally do not enter this
  slot map today.
- Mixed rectangular/hex lookup, tracing, blockers, occupants, and scans already
  work at the `GridWorld` level; only direct voxel-neighbor bridging is
  deferred.

Preferred design:

- Keep rectangular and hex direction APIs same-topology only.
- Add a separate mixed-topology contact query instead of adding fake
  rectangular/hex direction mappings.
- Treat mixed bridging as one-to-many: a source voxel can touch zero, one, or
  many voxels from grids that use a different topology.
- Use world-space voxel footprint AABBs as the deterministic contact model.
  Rectangular voxels use half rectangular cell extents around `WorldPosition`.
  Hex voxels use an orientation-aware prism AABB:
  - pointy-top half extents: `X = Sqrt3 * radius / 2`, `Z = radius`
  - flat-top half extents: `X = radius`, `Z = Sqrt3 * radius / 2`
  - both use `Y = layerHeight / 2`
- Use the source AABB as the query outline, expand by an optional fixed-point
  tolerance only when the caller requests it, and verify target voxel AABB
  overlap before returning a result.
- Return results in deterministic order by target grid index, then
  topology-local voxel index. Do not rely on hash-set iteration order.
- Keep sparse semantics intact: missing sparse voxels are absent and are never
  materialized by bridge queries.

Approaches considered:

- Direction-slot mapping: rejected because rectangular and hex directions do
  not have a stable one-to-one mapping and this would make the public API
  ambiguous again.
- Direct `GridTracer.GetCoveredVoxels(...)` wrapper: useful as a reference, but
  too broad as the core hot-path implementation.
- AABB contact query: selected because it is deterministic, fast to broad-phase
  through the spatial hash, works for rectangular and both hex orientations,
  and makes one-to-many mixed contacts explicit. This is an AABB contact model,
  not exact hex polygon intersection.

Questions to answer:

- Should the public API live on `Voxel`, `GridWorld`, or a dedicated query
  helper? Current recommendation: expose the friendly entry point on `Voxel`
  and put the implementation in an internal resolver.
- Should results be raw `Voxel` values or a small result struct with the target
  grid and overlap metadata? Current recommendation: start with raw `Voxel`
  results plus caller-owned result storage; add metadata only if a real caller
  needs it.
- Should an enumerable convenience API exist? Current recommendation: add an
  allocation-conscious `GetMixedTopologyNeighborsInto(...)` first, then add an
  enumerable wrapper only if it can match existing API ergonomics without
  hiding pooled lifetime rules.

Candidate public API:

```csharp
public void GetMixedTopologyNeighborsInto(
    VoxelGrid ownerGrid,
    SwiftList<Voxel> results,
    Fixed64? tolerance = null);

public bool HasMixedTopologyNeighbor(
    VoxelGrid ownerGrid,
    Fixed64? tolerance = null);
```

Possible follow-up convenience, only if the implementation can document result
lifetime clearly:

```csharp
public IEnumerable<Voxel> GetMixedTopologyNeighbors(
    VoxelGrid ownerGrid,
    Fixed64? tolerance = null);
```

Implementation phases:

#### Phase 1: Footprint And Candidate Foundations

Goal: add the internal geometry needed for mixed-topology contact without
changing public behavior.

Tasks:

- Add an internal lightweight footprint type, likely
  `TopologyVoxelAabb`, storing `Vector3d Min` and `Vector3d Max`.
- Add an internal topology method or helper that derives a voxel footprint AABB
  from `(VoxelGrid grid, VoxelIndex index)`.
- Implement rectangular footprint bounds from `CellWidth`, `LayerHeight`, and
  `CellLength`.
- Implement hex footprint bounds from `CellRadius`, `LayerHeight`,
  `HexOrientation`, and the existing GridForge-local `Sqrt3`.
- Add fixed-point AABB overlap helpers with inclusive edge contact and optional
  tolerance expansion.
- Add focused tests for rectangular, pointy-top hex, and flat-top hex footprint
  bounds.

Exit criteria:

- Footprint bounds are deterministic, allocation-free, and covered for
  rectangular, pointy-top hex, and flat-top hex cells.
- No public API behavior changes yet.

#### Phase 2: Internal Mixed-Topology Resolver

Goal: implement the bridge as a storage-neutral, topology-aware query.

Tasks:

- Create an internal resolver, likely under `src/GridForge/Spatial` or
  `src/GridForge/Grids/Topology`, that accepts a source `Voxel`, its owner
  `VoxelGrid`, a caller-owned `SwiftList<Voxel>` result buffer, and optional
  tolerance.
- Validate source ownership using the same world/grid/spawn-token checks used
  by existing voxel neighbor methods.
- Collect candidate grid ids through the world's spatial hash using the source
  footprint AABB expanded by `world.MaxTopologyCellEdge`.
- Deduplicate candidate grid ids and process them in ascending grid index
  order.
- Skip inactive grids, the source grid, and same-topology grids.
- For each mixed candidate grid, derive a topology-specific candidate index
  range from the source AABB, using the existing `GridTracer` hex coverage
  strategy as the model rather than duplicating ad hoc math.
- Iterate configured target voxels only, so dense and sparse storage remain
  neutral.
- Return a target voxel only when its footprint AABB overlaps the source
  footprint AABB with the selected tolerance.

Exit criteria:

- The resolver returns deterministic, duplicate-free mixed-topology contacts
  without changing `VoxelGrid.Neighbors` or same-topology caches.
- Sparse targets, unloaded grids, inactive grids, and same-topology grids are
  skipped.

#### Phase 3: Public API And Same-Topology Isolation

Goal: expose the feature without making existing neighbor APIs confusing again.

Tasks:

- Add `Voxel.GetMixedTopologyNeighborsInto(...)` as the primary public API.
- Add `Voxel.HasMixedTopologyNeighbor(...)` for fast boolean checks that can
  early-exit without filling a result list.
- Decide whether an enumerable `GetMixedTopologyNeighbors(...)` wrapper is
  worth adding after the caller-owned path is in place.
- Keep `TryGetRectangularNeighbor(...)`, `GetRectangularNeighbors(...)`,
  `TryGetHexNeighbor(...)`, and `GetHexNeighbors(...)` same-topology only.
- Keep `VoxelGrid.GetRectangularNeighborDirection(...)` and
  `VoxelGrid.GetHexNeighborDirection(...)` same-topology only.
- Keep `VoxelGrid.Neighbors` as a same-topology grid-slot map unless benchmarks
  prove that mixed queries need a separate cache.

Exit criteria:

- Public APIs make the one-to-many mixed bridge obvious.
- No mixed-topology behavior is hidden behind rectangular or hex direction
  enums.

#### Phase 4: Validation Matrix

Goal: prove correctness across orientations, directions, sparse storage, and
load/unload behavior.

Required tests:

- Rectangular source voxel resolves one or more pointy-top hex contacts.
- Pointy-top hex source voxel resolves rectangular contacts.
- Rectangular source voxel resolves flat-top hex contacts.
- Flat-top hex source voxel resolves rectangular contacts.
- A source voxel that touches multiple target voxels returns all of them in
  deterministic order.
- A source voxel near a hex AABB corner does not return non-overlapping target
  voxels after final AABB overlap filtering.
- Sparse mixed target grids return configured voxels only.
- Runtime sparse add/remove changes mixed bridge results without stale cache
  behavior.
- Grid unload removes mixed bridge results.
- Same-topology neighbor APIs and same-topology conjoined grid counts remain
  unchanged.
- Vertical contact respects layer AABBs and does not bridge across separated
  layers.

Validation commands:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --filter "MixedTopology|HexPrismGrid|Voxel"
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet test GridForge.slnx --configuration Release
dotnet test GridForge.slnx --configuration ReleaseLean
```

#### Phase 5: Performance Hardening

Goal: make sure the mixed bridge is useful without turning a voxel neighbor
query into a broad world scan.

Tasks:

- Add benchmark cases if the resolver lands:
  - no mixed candidates nearby
  - one pointy-top hex candidate
  - one flat-top hex candidate
  - many candidate grids in nearby spatial hash cells
  - sparse target with mostly missing candidate cells
- Verify candidate grid collection stays bounded by spatial hash coverage, not
  total active grid count.
- Verify hot paths avoid LINQ and avoid allocations when callers use
  `GetMixedTopologyNeighborsInto(...)`.
- Only add caching if benchmark data proves the uncached resolver is too slow;
  mixed result caching would require careful invalidation for grid load/unload,
  sparse mutation, and topology-specific footprint changes.

Exit criteria:

- Benchmarks either show the uncached spatial-hash resolver is acceptable or
  capture a measured reason to add a dedicated mixed-neighbor cache.

#### Phase 6: Documentation And Plan Closure

Goal: document the final contract without implying direction-based mixed
neighbors.

Tasks:

- Update `docs/wiki/VoxelGrid-and-Voxel-Model.md` with the mixed-topology
  contact-query semantics.
- Update `docs/wiki/Architecture-Overview.md` to distinguish same-topology
  grid-slot neighbors from mixed-topology contact queries.
- Update `docs/wiki/Testing-and-Benchmarking.md` with the mixed topology test
  and benchmark surfaces.
- Update README only if the public API becomes important enough for the front
  door.
- Mark this follow-up plan `**Status:** Done` and move it under
  `docs/feature-work/done` only after sparse hex validation and mixed topology
  bridging are both complete.

Exit criteria:

- Mixed-topology voxel bridging is either implemented as a tested, benchmarked,
  caller-friendly contact query, or the plan explicitly records why it should
  remain unsupported.

### 2. Sparse Hex-Prism Validation

Status: completed on 2026-06-13.

Intent: validate the combined sparse-storage plus hex-topology path explicitly
before documenting it as a recommended workflow.

Checklist:

- [x] Add tests for sparse hex construction from topology-local `(q, layer, r)`
  configured indices.
- [x] Add lookup, coverage, blocker, occupant, scan, and runtime mutation coverage
  for sparse hex grids.
- [x] Evaluate whether benchmark coverage is needed beyond dense hex and sparse
  rectangular grid coverage.
- [x] Update sparse and topology docs if sparse hex becomes a highlighted scenario.

Result:

- Sparse hex grids are supported as configured topology-local axial cells.
- Focused tests cover construction, missing lookup, tracing, scan cells,
  blockers, occupant registration, radius scans, and runtime sparse mutation.
- No benchmark was added because validation did not expose a different
  performance shape from the existing sparse-storage and hex-topology benchmark
  surfaces.

Exit criteria:

- Sparse hex behavior is documented as supported with explicit coverage.
