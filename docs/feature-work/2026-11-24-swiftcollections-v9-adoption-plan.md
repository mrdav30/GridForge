# SwiftCollections v9 Adoption Battle Plan

**Status:** Proposed; source audit complete, measurements and implementation not started.

**Goal:** Decide whether packed ticket storage or native fixed-point 2D indexes
improve real GridForge operations, then implement only the candidates that pass
the behavior, ownership, memory and performance gates below.

**Scope:** GridForge source at `b7f8cde4f9462c5d7a5ac47ab48029cee7688bad`
and sibling SwiftCollections source at
`7c2fa696f10d88194f5a895aaafd7debd8e27574`. This planning change adds no runtime,
tests, benchmarks, configuration or dependency edits and runs no builds or
benchmarks. Gains are hypotheses, not measured results. GridForge already
references the v9.0.0 core and FixedMathSharp companion packages, including Lean;
this is a usage evaluation rather than a package migration.

Use `lsf-development` as the primary workflow. Keep subsequent findings,
decisions and concise phase evidence in this plan. Feature work does not receive
bug-tracker IDs. Completion ends at implementation and verification; publication,
distribution and released-package availability are outside this plan.

## Provider contracts that constrain adoption

The inspected provider implementations and their public contracts are:

| Capability | Actual API and relevant contract |
| --- | --- |
| [SwiftDenseTicketMap<T>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Collection/DenseTicketMap/SwiftDenseTicketMap.cs) | `Add` issues a `SwiftTicket`; `TryGet`, `GetRef`, `Remove`, `GetTicketAt`, `Values`, `EnsureCapacity` and getter-only `State` support owned lifetimes and packed iteration. Removal swaps the last live value into the gap. Generations start at one, default tickets fail and terminal generations retire. `Clear` retains identity history and capacity. |
| [SwiftTicketStore<T>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Collection/TicketStore/SwiftTicketStore.cs) | `TryAdd`/`Add` attach values to an externally issued ticket; `ContainsKey`, `TryGet`, `GetRef`, `Remove`, `GetTicketAt`, `Values`, `EnsureCapacity` and `State` do not consult the issuer. Generation zero is valid key data. Any occupied index rejects another attachment even with a different generation. Removal does not advance generation. |
| [SwiftTicket](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Collection/Support/SwiftTicket.cs) | An `int Index` and `uint Generation`, without world, owner or timeline identity. Numeric foreign tickets can alias. Restoring an old owner state can revive numeric handles from an abandoned future. This is distinct from GridForge's process-wide `long` registration generations. |
| [SwiftFixedBVH2D<TKey>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/BVH/SwiftFixedBVH2D.cs) | Specializes the existing shared BVH over `FixedBoundVolume2D`. `Insert` upserts canonical equal keys, `UpdateEntryBounds` updates present keys, `Remove` removes, and `Query` appends results. Traversal is topology/history-dependent. |
| [SwiftFixedSpatialHash2D<TKey>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/SpatialHash/SwiftFixedSpatialHash2D.cs) | Native XY cells over `FixedBoundVolume2D`; positive fixed cell size, raw signed floor/clamp mapping, `GetCellIndex` and broad `CollectPointCandidates`. Exact `Query` appends results; huge cell ranges can still take impractical work. |
| [SwiftFixedQuadtree<TKey>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/Quadtree/SwiftFixedQuadtree.cs) | Fixed root bounds must wholly contain inserted rectangles. Explicit `SwiftQuadtreeOptions` and positive `minNodeSize`; straddlers remain at the parent, split-plane ties prefer the upper half. Query order depends on mutation history; no persistence contract. |

Use [the provider collection guide](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/docs/api/guides/collections.md)
and [spatial query guide](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/docs/api/guides/spatial-queries.md)
for selection and complete API details. `FixedBoundVolume2D` normalizes endpoints
and uses closed intersection; it is a rectangle broad phase, not a hex, circle,
capsule or swept-coverage oracle. Size/area representability and broad-phase
work bounds still matter. Use fixed wrappers, never Numerics wrappers.

