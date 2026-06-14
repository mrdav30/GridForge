# Hex Prism Follow-Up Plan

**Status:** Done

**Goal:** Tracked topology work intentionally deferred from the first
hex-prism release so the completed implementation plan could stay closed
without losing design context.

**Scope:** This plan is for follow-up validation and design work only. The first
hex-prism release already supports rectangular and hex grids in one
`GridWorld`, topology-aware lookup, tracing, blockers, occupants, scans,
benchmarks, and documentation.

## Completed Work

### 1. Mixed-Topology Voxel Neighbor Bridging

Status: completed on 2026-06-14.

Intent: decide whether rectangular-prism and hex-prism grids should ever expose
direct voxel-neighbor bridges across topology boundaries.

Current state:

- Same-topology voxel lookup is direction-based through `TryGetNeighbor(...)`
  overloads that accept `RectangularDirection` or `HexDirection`.
- `VoxelGrid.Neighbors` stores same-topology conjoined grids by topology-local
  neighbor slot. Mixed rectangular/hex grids intentionally do not enter this
  slot map.
- Mixed rectangular/hex lookup, tracing, blockers, occupants, and scans already
  work at the `GridWorld` level.
- Direct mixed voxel-neighbor bridging is now exposed through
  `GetNeighborsInto(...)` / `HasNeighbor(...)` with `VoxelNeighborScope`
  instead of mixed-only public method names.
- Hex directed lookup now uses orientation-neutral axial `HexDirection` names
  such as `QPositive`, `QPositiveRNegative`, and `RNegative` rather than
  compass-style names.
- The public contract now makes "which neighbors touch this voxel?" easy to ask
  without forcing callers to remember which topology-specific API applies.

Preferred design:

- Split the public model by query intent:
  - contact queries answer "which physical voxels touch this voxel?"
  - directed queries answer "which voxel exists at this topology-local
    direction?"
- Add a unified no-allocation contact query:
  `Voxel.GetNeighborsInto(ownerGrid, results, VoxelNeighborScope.All)`.
- Add a unified fast boolean contact query:
  `Voxel.HasNeighbor(ownerGrid, VoxelNeighborScope.All)`.
- Keep directed same-topology lookup through overloads:
  `TryGetNeighbor(ownerGrid, RectangularDirection direction, out Voxel?)` and
  `TryGetNeighbor(ownerGrid, HexDirection direction, out Voxel?)`.
- Keep no-allocation direction-aware result paths:
  `GetRectangularNeighborsInto(...)` and `GetHexNeighborsInto(...)`.
- Remove public `useCache` parameters. Cache policy is an implementation
  detail, not something callers should need to choose for correctness.
- Remove the current per-voxel neighbor result cache unless benchmarks later
  prove a new cache is worth the invalidation complexity.
- Treat contact lookup as one-to-many: a source voxel can touch zero, one, or
  many voxels from the source grid, same-topology grids, mixed-topology grids,
  or all of them depending on the requested `VoxelNeighborScope`.
- Use world-space voxel footprint AABBs as the deterministic contact model for
  broad and final contact checks. Rectangular voxels use half rectangular cell
  extents around `WorldPosition`. Hex voxels use an orientation-aware prism
  AABB:
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
- `HexDirection` values use orientation-neutral axial labels instead of
  world-compass labels. The values are axial offsets, so names such as
  `QPositive` and `RNegative` describe both `PointyTop` and `FlatTop` grids.

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
- AABB-only directed lookup: rejected because directed APIs promise an exact
  topology-local offset, while contact APIs promise physical footprint overlap.
- Preserve the current per-voxel neighbor cache: rejected unless future
  benchmarks prove a new cache is necessary. The existing cache leaks into the
  API through `useCache`, complicates sparse mutation and topology changes, and
  pulls the design toward old rectangular-only assumptions.

Decisions:

- The public API lives on `Voxel`, with implementation in an internal
  `VoxelNeighborResolver` or equivalent focused resolver.
- Results are raw `Voxel` values because callers can already inspect
  `WorldIndex`, `Index`, `WorldPosition`, and owner grid state.
- Caller-owned result storage is the primary API. No enumerable wrapper was
  added in phases 1-4 because it would hide lifetime/allocation tradeoffs
  without a proven caller need.
- `VoxelGrid.Neighbors` may remain as an internal same-topology grid adjacency
  accelerator, but it should not be the public source of truth for neighbor
  discovery.
