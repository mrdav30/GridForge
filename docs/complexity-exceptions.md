# Cyclomatic Complexity Exception Register

This document records methods that intentionally exceed the current
cyclomatic-complexity review threshold.

## Policy

- Review threshold: cyclomatic complexity greater than 10.
- Risk threshold: CRAP score greater than 30 requires immediate test hardening
  or refactoring.
- Current status: the coverage/CRAP report generated on 2026-06-14 has no
  methods above CRAP 30.
- Source report:
  `TestResults/coverage-analysis/raw/0fbb7c73-ed84-4d1f-ad02-b9292a5c6deb/coverage.cobertura.xml`.

Complexity exceptions are acceptable when the method is compiler-generated
iterator machinery, deterministic fixed-order comparison, or hot fixed-shape
spatial traversal where extraction would add indirection, allocations, or extra
passes without lowering real maintenance risk. Revisit these exceptions when
coverage drops, behavior changes, or benchmarks show a different bottleneck.

## Exception Register

| Module | Method | Complexity | Coverage | Rationale | Revisit if |
| --- | --- | ---: | --- | --- | --- |
| `GridForge.Grids.GridWorld` | `TryPrepareConfiguredVoxelMask(bool[,,], GridDimensions, out VoxelIndex[])` | 26 | 100% line | Fixed-order sparse-mask validation, counting, allocation, and deterministic index emission are kept in one pass shape to avoid extra temporary collections. | Sparse mask input rules change, allocation strategy changes, or mask preparation becomes a benchmark hotspot. |
| `GridForge.Grids.GridWorld` | `TryGetClosestGridAndVoxel(Vector3d, out VoxelGrid?, out Voxel?, GridTopologyKind?)` | 26 | 100% line | Single-pass closest physical voxel resolution avoids intermediate candidate lists and preserves deterministic tie-breaking. | Closest-query semantics change or a pooled candidate pipeline benchmarks faster. |
| `GridForge.Grids.Topology.HexPrismTopology` | `NormalizePlanarOffset(int, int)` | 24 | 100% line | Hex axial neighbor normalization is a compact fixed decision table; extracting cases would obscure the topology mapping without reducing runtime branches. | Hex direction semantics change or the mapping table moves to generated/static data. |
| `GridForge.Grids.Topology.VoxelNeighborResolver` | `ResolveContactNeighbors(Voxel, VoxelGrid, SwiftList<Voxel>?, bool, VoxelNeighborScope, Fixed64?)` | 18 | 100% line | Hot neighbor query path combines scope handling, tolerance expansion, pooled scratch rental, and early-exit behavior without allocations. | Neighbor scopes change, profiling shows resolver overhead, or an allocation-free split keeps the same traversal cost. |
| `GridForge.Grids.Storage.SparseVoxelGridStorage` | `AddScanCellsInRange(...)` | 16 | 100% line | Tight scan-cell range traversal over sparse blocks avoids materializing candidate keys. | Sparse scan-cell layout changes or benchmarks support a lower-branch traversal. |
| `GridForge.Utility.GridTracer` | `AddCoveredHexGridVoxels(...)` | 14 | 100% line | Hex coverage walks bounded axial ranges directly and filters by voxel-center coverage without temporary geometry objects. | Hex coverage rules change or a benchmarked lookup table reduces branches without allocations. |
| `GridForge.Grids.ScanCell/<GetConditionalOccupants>d__32` | `MoveNext()` | 14 | 100% line | Compiler-generated iterator state machine for lazy conditional occupant enumeration. | The lazy API is replaced by an allocation-free `Into` API. |
| `GridForge.Grids.Storage.SparseVoxelGridStorage` | `AddVoxelsInIndexRange(VoxelIndex, VoxelIndex, SwiftList<Voxel>, SwiftHashSet<Voxel>)` | 14 | 100% line | Sparse voxel range traversal visits only intersecting sparse blocks and appends through caller-owned redundancy storage. | Sparse block indexing changes or range scans become a measured bottleneck. |
| `GridForge.Grids.ScanCell` | `AddOccupantsWithinRadius2dTo<T>(...)` | 14 | 100% line | Typed scan-cell filtering keeps type, group, radius, and occupant predicates in one pass over occupant buckets. | Typed scan allocation or predicate cost shows up in profiling. |
| `GridForge.Grids.Storage.DenseVoxelGridStorage` | `AddScanCellsInRange(...)` | 14 | 100% line | Dense scan-cell traversal is a direct nested loop with caller-owned redundancy checks and no candidate allocation. | Dense scan-cell layout changes or benchmarks support a flatter traversal. |
| `GridForge.Grids.Topology.VoxelNeighborResolver` | `CollectCandidateGridIds(GridWorld, TopologyVoxelAabb, SwiftList<ushort>, SwiftHashSet<ushort>)` | 12 | 100% line | Candidate collection fuses source-grid and spatial-hash paths into pooled caller-owned buffers. | Spatial hash lookup changes or candidate ordering needs new semantics. |
| `GridForge.Grids.GridWorld` | `TryPrepareConfiguredVoxelIndices(IEnumerable<VoxelIndex>?, GridDimensions, out VoxelIndex[])` | 12 | 100% line | Sparse index preparation validates, sorts, and compacts caller input once before storage initialization. | Sparse setup accepts new index sources or duplicate handling changes. |
| `GridForge.Grids.GridEventInfo` | `.ctor(...)` | 12 | 100% line | Event payload construction keeps all deterministic identity and affected-bound fields explicit. | Event payload shape changes or construction becomes generated. |
| `GridForge.Grids.GridOccupantManager` | `CompareTrackedOccupancies(TrackedOccupancy, TrackedOccupancy)` | 12 | 100% line | Deterministic multi-key ordering avoids comparer allocation and makes snapshot precedence auditable. | Snapshot ordering changes or a source-shared comparer becomes available without runtime cost. |
| `GridForge.Grids.ScanCell` | `AddOccupantsWithinRadius2dTo(...)` | 12 | 100% line | Untyped 2D radius filtering keeps distance, group, and occupant predicates in one pass. | Scan filtering changes or profiling shows predicate overhead. |
| `GridForge.Grids.Storage.SparseVoxelGridStorage` | `TryGetClosestVoxelFromTree(Vector3d, out Voxel?, out Fixed64)` | 12 | 100% line | BVH nearest search uses stack pruning and deterministic tie-breaking without heap allocations. | BVH internals change or closest sparse queries become a measured hotspot. |
| `GridForge.Grids.GridWorld` | `TryGetClosestGrid(Vector3d, out VoxelGrid?, GridTopologyKind?)` | 12 | 100% line | Closest-grid lookup stays single-pass over active grids and relies on deterministic bucket order for equal-distance ties. | Active-grid iteration order changes or topology filtering expands. |

## Review Notes

- CRAP status from the 2026-06-14 run: `FLAGGED_METHODS:0`.
- Generated iterator `MoveNext` methods should be reviewed through the source
  iterator body first; do not rewrite public lazy APIs only to reduce generated
  complexity.
- For deterministic ordering, spatial scans, and fixed-shape predicates, prefer
  benchmark-backed changes over structural edits made only to lower a metric.
- Re-run coverage and CRAP analysis after changes to scan cells, sparse storage,
  closest queries, neighbor resolution, grid registration, or tracing, then
  update this register.