Packed live storage does not eliminate metadata cost. Dense-map slot history
includes issued/free/retired slots. TicketStore sparse lookup storage grows with
the highest attached index plus one, not live membership. Neither collection
promises stable dense positions, addresses, refs, spans or insertion order.
Structural mutation requires reacquiring borrowed storage. Separate stores must
be joined by tickets, never zipped by their dense positions.

## Production ownership audit and decisions

“Adopt after proof” authorizes a bounded future comparison at the named owner,
not an assumption that replacement must ship. “Retain” means the current owner
matches the contract. “Defer” requires the stated new evidence before reopening.

| Candidate and actual callers | Decision and reason | Existing verification to reuse |
| --- | --- | --- |
| [ScanCell](../../src/GridForge/Grids/Nodes/ScanCell.cs) per-voxel `SwiftSlotMap<OccupantEntry>`, [bucket pools](../../src/GridForge/Grids/Support/Pools.cs); registration/removal in [GridOccupantManager](../../src/GridForge/Grids/Managers/GridOccupantManager.cs), exact and radius queries in [GridScanManager](../../src/GridForge/Grids/Managers/GridScanManager.cs) | **Adopt dense storage after proof, conditional.** Churn can leave holes while scans visit live entries. But voxel-specific borrowed queries explicitly enumerate slot order, and tickets carry process-unique long generations. A direct DenseTicketMap substitution violates both assumptions. Preserve public slot order, exact voxel grouping and ticket safety; charge any mapping/ordered-view maintenance to the full operation. If the smallest compatible design loses, retain SlotMap. A new order or ticket contract needs an explicit separately justified decision before implementation; this plan does not authorize it. | [ScanCell.Tests](../../tests/GridForge.Tests/Grids/ScanCell.Tests.cs), [exact voxel queries](../../tests/GridForge.Tests/Grids/GridScanManager.VoxelOccupants.Tests.cs), [scan coverage order](../../tests/GridForge.Tests/Utility/GridScanCoverageOrderTests.cs); `OccupantWaveBenchmarks`, `ScanRadiusMemoryBenchmarks`. |
| [WorldOccupancyRegistry](../../src/GridForge/Grids/Managers/WorldOccupancyRegistry.cs) `Guid -> OccupancyRecord`, [OccupancyRecord](../../src/GridForge/Grids/Managers/OccupancyRecord.cs) `WorldVoxelIndex -> OccupantTicket` | **Retain dictionaries.** The issuer is a host occupant with a Guid, not an integer-ticket owner. Registry lookup additionally requires object reference equality to reject Guid collisions. One occupant may have multiple voxel registrations; ticket slots overlap across buckets. A TicketStore would need a new issuer, indirection and lifecycle machinery without replacing these lookup needs. | [OccupancyRegistryTests](../../tests/GridForge.Tests/Grids/OccupancyRegistryTests.cs), exact voxel queries and occupant waves. |
| Optional parallel registration metadata under `SwiftTicketStore<T>` | **Defer.** No current GridForge `SwiftTicket` issuer/component owner exists. Neither `OccupantTicket.Generation` nor world/grid `long` spawn tokens can be narrowed to uint. Bucket-local slot numbers cannot be combined in a world store. Reopen only for an implemented owner with a real optional payload, shared lifetime boundary and measured lookup-density/memory budget. Do not build an ECS or issue tickets just to use this container. | Reuse occupancy/lifecycle fixtures if a real consumer emerges; provider store scenarios remain provider tests. |
| [GridWorld.ActiveGrids and BoundsTracker](../../src/GridForge/Grids/Managers/GridWorld.cs), [WorldVoxelIndex](../../src/GridForge/Spatial/WorldVoxelIndex.cs), neighbor rows in [VoxelGrid](../../src/GridForge/Grids/VoxelGrid.cs) | **Retain.** `ActiveGrids` is a public SlotMap; grid slots are ushort and identity includes separate world/grid generations. BoundsTracker uses complete normalized configuration equality. Neighbor sets and sorted boundary-contact rows describe topology relationships, not issued object lifetimes. Dense replacement would add remapping and change exposed storage/order contracts. | [GridWorld.Tests](../../tests/GridForge.Tests/Grids/GridWorld.Tests.cs), [mixed topology neighbors](../../tests/GridForge.Tests/Grids/MixedTopologyNeighborTests.cs), [boundary contacts](../../tests/GridForge.Tests/Grids/GridBoundaryContactCursorTests.cs); grid registration, spatial-index and neighbor benchmarks. |
| Dense voxel arrays, [SparseVoxelGridStorage](../../src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs), [SparseVoxelBlock](../../src/GridForge/Grids/Storage/SparseVoxelBlock.cs), scan-cell maps and active-cell sets | **Retain.** Addresses/scan keys are externally derived grid coordinates, not generations. Sparse blocks keep sorted coordinates with binary lookup; physical presence, canonical enumeration and existing pooled arrays matter. Ticket storage would need a coordinate map anyway. Preserve sparse absence and high-address-space behavior; sparse lookup memory already warrants measurement independently of this adoption. | [SparseVoxelGrid.Tests](../../tests/GridForge.Tests/Grids/SparseVoxelGrid.Tests.cs), [storage guards](../../tests/GridForge.Tests/Grids/StorageGuardTests.cs), covered-address tests; sparse-grid, voxel-grid-memory and hex-topology benchmarks. |
| [Voxel.ObstacleTracker](../../src/GridForge/Grids/Nodes/Voxel.cs), [GridObstacleManager](../../src/GridForge/Grids/Managers/GridObstacleManager.cs), [Blocker](../../src/GridForge/Blockers/Blocker.cs) coverage caches | **Retain.** Obstacle tokens represent process-unique attachment ownership and stack distinct blockers. Membership by complete token, world voxel identities and cached coverage are the required operations. A dense ticket does not replace their equality or cached ordering. | [obstacle ownership](../../tests/GridForge.Tests/Grids/GridObstacleManager.Ownership.Tests.cs), [BlockerTests](../../tests/GridForge.Tests/Blockers/BlockerTests.cs); blocker apply/remove and memory benchmarks. |
| [PartitionProvider](../../src/GridForge/Spatial/PartitionProvider.cs), [GridDiagnosticSession](../../src/GridForge/Diagnostics/GridDiagnosticSession.cs), scratch/output lists and committed-change/cursor state | **Retain.** Partitions key by exact Type, optimize one/two entries inline and preserve registration order. Diagnostics coalesces complete change identities then sorts publication. Scratch and change streams have caller-capacity, sequence and lifetime contracts. Tickets do not remove those owners. | [PartitionProvider.Tests](../../tests/GridForge.Tests/Spatial/PartitionProvider.Tests.cs), diagnostic session/contracts and navigation-baseline/cursor tests; partition-provider, diagnostics and navigation benchmarks. |
| Existing top-level [GridSpatialIndex](../../src/GridForge/Grids/Managers/GridSpatialIndex.cs) ordinary fixed 3D spatial hash, oversized fixed 3D BVH and contact-envelope BVH | **Retain general 3D path.** Stacked grids and mixed rectangular/hex volumes need vertical distinction. Existing cell-budget classification, full-volume scan fallback, exact filtering and ascending slot publication solve real large-span workloads. A 2D projection cannot replace 3D overlap/contact proofs or nearest-voxel distances. | [GridSpatialIndexTests](../../tests/GridForge.Tests/Grids/GridSpatialIndexTests.cs), [closest query tests](../../tests/GridForge.Tests/Grids/ClosestQueryTests.cs), boundary-contact tests; `GridSpatialIndex*Benchmarks`, including mixed/large/tuning/footprint cases. |
| Planar point/area/scan candidate discovery through GridWorld, [GridTracer](../../src/GridForge/Utility/GridTracer.cs) and GridScanManager | **Adopt native 2D broad phase after proof, conditional.** Evaluate only at the existing spatial-index owner and only where profiling shows candidate discovery dominates a real planar operation. Compare native hash/BVH for the measured scale/churn; retain exact vertical, topology and presence checks downstream. Any added projected index must earn its registration, update, removal and retained-memory cost across the complete mixed workload. Do not add a second query framework or dimension-dispatch abstraction. | [GridPlane2d.Tests](../../tests/GridForge.Tests/Spatial/GridPlane2d.Tests.cs), scan-cell and coverage-order tests, [GridTracer.Tests](../../tests/GridForge.Tests/Utility/GridTracer.Tests.cs); `Vector2dLookupBenchmarks`, layered radius scans and spatial-index scenarios. |
| [Native planar navigation-body tracing](../../src/GridForge/Utility/GridTracer.PlanarNavigationBody.cs), planar interval/slab/contact geometry, AreaBlocker topology coverage | **Retain exact algorithms.** The native body trace already receives ordered eligible generation/layer selections, then traverses topology addresses with hard existing-capacity ceilings. A generic spatial rectangle index adds redundant selection and can alter work-limit outcomes. The 2D index is no substitute for exact swept/endpoint contact, sparse alternatives, XZ layer policy or hex geometry. | [planar navigation-body tests](../../tests/GridForge.Tests/Utility/GridPlanarNavigationBodyTraceTests.cs) and their Geometry/Boundaries/Lifecycle/Ranges/Validation/WorldEdge companions; planar interval/body-trace, exact-boundary and blocker benchmarks. |
| Fixed quadtree as another top-level backend | **Defer.** Its fixed root/world-bounds ownership does not naturally fit dynamically conjoined/streamed unbounded worlds. Reopen only for a concrete bounded planar consumer whose repeated regional queries beat both existing discovery and native hash/BVH after maintenance cost. Do not add a third selectable backend without that consumer. | Existing spatial-index, sparse and hex fixtures are controls; provider quadtree tests cover its own subdivision contract. |