- `GridTracer` and neighbor discovery should share candidate-grid and
  candidate-voxel range helpers where their behavior overlaps. Tracing can keep
  trace-specific center/coverage rules; neighbor contact should keep footprint
  overlap rules.

Phase 1-4 public API, replaced during phase 5:

```csharp
public void GetMixedTopologyNeighborsInto(
    VoxelGrid ownerGrid,
    SwiftList<Voxel> results,
    Fixed64? tolerance = null);

public bool HasMixedTopologyNeighbor(
    VoxelGrid ownerGrid,
    Fixed64? tolerance = null);
```

Target public API:

```csharp
[Flags]
public enum VoxelNeighborScope : byte
{
    None = 0,
    SourceGrid = 1,
    SameTopologyGrids = 2,
    MixedTopologyGrids = 4,
    All = SourceGrid | SameTopologyGrids | MixedTopologyGrids
}

public void GetNeighborsInto(
    VoxelGrid ownerGrid,
    SwiftList<Voxel> results,
    VoxelNeighborScope scope = VoxelNeighborScope.All,
    Fixed64? tolerance = null);

public bool HasNeighbor(
    VoxelGrid ownerGrid,
    VoxelNeighborScope scope = VoxelNeighborScope.All,
    Fixed64? tolerance = null);

public bool TryGetNeighbor(
    VoxelGrid ownerGrid,
    RectangularDirection direction,
    out Voxel? neighbor);

public bool TryGetNeighbor(
    VoxelGrid ownerGrid,
    HexDirection direction,
    out Voxel? neighbor);

public void GetRectangularNeighborsInto(
    VoxelGrid ownerGrid,
    SwiftList<(RectangularDirection Direction, Voxel Voxel)> results);

public void GetHexNeighborsInto(
    VoxelGrid ownerGrid,
    SwiftList<(HexDirection Direction, Voxel Voxel)> results);
```

Implementation phases:

#### Phase 1: Footprint And Candidate Foundations

Status: completed on 2026-06-13.

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

Status: completed on 2026-06-13.

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

Status: completed on 2026-06-13.

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

Status: completed on 2026-06-13.

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

#### Phase 5: Public Neighbor API Refactor

Status: completed on 2026-06-13.

Goal: replace topology-fragmented public neighbor discovery with contact and
directed lookup APIs that are easier to discover and harder to misuse.

Tasks:

- Add `VoxelNeighborScope` as a `[Flags]` enum with `SourceGrid`,
  `SameTopologyGrids`, `MixedTopologyGrids`, and `All`.
- Replace `GetMixedTopologyNeighborsInto(...)` with
  `GetNeighborsInto(..., VoxelNeighborScope scope = VoxelNeighborScope.All,
  Fixed64? tolerance = null)`.
- Replace `HasMixedTopologyNeighbor(...)` with
  `HasNeighbor(..., VoxelNeighborScope scope = VoxelNeighborScope.All,
  Fixed64? tolerance = null)`.
- Replace `TryGetRectangularNeighbor(...)` and `TryGetHexNeighbor(...)` with
  `TryGetNeighbor(...)` overloads that accept `RectangularDirection` or
  `HexDirection`.
- Add no-allocation `GetRectangularNeighborsInto(...)` and
  `GetHexNeighborsInto(...)` APIs for callers that need direction-labeled
  same-topology results.
- Remove public `useCache` parameters from neighbor APIs.
- Remove or de-emphasize enumerable neighbor wrappers unless a clear caller need
  outweighs the hidden allocation/lifetime cost.

Exit criteria:

- A caller can ask for all touching neighbors through one primary API without
  branching on topology.
- Directed same-topology lookup remains explicit and exact.
- The public API no longer exposes cache policy.

Result:

- `VoxelNeighborScope`, `Voxel.GetNeighborsInto(...)`, and
  `Voxel.HasNeighbor(...)` are the primary contact-query API.
- Directed rectangular and hex lookup now use `TryGetNeighbor(...)` overloads.
- Direction-labeled no-allocation paths now use
  `GetRectangularNeighborsInto(...)` and `GetHexNeighborsInto(...)`.
- Enumerable neighbor wrappers and public `useCache` parameters were removed.

#### Phase 6: Resolver Consolidation And Cache Removal

Status: completed on 2026-06-13.

Goal: move neighbor discovery out of `Voxel`, remove the per-voxel neighbor
cache, and share candidate discovery logic without blurring contact semantics
with trace semantics.