The contracts above are also described in the current
[scan guide](../api/guides/Scan-Cells-and-Query-Flow.md),
[occupant guide](../api/guides/Occupants-and-Partitions.md),
[determinism guide](../api/guides/Determinism-Snapping-and-Pooling.md) and
[navigation-body guide](../api/guides/Navigation-Body-Coverage.md).

## Non-negotiable compatibility and lifetime boundaries

- Preserve public and serialized type identities, public collection/property
  shapes, complete key equality and existing exception/failure contracts.
  Runtime tickets are not persistent content IDs. Do not persist a dense slot or
  substitute its value for WorldVoxelIndex, Guid, a layer, configuration identity
  or committed sequence. Preserve the existing state surface; this work does not
  introduce a GridWorld serializer.
- Keep OccupantTicket's process-wide positive long generation and default/stale
  behavior, including exhaustion before mutation, same-slot reuse, cross-world
  rejection and pooled reuse. A private SwiftTicket may be used only with its
  owning map and existing world/grid/bucket lifetime checks. Never cast away
  generation bits or treat matching numeric tickets as ownership proof.
- Preserve exact-voxel bucket selection at call time, borrowed slot-order
  enumeration, filter call scope/short-circuit order, inclusive radius filtering,
  per-registration multiplicity and caller-owned output clearing. A scan can
  observe registration into a later initially empty candidate; do not replace
  that with an undocumented frozen snapshot.
- Keep grid occupant monitor -> captured registry monitor ordering. Ordinary
  reset clears records for reuse; deactivation atomically detaches the registry.
  Preserve rollback when tracking rejects a registration, notifications after
  publication and removal independent of the occupant's current Position. Dense
  refs/spans must not survive callbacks, reset, pool release or structural edits.
- Swift XY must receive `(world.X, world.Z)` via
  [GridPlane2d](../../src/GridForge/Spatial/GridPlane2d.cs); its second component
  is not world Y. Retain explicit embedding Y/local resolved layers and full
  grid generations. Coincident XZ bounds from different heights remain separate
  candidates until exact Y/layer admission; never merge identities by projection.
  Rectangular/hex topology and sparse physical absence stay authoritative.
- Sort/canonicalize candidate publication at its existing owner. Provider tree,
  hash and dense orders are deterministic for identical histories, not identical
  to current GridForge order. Preserve ascending grid slots, topology-local scan
  order and navigation proof ordering. Do not silently increase or reinterpret
  hard scratch/output/candidate-work ceilings.
- In [bounded voxel discovery](../../src/GridForge/Grids/Managers/GridWorld.BoundedVoxels.cs),
  every registered grid consumes discovery work before intersection rejection.
  A projected index must not skip those charged visits and turn
  `WorkBudgetExceeded` into `Completed`; preserve the current meter or retain
  that path. Equal unlimited result sets do not establish bounded-query parity.