Tasks:

- Create or rename to a single internal `VoxelNeighborResolver` under the
  topology/grid area.
- Move exact topology-offset lookup for directed neighbor overloads into the
  resolver.
- Move local same-grid all-neighbor lookup into the resolver.
- Move world-space footprint contact lookup for source-grid, same-topology grid,
  and mixed-topology grid scopes into the resolver.
- Replace `MixedTopologyNeighborResolver` once the unified resolver owns the
  mixed contact path.
- Remove `Voxel._cachedNeighbors`, `_isNeighborCacheValid`,
  `InvalidateNeighborCache()`, `EnsureNeighborCache(...)`,
  `RefreshNeighborCache(...)`, and related cache invalidation paths if no
  other behavior still requires them.
- Keep `VoxelGrid.Neighbors` only as an internal same-topology grid adjacency
  accelerator if it still improves grid load/unload behavior; do not let it
  define public neighbor semantics.
- Extract shared candidate-grid and candidate-voxel range helpers used by both
  neighbor contact and `GridTracer` where doing so removes duplicate broad-phase
  logic without weakening either system's final filter.
- Ensure all pooled collections are released in `finally` blocks and that
  result ordering remains deterministic.

Exit criteria:

- `Voxel` mostly validates ownership/nulls and forwards to the resolver.
- Per-voxel neighbor result caching is gone.
- The resolver owns neighbor behavior in one place and supports dense, sparse,
  rectangular, pointy-top hex, and flat-top hex grids.

Result:

- `VoxelNeighborResolver` now owns directed same-topology lookup,
  direction-labeled same-topology enumeration, and scoped footprint-contact
  lookup.
- `MixedTopologyNeighborResolver` was replaced by the unified resolver.
- The per-voxel neighbor result cache, invalidation methods, and storage
  boundary invalidation hooks were removed.
- `VoxelGrid.Neighbors` remains only as a same-topology grid-slot accelerator;
  public contact discovery resolves against current world/grid/storage state.

#### Phase 7: Direction Semantics And Validation Refresh

Status: completed on 2026-06-14.

Goal: make hex direction naming orientation-neutral and prove the unified API
does not regress rectangular, hex, sparse, or mixed neighbor behavior.

Tasks:

- [x] Rename `HexDirection` values from compass-style names to axial-offset
  names such as `QPositive`, `QPositiveRNegative`, `RNegative`, `QNegative`,
  `QNegativeRPositive`, and `RPositive`, plus matching above/below variants.
- [x] Update `HexDirectionUtility` arrays, subsets, XML docs, tests, and
  examples to use the new axial names.
- [x] Add tests for `VoxelNeighborScope.SourceGrid`,
  `VoxelNeighborScope.SameTopologyGrids`, `VoxelNeighborScope.MixedTopologyGrids`,
  and `VoxelNeighborScope.All`.
- [x] Add tests proving `GetNeighborsInto(...)` returns deterministic contact
  results across local, conjoined same-topology, and mixed-topology grids.
- [x] Add tests proving directed `TryGetNeighbor(...)` overloads remain exact,
  same-topology, and do not return mixed contact results.
- [x] Update existing rectangular and hex neighbor tests to use the new public API
  names and no-cache signatures.
- [x] Keep sparse runtime mutation, grid unload, and vertical-separation tests in
  the validation matrix.