- Derive indexes from authoritative live state. Rebuild rather than serialize
  private topology. If a future real state owner adopts dense storage, preserve
  dense order, generation/free/retired history, companion-store membership and
  subsequent issuance together; invalidate abandoned-future handles. Test
  failed/malformed restore without replacing the valid owner. Shallow payload
  copies do not create independent world/occupant objects.

## Phase 0 — Production baselines and go/no-go decisions

- [ ] Reconfirm clean baseline/provider revisions, actual loaded dependencies,
  framework/configuration and authoritative methods. Profile executed callers
  before attributing cost to holes or dimensional storage. Read current
  `CONTRIBUTING.md` validation and benchmark commands; existing historical plans
  describe older framework/workflow facts and are not current gates.
- [ ] Reuse the maintained [benchmark project](../../tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj)
  and [scenario factory](../../tests/GridForge.Benchmarks/Support/BenchmarkScenarioFactory.cs).
  Extend the owning benchmark class only for a missing production scenario;
  do not create an adoption-only runner, tests of benchmark machinery or a second
  report. Container microbenchmarks can attribute cost after production evidence,
  but cannot select a replacement by themselves.
- [ ] Measure a complete occupancy host cycle: register agents, move by
  deregister/register, perform exact-ticket reads plus typed/group-filtered 3D
  and layer-locked radius scans, remove a middle registration, unload/reload a
  grid and reset/reuse the world. Use current occupants' actual registration
  APIs and include multi-voxel occupants. Measure isolated operations as well as
  the complete deterministic cycle, including any ordering repairs.
- [ ] Measure planar host cycles with streamed registration/removal, point
  lookup, XZ area/scan coverage and blockers. Include mixed 3D queries in the
  same world so a projected index pays its real maintenance cost. Compare flat
  worlds with coincident XZ bounds on stacked layers, rectangular/hex neighbors,
  dense/sparse presence, negative raw-boundary coordinates, one oversized grid
  and a huge sparse query window. Retain the existing 3D index as the control.
- [ ] Begin with maintained fixture defaults: occupant waves use 8,192 occupants
  and four waves; radius scans use 16,384 occupants or 8,192 on each of two layers;
  planar lookup uses 1,024 positions across four tiled grids. Add bounded sweeps
  at small and large populations, one/two/32/255 occupants per voxel, 0/10/50/90%
  removal churn and 1/10/50/90% live-to-issued density. Record scan-to-mutation
  and ticket-lookup-to-iteration ratios. These are proposed experimental points,
  not observed production distributions; identify the actual host workload before
  generalizing. Do not allocate huge sparse capacities merely to hit a sweep.
- [ ] Record elapsed ns/operation and ms/full cycle, warm bytes/op and cold
  growth allocations, latency dispersion, retained bytes after churn/reset/pool
  reuse, peak live/issued/retired counts, capacities, hole density, candidate
  counts and index maintenance time. For projected indexes record occupied cells,
  query-cell ranges and vertical false positives. Include mapping arrays,
  ordered views, both indexes and pooled retained objects in total memory.
  Record host-cycle p95/p99 from an actual host sample stream when available;
  BenchmarkDotNet iteration percentiles are not host-frame percentiles.
- [ ] Freeze output/order/event oracles before timing. Use balanced baseline/
  candidate run order and at least three independent complete comparisons on an
  idle host. Inspect child errors, missing reports and actual sample/launch counts;
  incomplete runs do not establish a performance result. Record environment,
  input construction/seed, commands, run count and adverse/inconclusive results.

**Decision rules:** first require identical public behavior and no new warm
allocations in currently allocation-free paths. Advance a candidate only if its
complete intended cycle shows a repeatable, useful benefit beyond measurement
uncertainty after all compatibility and lifecycle costs. Evaluate latency and
total retained memory together; a smaller container is not a win if preserving
ordering makes its real caller slower. Establish workload-specific budgets from
the measured baseline before implementation rather than inventing a universal
percentage or frame-rate threshold. A repeatable regression in a representative
unaffected operation or host-cycle tail is a no-go unless the user explicitly
accepts the measured tradeoff. Reject ambiguous results, lost order, aliasing,
unbounded range traversal, extra schema/owner machinery or memory growth without
a bounded budget. A retain/defer outcome is valid completion of the evaluation;
it is not permission to keep experimental runtime branches.
If no candidate passes Phase 0, record the no-adoption result, remove any
experimental runtime changes and skip Phases 1 and 2. No ticket migration,
parallel index or implementation obligation survives that decision. Documentation
and evidence review then close the feature evaluation without running irrelevant
runtime gates for a documentation-only outcome.

Phase 0 result record (fill here): baseline/provider/environment; fixture and
complete-cycle commands; current vs candidate output equivalence; time/allocation/
capacity totals with dispersion; chosen candidate or no-go reason; retained
contracts and any separately accepted tradeoff. Keep these reproducible facts in
the tracked plan, without links to ignored build/benchmark captures.

## Phase 1 — Smallest compatible occupancy slice, if selected

- [ ] Write focused production regressions at ScanCell/GridOccupantManager/
  GridScanManager first. Use A/B/C occupants in one voxel, remove A or B, reuse
  the slot, then compare exact ticket identity and full slot-ordered enumeration
  with the baseline. Repeat in neighboring buckets sharing slot numbers and in
  another world with matching local indices. Verify default/removed tickets
  cannot read or remove replacements; same occupant re-registration is a new
  lifetime.
- [ ] Prototype only the selected compatible representation at ScanCell and
  its existing pool owner. Retain process-generation admission and exact voxel
  grouping. If compatibility demands a persistent parallel slot owner plus dense
  map plus remapping, include all three costs and stop if that defeats the
  measured benefit. Do not retain the old and new machinery as selectable runtime
  paths after the decision.
- [ ] Exercise duplicate/reference-colliding Guid rejection, full voxel capacity,
  failed tracking rollback, throwing query predicates, filter scope/order,
  ticket reads concurrent with reset under existing monitors, pool release,
  world deactivation and reset without deactivation. Count/event/active-cell/
  registry changes must match current behavior, including partial-result failure.
- [ ] Run focused tests and the same complete host-cycle comparison before
  expanding all query variants. Obtain independent source review of ownership,
  order and deletion opportunities; then update all affected typed/untyped and
  2D/3D callers together. Revert the experimental representation on no-go and
  record the reason here. Delete superseded storage/pool helpers only when their
  final production consumer is removed.

## Phase 2 — Smallest planar broad-phase slice, if selected

- [ ] Select one existing planar production operation whose measured candidate
  cost warrants native 2D indexing. Compare projected BVH with native spatial hash
  using representative scale/churn, not a new generic backend hierarchy. Defer
  quadtree unless Phase 0 establishes its bounded-world consumer.
- [ ] Keep ownership in GridSpatialIndex/GridWorld. Specify exactly which
  registration/removal/reset operations maintain the index and which planar
  callers read it. A parallel projected index is acceptable only after charging
  its whole-world cost, including full rebuild, cold growth, all mutations,
  retained storage, exact Y filtering and sorting against the baseline;
  otherwise retain 3D discovery. Do not keep a permanent shadow index for
  comparison after the experiment. Any change to externally observable hard
  candidate-work-limit admission or terminal outcomes is a no-go even when
  complete unlimited queries return the same answers.
  Preserve the existing large-query fallback/cell-budget protections and sorted
  output. Do not project the contact-envelope or nearest-voxel proof into 2D.