Validation commands:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --filter "Neighbor|MixedTopology|HexDirection|HexPrismGrid|Voxel"
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet test GridForge.slnx --configuration Release
dotnet test GridForge.slnx --configuration ReleaseLean
```

Exit criteria:

- Hex direction names describe axial offsets rather than pointy-top compass
  intuition.
- Unified contact lookup and directed lookup are both covered by focused tests.
- Existing mixed-topology phase 1-4 behavior is preserved through the new API.

Result:

- `HexDirection` now uses axial labels for planar and layer-offset directions:
  `QPositive`, `QPositiveRNegative`, `RNegative`, `QNegative`,
  `QNegativeRPositive`, `RPositive`, and matching `Below...` / `Above...`
  variants.
- `Below` and `Above` remain direct vertical directions.
- `HexDirectionUtility` deterministic arrays and subset helpers now expose the
  axial names without changing slot order or topology-local offsets.
- Focused tests cover axial naming, deterministic subset composition,
  direction-labeled hex neighbor enumeration, same-topology conjoined hex
  lookup, and sparse/mixed neighbor validation through the existing unified API
  matrix.

#### Phase 8: Performance Hardening

Status: completed on 2026-06-14.

Goal: make sure unified neighbor discovery is useful without turning a voxel
neighbor query into a broad world scan.

Tasks:

- [x] Add benchmark cases for the unified resolver:
  - local source-grid only neighbors
  - same-topology conjoined-grid neighbors
  - no mixed candidates nearby
  - one pointy-top hex mixed candidate
  - one flat-top hex mixed candidate
  - many candidate grids in nearby spatial hash cells
  - sparse target with mostly missing candidate cells
- [x] Verify candidate grid collection stays bounded by spatial hash coverage, not
  total active grid count.
- [x] Verify hot paths avoid LINQ and avoid allocations when callers use
  `GetNeighborsInto(...)`, `GetRectangularNeighborsInto(...)`, and
  `GetHexNeighborsInto(...)`.
- [x] Compare the unified resolver against the removed per-voxel cache behavior if
  a prior benchmark exists, or capture a new baseline before considering any
  replacement cache.
- [x] Only add caching if benchmark data proves the uncached resolver is too slow.
  Any new cache must have explicit invalidation for grid load/unload, sparse
  mutation, and topology-specific footprint changes.

Result:

- Added `NeighborLookupBenchmarks` coverage for source-grid contacts,
  same-topology contacts, empty mixed-scope queries, pointy-top and flat-top
  mixed contacts, many nearby candidate grids, sparse mostly-missing targets,
  and rectangular/hex direction-labeled caller-owned result paths.
- Replaced per-query shared-pool scratch rentals inside
  `VoxelNeighborResolver` contact lookup with thread-local scratch storage and
  a reentrant fallback. This keeps the public API allocation-conscious without
  adding caller-visible cache controls.
- Kept source-grid contact neighbors in deterministic topology-slot order and
  sorted cross-grid candidate voxels only where spatial-range collection can
  produce non-directional ordering.
- BenchmarkDotNet ShortRun on 2026-06-14 showed the unified contact cases
  bounded by spatial-hash candidate coverage with sub-KB managed allocation per
  benchmark operation after warmup. Mixed pointy-top and flat-top contact
  scenarios measured near 2 ms in the short-run matrix; many nearby candidate
  grids remained the expected upper-bound case.
- No replacement neighbor cache was added. The measured uncached resolver does
  not justify cache invalidation complexity for grid load/unload, sparse
  mutation, and topology footprint changes.

Exit criteria:

- Benchmarks show the uncached unified resolver is acceptable for the current
  scenarios, so no new dedicated neighbor cache is planned.

#### Phase 9: Documentation And Plan Closure

Status: completed on 2026-06-14.

Goal: document the final contact-vs-directed neighbor contract without implying
fake direction mappings across topology families.

Tasks:

- [x] Update `docs/wiki/VoxelGrid-and-Voxel-Model.md` with the unified
  `GetNeighborsInto(...)` contact-query semantics and directed
  `TryGetNeighbor(...)` overload semantics.
- [x] Update `docs/wiki/Architecture-Overview.md` to distinguish same-topology
  grid-slot acceleration from public contact queries.
- [x] Update `docs/wiki/Core-Concepts.md` so users see one primary neighbor query
  and topology-specific directed overloads.
- [x] Update `docs/wiki/Testing-and-Benchmarking.md` with the unified neighbor test
  and benchmark surfaces.
- [x] Update README only if the public API becomes important enough for the front
  door.
- [x] Mark this follow-up plan `**Status:** Done` and move it under
  `docs/feature-work/done` only after sparse hex validation and mixed topology
  bridging/API hardening are complete.

Result:

- `docs/wiki/VoxelGrid-and-Voxel-Model.md`, `docs/wiki/Architecture-Overview.md`,
  and `docs/wiki/Core-Concepts.md` already describe the final
  contact-query-versus-directed-lookup model from phases 5-7.
- `docs/wiki/Testing-and-Benchmarking.md` was aligned in phase 8 with the
  unified neighbor benchmark matrix.
- README/AGENTS front-door wording was cleaned up to remove stale references to
  cached neighbor state.
- The completed follow-up plan is archived under `docs/feature-work/done`.

Exit criteria:

- Voxel neighbor discovery is implemented as a tested, benchmarked,
  caller-friendly contact query plus exact directed lookup overloads.

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