- [ ] Prove vertical false-positive rejection with two grids having identical
  XZ footprints at different Y, ordering the ineligible one first. Then remove
  the eligible grid and require the query to miss. Include same-slot stale
  handles with different grid generations after unload/reload, selected layers
  crossing different voxel
  heights, negative floor boundaries, exact edge/corner touch, hex axial ranges,
  sparse holes, oversized bounds and an empty/reused world.
- [ ] Compare complete scalar/Into results and deterministic candidate order,
  exact voxel selection, predicates, multiplicity and failure mutation against
  the current path. Preserve native planar navigation's explicit eligible-layer
  validation, hard capacities, stamps and strict swept/closed endpoint semantics;
  this phase does not replace that trace's traversal.
- [ ] Review the first complete operation and retained-memory accounting before
  expanding. Share only applicable existing GridPlane2d projection/admission
  policy; avoid a forwarding facade, copied geometry or per-caller indexes.
  Remove superseded internals if an exclusive replacement is proven; remove the
  experiment if it is not. Rerun the mixed/full host cycle after expansion.

## Phase 3 — Verification and handoff

- [ ] Run focused real-world regressions before the full matrix: churn/order/
  stale-ticket controls for occupancy, and stacked/hex/sparse/extreme-range
  controls for spatial work. Preserve existing regressions rather than weakening
  them to fit dense or tree order. Repeated runs must produce the same canonical
  identities, ordered query results, observable events and continuation after
  reset; runtime process tokens are compared for their safety relations, not
  assumed identical numeric values across processes.
- [ ] Verify state/restore where an actual affected owner supports it: JSON and
  MemoryPack in Standard, JSON/state in Lean, invalid state/failure atomicity,
  subsequent reuse and stale abandoned-future handles. GridForge world/pool
  reconstruction must rebuild derived indexes and preserve public contracts.
  Provider-only state tests do not establish consumer lifecycle safety. Do not
  invent a world snapshot format merely to satisfy this checklist.
- [ ] Build Standard `Release` and `ReleaseLean` for `netstandard2.1` and
  `net10.0`; run the net10.0 GridForge tests in both configurations and the
  repository's Windows/Linux CI matrix. Collect fresh coverage using
  `tests/GridForge.Tests/coverlet.runsettings`, then run
  `.assets/scripts/assert-full-coverage.ps1` per configuration. Require 100%
  reported line, branch and executable-method coverage with raw uncovered-count
  inspection; remove proved unreachable branches, never add exclusions.
- [ ] Use existing `UseLocalLsfStack=true` for coordinated source validation;
  propagate it through restore/build/test and benchmark child builds. Carry
  `-p:DisableTransitiveProjectReferences=true` on every source-mode command and
  serialize shared-output builds/coverage/timings. Record exact provider and
  consumer revisions and any local source patches. Existing released-package
  validation remains distinct from source validation; do not rewrite references
  for this experiment.
- [ ] Provider changes are not presumed necessary. If a missing reusable
  contract or defect is proven, fix it in SwiftCollections first with its own
  behavior regression and Standard/Lean core+companion 100% line/branch/
  executable-method gates, then validate GridForge on that source graph. Remind
  the owner to update actual CI build/test dependency pins after upstream changes
  land. Pin updates or package releases do not keep verified feature work open.
- [ ] Reuse the same maintained benchmark inputs and run balances for final
  comparison; inspect complete results, adverse cases, warm allocations,
  retained-memory high water and full-cycle latency. A lookup win alone cannot
  close a scanning or host-cycle claim. Resolve introduced regressions in this
  change unless the user explicitly accepts the measured tradeoff.
- [ ] Update the affected conceptual guides/API comments for actual adopted
  behavior, then this plan and the feature-work overview. Review final connected
  owners independently, including net runtime growth, duplicate paths, remaining
  production callers and deletions. Leave edits unstaged/uncommitted. Archive
  this plan under `done/` by repository convention when evaluation and all
  selected implementation/verification work are complete.

Completion evidence belongs in this plan: selected/no-go decisions; exact source
graph and input recipes; build/test/coverage counts; focused counterexample
outcomes; deterministic continuation/state results where applicable; complete
performance/memory comparisons; removed machinery; remaining explicit deferrals.
No release gate or separate phase report is required.
