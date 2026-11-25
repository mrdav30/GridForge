# SwiftCollections v9 Adoption Battle Plan

**Status:** Completed Phase 0 on 2026-11-24 and the separately authorized
TicketStore and cached-position/provider follow-ups on 2026-11-25. Retain
SlotMap occupancy and existing 3D discovery. No GridForge adoption is selected;
the isolated DenseTicketMap lookup improvement warrants separate provider work.

**Goal:** Decide whether packed ticket storage or native fixed-point 2D indexes
improve real GridForge operations, then implement only the candidates that pass
the behavior, ownership, memory and performance gates below.

**Scope:** GridForge source at `b7f8cde4f9462c5d7a5ac47ab48029cee7688bad` and
sibling SwiftCollections source at `7c2fa696f10d88194f5a895aaafd7debd8e27574`.
The original planning audit added no runtime, tests, benchmarks, configuration
or dependency edits and ran no builds or benchmarks. Phase 0 subsequently
measured two bounded production prototypes and removed both after no-go results;
only benchmark baselines and this completion record remain. GridForge already
references the v9.0.0 core and FixedMathSharp companion packages, including
Lean; this is a usage evaluation rather than a package migration. The execution
baseline is the planning commit `59c715277be368b4dc8970755b8470a2270154bc`; the
preceding runtime source revision above remains unchanged.

Use `lsf-development` as the primary workflow. Keep subsequent findings,
decisions and concise phase evidence in this plan. Feature work does not receive
bug-tracker IDs. Completion ends at implementation and verification;
publication, distribution and released-package availability are outside this
plan.

## Provider contracts that constrain adoption

The inspected provider implementations and their public contracts are:

| Capability                                                                                                                                                                | Actual API and relevant contract                                                                                                                                                                                                                                                                                                                           |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [SwiftDenseTicketMap<T>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Collection/DenseTicketMap/SwiftDenseTicketMap.cs)                   | `Add` issues a `SwiftTicket`; `TryGet`, `GetRef`, `Remove`, `GetTicketAt`, `Values`, `EnsureCapacity` and getter-only `State` support owned lifetimes and packed iteration. Removal swaps the last live value into the gap. Generations start at one, default tickets fail and terminal generations retire. `Clear` retains identity history and capacity. |
| [SwiftTicketStore<T>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Collection/TicketStore/SwiftTicketStore.cs)                            | `TryAdd`/`Add` attach values to an externally issued ticket; `ContainsKey`, `TryGet`, `GetRef`, `Remove`, `GetTicketAt`, `Values`, `EnsureCapacity` and `State` do not consult the issuer. Generation zero is valid key data. Any occupied index rejects another attachment even with a different generation. Removal does not advance generation.         |
| [SwiftTicket](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Collection/Support/SwiftTicket.cs)                                             | An `int Index` and `uint Generation`, without world, owner or timeline identity. Numeric foreign tickets can alias. Restoring an old owner state can revive numeric handles from an abandoned future. This is distinct from GridForge's process-wide `long` registration generations.                                                                      |
| [SwiftFixedBVH2D<TKey>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/BVH/SwiftFixedBVH2D.cs)                         | Specializes the existing shared BVH over `FixedBoundVolume2D`. `Insert` upserts canonical equal keys, `UpdateEntryBounds` updates present keys, `Remove` removes, and `Query` appends results. Traversal is topology/history-dependent.                                                                                                                    |
| [SwiftFixedSpatialHash2D<TKey>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/SpatialHash/SwiftFixedSpatialHash2D.cs) | Native XY cells over `FixedBoundVolume2D`; positive fixed cell size, raw signed floor/clamp mapping, `GetCellIndex` and broad `CollectPointCandidates`. Exact `Query` appends results; huge cell ranges can still take impractical work.                                                                                                                   |
| [SwiftFixedQuadtree<TKey>](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/Quadtree/SwiftFixedQuadtree.cs)              | Fixed root bounds must wholly contain inserted rectangles. Explicit `SwiftQuadtreeOptions` and positive `minNodeSize`; straddlers remain at the parent, split-plane ties prefer the upper half. Query order depends on mutation history; no persistence contract.                                                                                          |

Use
[the provider collection guide](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/docs/api/guides/collections.md)
and
[spatial query guide](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/docs/api/guides/spatial-queries.md)
for selection and complete API details. `FixedBoundVolume2D` normalizes
endpoints and uses closed intersection; it is a rectangle broad phase, not a
hex, circle, capsule or swept-coverage oracle. Size/area representability and
broad-phase work bounds still matter. Use fixed wrappers, never Numerics
wrappers.

Packed live storage does not eliminate metadata cost. Dense-map slot history
includes issued/free/retired slots. TicketStore sparse lookup storage grows with
the highest attached index plus one, not live membership. Neither collection
promises stable dense positions, addresses, refs, spans or insertion order.
Structural mutation requires reacquiring borrowed storage. Separate stores must
be joined by tickets, never zipped by their dense positions.

## Production ownership audit and decisions

The completed Phase 0 decisions below retain existing production owners. The
original “adopt after proof” candidates were evaluated and rejected; reopening
requires the specific new evidence recorded in the completion section. “Retain”
means the current owner matches the contract. “Defer” requires the stated new
evidence before reopening.

| Candidate and actual callers                                                                                                                                                                                                                                                                                                                                                            | Decision and reason                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             | Existing verification to reuse                                                                                                                                                                                                                                                                                                      |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [ScanCell](../../../src/GridForge/Grids/Nodes/ScanCell.cs) per-voxel `SwiftSlotMap<OccupantEntry>`, [bucket pools](../../../src/GridForge/Grids/Support/Pools.cs); registration/removal in [GridOccupantManager](../../../src/GridForge/Grids/Managers/GridOccupantManager.cs), exact and radius queries in [GridScanManager](../../../src/GridForge/Grids/Managers/GridScanManager.cs) | **Retain SlotMap after scoped comparisons.** The original DenseTicketMap prototype regressed all four measured occupancy cases and doubled backing-array payload. The separately measured TicketStore follow-up below did not establish a churned-query win and failed the representative cost gates. Original hypothesis: Churn can leave holes while scans visit live entries. But voxel-specific borrowed queries explicitly enumerate slot order, and tickets carry process-unique long generations. A direct DenseTicketMap substitution violates both assumptions. Preserve public slot order, exact voxel grouping and ticket safety; charge any mapping/ordered-view maintenance to the full operation. If the smallest compatible design loses, retain SlotMap. A new order or ticket contract needs an explicit separately justified decision before implementation; this plan does not authorize it. | [ScanCell.Tests](../../../tests/GridForge.Tests/Grids/ScanCell.Tests.cs), [exact voxel queries](../../../tests/GridForge.Tests/Grids/GridScanManager.VoxelOccupants.Tests.cs), [scan coverage order](../../../tests/GridForge.Tests/Utility/GridScanCoverageOrderTests.cs); `OccupantWaveBenchmarks`, `ScanRadiusMemoryBenchmarks`. |
| [WorldOccupancyRegistry](../../../src/GridForge/Grids/Managers/WorldOccupancyRegistry.cs) `Guid -> OccupancyRecord`, [OccupancyRecord](../../../src/GridForge/Grids/Managers/OccupancyRecord.cs) `WorldVoxelIndex -> OccupantTicket`                                                                                                                                                    | **Retain dictionaries.** The issuer is a host occupant with a Guid, not an integer-ticket owner. Registry lookup additionally requires object reference equality to reject Guid collisions. One occupant may have multiple voxel registrations; ticket slots overlap across buckets. A TicketStore would need a new issuer, indirection and lifecycle machinery without replacing these lookup needs.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | [OccupancyRegistryTests](../../../tests/GridForge.Tests/Grids/OccupancyRegistryTests.cs), exact voxel queries and occupant waves.                                                                                                                                                                                                   |
| Optional parallel registration metadata under `SwiftTicketStore<T>`                                                                                                                                                                                                                                                                                                                     | **Defer.** No current GridForge `SwiftTicket` issuer/component owner exists. Neither `OccupantTicket.Generation` nor world/grid `long` spawn tokens can be narrowed to uint. Bucket-local slot numbers cannot be combined in a world store. Reopen only for an implemented owner with a real optional payload, shared lifetime boundary and measured lookup-density/memory budget. Do not build an ECS or issue tickets just to use this container.                                                                                                                                                                                                                                                                                                                                                                                                                                                             | Reuse occupancy/lifecycle fixtures if a real consumer emerges; provider store scenarios remain provider tests.                                                                                                                                                                                                                      |
| [GridWorld.ActiveGrids and BoundsTracker](../../../src/GridForge/Grids/Managers/GridWorld.cs), [WorldVoxelIndex](../../../src/GridForge/Spatial/WorldVoxelIndex.cs), neighbor rows in [VoxelGrid](../../../src/GridForge/Grids/VoxelGrid.cs)                                                                                                                                            | **Retain.** `ActiveGrids` is a public SlotMap; grid slots are ushort and identity includes separate world/grid generations. BoundsTracker uses complete normalized configuration equality. Neighbor sets and sorted boundary-contact rows describe topology relationships, not issued object lifetimes. Dense replacement would add remapping and change exposed storage/order contracts.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | [GridWorld.Tests](../../../tests/GridForge.Tests/Grids/GridWorld.Tests.cs), [mixed topology neighbors](../../../tests/GridForge.Tests/Grids/MixedTopologyNeighborTests.cs), [boundary contacts](../../../tests/GridForge.Tests/Grids/GridBoundaryContactCursorTests.cs); grid registration, spatial-index and neighbor benchmarks.  |
| Dense voxel arrays, [SparseVoxelGridStorage](../../../src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs), [SparseVoxelBlock](../../../src/GridForge/Grids/Storage/SparseVoxelBlock.cs), scan-cell maps and active-cell sets                                                                                                                                                         | **Retain.** Addresses/scan keys are externally derived grid coordinates, not generations. Sparse blocks keep sorted coordinates with binary lookup; physical presence, canonical enumeration and existing pooled arrays matter. Ticket storage would need a coordinate map anyway. Preserve sparse absence and high-address-space behavior; sparse lookup memory already warrants measurement independently of this adoption.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | [SparseVoxelGrid.Tests](../../../tests/GridForge.Tests/Grids/SparseVoxelGrid.Tests.cs), [storage guards](../../../tests/GridForge.Tests/Grids/StorageGuardTests.cs), covered-address tests; sparse-grid, voxel-grid-memory and hex-topology benchmarks.                                                                             |
| [Voxel.ObstacleTracker](../../../src/GridForge/Grids/Nodes/Voxel.cs), [GridObstacleManager](../../../src/GridForge/Grids/Managers/GridObstacleManager.cs), [Blocker](../../../src/GridForge/Blockers/Blocker.cs) coverage caches                                                                                                                                                        | **Retain.** Obstacle tokens represent process-unique attachment ownership and stack distinct blockers. Membership by complete token, world voxel identities and cached coverage are the required operations. A dense ticket does not replace their equality or cached ordering.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | [obstacle ownership](../../../tests/GridForge.Tests/Grids/GridObstacleManager.Ownership.Tests.cs), [BlockerTests](../../../tests/GridForge.Tests/Blockers/BlockerTests.cs); blocker apply/remove and memory benchmarks.                                                                                                             |
| [PartitionProvider](../../../src/GridForge/Spatial/PartitionProvider.cs), [GridDiagnosticSession](../../../src/GridForge/Diagnostics/GridDiagnosticSession.cs), scratch/output lists and committed-change/cursor state                                                                                                                                                                  | **Retain.** Partitions key by exact Type, optimize one/two entries inline and preserve registration order. Diagnostics coalesces complete change identities then sorts publication. Scratch and change streams have caller-capacity, sequence and lifetime contracts. Tickets do not remove those owners.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | [PartitionProvider.Tests](../../../tests/GridForge.Tests/Spatial/PartitionProvider.Tests.cs), diagnostic session/contracts and navigation-baseline/cursor tests; partition-provider, diagnostics and navigation benchmarks.                                                                                                         |
| Existing top-level [GridSpatialIndex](../../../src/GridForge/Grids/Managers/GridSpatialIndex.cs) ordinary fixed 3D spatial hash, oversized fixed 3D BVH and contact-envelope BVH                                                                                                                                                                                                        | **Retain general 3D path.** Stacked grids and mixed rectangular/hex volumes need vertical distinction. Existing cell-budget classification, full-volume scan fallback, exact filtering and ascending slot publication solve real large-span workloads. A 2D projection cannot replace 3D overlap/contact proofs or nearest-voxel distances.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     | [GridSpatialIndexTests](../../../tests/GridForge.Tests/Grids/GridSpatialIndexTests.cs), [closest query tests](../../../tests/GridForge.Tests/Grids/ClosestQueryTests.cs), boundary-contact tests; `GridSpatialIndex*Benchmarks`, including mixed/large/tuning/footprint cases.                                                      |
| Planar point/area/scan candidate discovery through GridWorld, [GridTracer](../../../src/GridForge/Utility/GridTracer.cs) and GridScanManager                                                                                                                                                                                                                                            | **Defer projected indexing after Phase 0 no-go.** A small flat-area gain did not offset stacked-query and reload regressions. Original hypothesis: Evaluate only at the existing spatial-index owner and only where profiling shows candidate discovery dominates a real planar operation. Compare native hash/BVH for the measured scale/churn; retain exact vertical, topology and presence checks downstream. Any added projected index must earn its registration, update, removal and retained-memory cost across the complete mixed workload. Do not add a second query framework or dimension-dispatch abstraction.                                                                                                                                                                                                                                                                                      | [GridPlane2d.Tests](../../../tests/GridForge.Tests/Spatial/GridPlane2d.Tests.cs), scan-cell and coverage-order tests, [GridTracer.Tests](../../../tests/GridForge.Tests/Utility/GridTracer.Tests.cs); `Vector2dLookupBenchmarks`, layered radius scans and spatial-index scenarios.                                                 |
| [Native planar navigation-body tracing](../../../src/GridForge/Utility/GridTracer.PlanarNavigationBody.cs), planar interval/slab/contact geometry, AreaBlocker topology coverage                                                                                                                                                                                                        | **Retain exact algorithms.** The native body trace already receives ordered eligible generation/layer selections, then traverses topology addresses with hard existing-capacity ceilings. A generic spatial rectangle index adds redundant selection and can alter work-limit outcomes. The 2D index is no substitute for exact swept/endpoint contact, sparse alternatives, XZ layer policy or hex geometry.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | [planar navigation-body tests](../../../tests/GridForge.Tests/Utility/GridPlanarNavigationBodyTraceTests.cs) and their Geometry/Boundaries/Lifecycle/Ranges/Validation/WorldEdge companions; planar interval/body-trace, exact-boundary and blocker benchmarks.                                                                     |
| Fixed quadtree as another top-level backend                                                                                                                                                                                                                                                                                                                                             | **Defer.** Its fixed root/world-bounds ownership does not naturally fit dynamically conjoined/streamed unbounded worlds. Reopen only for a concrete bounded planar consumer whose repeated regional queries beat both existing discovery and native hash/BVH after maintenance cost. Do not add a third selectable backend without that consumer.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | Existing spatial-index, sparse and hex fixtures are controls; provider quadtree tests cover its own subdivision contract.                                                                                                                                                                                                           |

The contracts above are also described in the current
[scan guide](../../api/guides/Scan-Cells-and-Query-Flow.md),
[occupant guide](../../api/guides/Occupants-and-Partitions.md),
[determinism guide](../../api/guides/Determinism-Snapping-and-Pooling.md) and
[navigation-body guide](../../api/guides/Navigation-Body-Coverage.md).

## Non-negotiable compatibility and lifetime boundaries

- Preserve public and serialized type identities, public collection/property
  shapes, complete key equality and existing exception/failure contracts.
  Runtime tickets are not persistent content IDs. Do not persist a dense slot or
  substitute its value for WorldVoxelIndex, Guid, a layer, configuration
  identity or committed sequence. Preserve the existing state surface; this work
  does not introduce a GridWorld serializer.
- Keep OccupantTicket's process-wide positive long generation and default/stale
  behavior, including exhaustion before mutation, same-slot reuse, cross-world
  rejection and pooled reuse. A private SwiftTicket may be used only with its
  owning map and existing world/grid/bucket lifetime checks. Never cast away
  generation bits or treat matching numeric tickets as ownership proof.
- Preserve exact-voxel bucket selection at call time, borrowed slot-order
  enumeration, filter call scope/short-circuit order, inclusive radius
  filtering, per-registration multiplicity and caller-owned output clearing. A
  scan can observe registration into a later initially empty candidate; do not
  replace that with an undocumented frozen snapshot.
- Keep grid occupant monitor -> captured registry monitor ordering. Ordinary
  reset clears records for reuse; deactivation atomically detaches the registry.
  Preserve rollback when tracking rejects a registration, notifications after
  publication and removal independent of the occupant's current Position. Dense
  refs/spans must not survive callbacks, reset, pool release or structural
  edits.
- Swift XY must receive `(world.X, world.Z)` via
  [GridPlane2d](../../../src/GridForge/Spatial/GridPlane2d.cs); its second
  component is not world Y. Retain explicit embedding Y/local resolved layers
  and full grid generations. Coincident XZ bounds from different heights remain
  separate candidates until exact Y/layer admission; never merge identities by
  projection. Rectangular/hex topology and sparse physical absence stay
  authoritative.
- Sort/canonicalize candidate publication at its existing owner. Provider tree,
  hash and dense orders are deterministic for identical histories, not identical
  to current GridForge order. Preserve ascending grid slots, topology-local scan
  order and navigation proof ordering. Do not silently increase or reinterpret
  hard scratch/output/candidate-work ceilings.
- In
  [bounded voxel discovery](../../../src/GridForge/Grids/Managers/GridWorld.BoundedVoxels.cs),
  every registered grid consumes discovery work before intersection rejection. A
  projected index must not skip those charged visits and turn
  `WorkBudgetExceeded` into `Completed`; preserve the current meter or retain
  that path. Equal unlimited result sets do not establish bounded-query parity.
- Derive indexes from authoritative live state. Rebuild rather than serialize
  private topology. If a future real state owner adopts dense storage, preserve
  dense order, generation/free/retired history, companion-store membership and
  subsequent issuance together; invalidate abandoned-future handles. Test
  failed/malformed restore without replacing the valid owner. Shallow payload
  copies do not create independent world/occupant objects.

## Phase 0 — Production baselines, bounded feasibility and go/no-go decisions

Start with an inexpensive opportunity check for each family, then select at most
one bounded production prototype at a time. Phase 0 explicitly permits that
first compatible prototype at the existing owner, after profiling and a frozen
behavior oracle. Measure it against the baseline before making an adoption
decision. Phases 1/2 expand and complete a selected working slice; they are not
prerequisites for the evidence required to exit Phase 0. A failed opportunity
check can defer a family without constructing a candidate. Do not run the full
sweep merely to justify using a new container or index. Any runtime prototype
first needs focused production contract checks and independent source review
before expanding.

- [x] Reconfirm clean baseline/provider revisions, actual loaded dependencies,
      framework/configuration and authoritative methods. Profile executed
      callers before attributing cost to holes or dimensional storage. Read
      current `CONTRIBUTING.md` validation and benchmark commands; existing
      historical plans describe older framework/workflow facts and are not
      current gates.
- [x] Reuse the maintained
      [benchmark project](../../../tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj)
      and
      [scenario factory](../../../tests/GridForge.Benchmarks/Support/BenchmarkScenarioFactory.cs).
      Extend the owning benchmark class only for a missing production scenario;
      do not create an adoption-only runner, tests of benchmark machinery or a
      second report. Container microbenchmarks can attribute cost after
      production evidence, but cannot select a replacement by themselves.
- [x] First measure actual live/peak occupancy and retained holes per voxel in
      the executed workload. The existing occupant wave uses distinct
      coordinates and empties every bucket, so it is a pooling/registration
      control rather than evidence of costly persistent holes. Begin with one
      multi-occupant churn plus ordered-scan pilot, including a full-bucket
      control and exact-ticket reads. Qualify a dense prototype only when bucket
      traversal is a meaningful measured cost after normal registration/scan
      overhead. Charge slot-order repair, public-long/private-uint ticket
      mapping, private generation retirement and pool-clear history to the
      candidate. Otherwise retain SlotMap for this workload.
- [ ] **Not reached after early rejection:** a complete occupancy host cycle
      would additionally register agents, move by deregister/register, perform
      exact-ticket reads plus typed/group-filtered 3D and layer-locked radius
      scans, remove a middle registration, unload/reload a grid and reset/reuse
      the world. Use current occupants' actual registration APIs and include
      multi-voxel occupants. Measure isolated operations as well as the complete
      deterministic cycle, including any ordering repairs.
- [x] Apply an early spatial rejection check: current planar overloads forward
      into the existing 3D path, and the tested flat point recipes occupy one
      vertical hash slice. Profile discovery before adding another index. Start
      with bounded registered-grid counts (4 and 64 horizontal footprints) and
      coincident XZ stacks (1 and 8 layers), retaining exact Y admission and
      sorted slots. Reuse existing spatial-index scenarios; a four-grid lookup
      alone cannot select a world-level backend. Record projected vertical false
      positives and all index maintenance costs. If little discovery work can be
      removed, defer projected indexing without building a parallel owner.
- [ ] **Partial; expansion not reached after early rejection:** measured mixed
      planar/3D area and reload cycles; broader planar host cycles with streamed
      registration/removal, point lookup, XZ area/scan coverage and blockers.
      Include mixed 3D queries in the same world so a projected index pays its
      real maintenance cost. Compare flat worlds with coincident XZ bounds on
      stacked layers, rectangular/hex neighbors, dense/sparse presence, negative
      raw-boundary coordinates, one oversized grid and a huge sparse query
      window. Retain the existing 3D index as the control.
- [ ] **Bounded fixtures executed; broad sweeps not reached after rejection.**
      Original proposed matrix: occupant waves use 8,192 occupants and four
      waves; radius scans use 16,384 occupants or 8,192 on each of two layers;
      planar lookup uses 1,024 positions across four tiled grids. Only after an
      opportunity check and one working compatible pilot, add bounded sweeps at
      small and large populations, one/two/32/255 occupants per voxel,
      0/10/50/90% removal churn and 1/10/50/90% live-to-issued density. Record
      scan-to-mutation and ticket-lookup-to-iteration ratios. These are proposed
      experimental points, not observed production distributions; identify the
      actual host workload before generalizing. Do not allocate huge sparse
      capacities merely to hit a sweep.
- [ ] **Screen metrics recorded; complete heap/tail gates not reached.** The
      adoption requirement was to record elapsed ns/operation and ms/full cycle,
      warm bytes/op and cold growth allocations, latency dispersion, retained
      bytes after churn/reset/pool reuse, peak live/issued/retired counts,
      capacities, hole density, candidate counts and index maintenance time. For
      projected indexes record occupied cells, query-cell ranges and vertical
      false positives. Include mapping arrays, ordered views, both indexes and
      pooled retained objects in total memory. Record host-cycle p95/p99 from an
      actual host sample stream when available; BenchmarkDotNet iteration
      percentiles are not host-frame percentiles.
- [x] Freeze output/order/event oracles before timing. Use balanced baseline/
      candidate run order and at least three independent complete comparisons on
      an idle host. Inspect child errors, missing reports and actual
      sample/launch counts; incomplete runs do not establish a performance
      result. Record environment, input construction/seed, commands, run count
      and adverse/inconclusive results.

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
it is not permission to keep experimental runtime branches. If no opportunity
warrants a prototype, or no prototype passes Phase 0, record the bounded
no-adoption/defer result and its reopening evidence, remove any experimental
runtime changes and skip Phases 1 and 2. No ticket migration, parallel index or
implementation obligation survives that decision. Documentation and evidence
review then close the feature evaluation without running irrelevant runtime
gates for a documentation-only outcome.

### Initial Phase 0 screen — 2026-11-24

Historical screen before the completion experiments below. Its open/next-step
statements are superseded by the final retain/defer decisions.

**Disposition:** opportunity screening completed; Phase 0 remains open. No dense
map or projected index has been admitted to production, and no adoption gain is
claimed. Occupancy is the next bounded feasibility question because the executed
synthetic workload exposes retained-hole cost. The tested point-lookup recipes
do not justify a parallel projected index. Broader area-query selection and a
compatible occupancy candidate still require evidence before any adoption gate.

The test-author and spatial-analysis subagents prepared disjoint benchmark and
disposable inspection work. The coordinator ran all shared-output commands
serially; an independent `ponytail-review` subagent checked the benchmark slice
and its evidence limits, then verified the final record against raw reports with
no blocking findings. Runtime growth is zero: no changes under `src`, no new
runtime fields, APIs, helpers, dependencies or indexes. The existing occupant
benchmark gained four cases in its own file; no runner, test of benchmark
machinery, or separate phase report was added to the repository.

#### Revisions and execution environment

Execution baseline is `59c715277be368b4dc8970755b8470a2270154bc`, with only the
benchmark additions and plan/overview edits described here. Sibling checkouts
were clean and unchanged:

- Chronicler `0281469ecc40e65a05867f473bc10f030e7f69f8`.
- FixedMathSharp `8fdf889322f1bb82501c2afec63d6bb2f304ace2`.
- SwiftCollections `7c2fa696f10d88194f5a895aaafd7debd8e27574`.

Windows x64, Intel Core i7-9700K; SDK 10.0.302, .NET 10.0.10, BenchmarkDotNet
0.15.8. Timings used Standard Release and `DOTNET_TieredCompilation=0`,
restoring the previous environment afterward. All source restore/build/test
commands used
`-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`. The
loaded GridForge, FixedMathSharp and SwiftCollections assemblies were checked as
local-source version `0.0.0.0`, including the fixed-point companion. The final
Standard runtime DLL SHA-256 is
`8417fb5129654ab25b5c3d7c774f99a83810c6f0998b46baf6f8af0f848dbd74`.

#### Occupancy: retained-hole baseline

[OccupantWaveBenchmarks](../../../tests/GridForge.Benchmarks/Memory/OccupantWaveBenchmarks.cs)
now includes three query controls and one mutation/query case. All use a dense
16×1×16 world grid with scan-cell size 8, 256 voxel buckets, and eight query
rounds. Every round performs exact-ticket reads, exact voxel enumeration, a
caller-owned 3D radius scan and a layer-zero planar radius scan centered at
`(7, 0, 7)` with radius 32. Each bucket contains distinct occupant references at
its voxel position. Query results include all survivors.

The compact control registers four occupants per bucket. The churned control
registers 32 and removes the first 28, leaving the last four public slots live.
The full control keeps all 32. The mutation case refills and removes the 7,168
removed registrations before the same eight query rounds. Each setup clears
pools first, then warms all query paths. This is necessary to establish actual
high-water controls: releasing an already-empty SlotMap calls its empty `Clear`
fast path, which can retain prior peak/free history.

Public ticket slots sorted during setup provide the independent exact-voxel
order oracle. Radius setup independently checks complete survivor membership and
absence of duplicates, then captures order as a per-scenario repeatability
check. It is **not** the frozen cross-implementation order oracle needed for a
future candidate; that comparison remains required. All timed query controls
include equal reference/count validation work for equal live populations.

A disposable inspection of the actual live buckets (reflection outside timing)
verified the following aggregate state and warm current-thread allocation over
16 repeated cycles:

| Control                   | Live / peak slots | Backing capacity | Holes | Consumed hits/cycle | Warm bytes/cycle |
| ------------------------- | ----------------- | ---------------- | ----- | ------------------- | ---------------- |
| Compact, 4 live / 4 peak  | 1,024 / 1,024     | 2,048            | 0     | 32,768              | 245,760          |
| Churned, 4 live / 32 peak | 1,024 / 8,192     | 8,192            | 7,168 | 32,768              | 245,760          |
| Full, 32 live / 32 peak   | 8,192 / 8,192     | 8,192            | 0     | 262,144             | 245,760          |

Inspection recipe: after the named public benchmark setup, obtain the existing
`_grid` fixture field, visit its scan cells once, inspect each scan cell's
`_voxelOccupants.Values`, and sum each SlotMap's public `Count`, `PeakCount` and
`Capacity`. Run the named query once more before measuring 16 calls with
`GC.GetAllocatedBytesForCurrentThread`. None of this reflection is maintained
benchmark logic. Capacity counts are slots, not measured retained heap bytes;
fixture-held removed occupants, registry capacity and pools still need full
memory accounting for a candidate.

Three independent benchmark processes ran ten warmups and fifteen measured
iterations per case, one invocation per iteration. Each completed all four cases
and exited zero: 180 actual measured iterations total, with 58/57/56 retained
after BenchmarkDotNet outlier removal.

| Validation-inclusive phase, ms/cycle          | Run 1  | Run 2 | Run 3 |
| --------------------------------------------- | ------ | ----- | ----- |
| Query compact buckets                         | 1.197  | 1.123 | 1.154 |
| Query churned buckets                         | 1.428  | 1.367 | 1.346 |
| Query full buckets                            | 12.050 | 8.493 | 8.953 |
| Refill/remove 7,168 registrations, then query | 7.201  | 7.119 | 7.194 |

The equal-survivor query controls differ by 0.192–0.244 ms in these runs. That
is an opportunity signal, not an achievable DenseTicketMap saving: ordering and
public/private ticket translation have not been implemented or measured. The
full control has eight times the live query work and materially wider variation;
it is not an equal-work comparison. These runs use a fixed case order and short
iterations, so they do not satisfy balanced candidate acceptance. Single-
invocation BDN allocations were approximately 240–243.23 KiB for query controls
and 6,178.95–6,179.23 KiB for churn/query; the separate warm counter above found
equal 245,760-byte query allocations. Do not attribute the small BDN accounting
differences to a container benefit.

A separate sampled-thread-time trace of the executed churned query used
`dotnet-trace` 10.0.750501, three warmups, twenty measured iterations and 256
invocations per iteration. Under `ExecuteBucketQueries`, approximately 60% of
sampled elapsed weight was in exact-ticket lookup/Monitor entry, 25% in planar
radius collection and 13% in 3D radius collection. These are inclusive sampled
frames, not proof of lock contention or time solely spent checking empty slots;
inlining and validation remain part of the workload. A dense map cannot claim
those complete frame costs as removable. Query-root filtering excludes setup,
cleanup and report export.

The traced BDN process paused during report export on an inherited-diagnostics
`dotnet --version` child. Attaching a second collector did not release it; the
coordinator verified and stopped only that version child, after which BDN and
trace conversion completed. The trace is attribution evidence only, excluded
from acceptance timings. Future profiling should attach to an already-running
workload rather than propagate launch-time diagnostic suspension to
subprocesses. No benchmark infrastructure was changed to work around this tool
interaction.

#### Spatial: point-candidate screen

A disposable probe compared the actual GridWorld 3D index (reflection outside
timing) with provider-native `SwiftFixedSpatialHash2D<ushort>` projection. Both
used cell edge 50. Register sparse grid bounds from
`(column×100, layer×100, row×100)` to that point plus `(1,0,1)`, with 2×2 or 8×8
horizontal footprints and one or eight layers. These count-only worlds need no
materialized voxels. Query `(0.5,0,0.5)`; project X/Z, then apply exact XYZ
bounds and sort slots. All entries remained in the ordinary hash tier, all exact
results matched, and public planar grid lookup agreed:

| Horizontal footprints | Layers | Registered grids | Current 3D candidates | Projected candidates | Exact matches |
| --------------------- | ------ | ---------------- | --------------------- | -------------------- | ------------- |
| 4                     | 1      | 4                | 1                     | 1                    | 1             |
| 4                     | 8      | 32               | 1                     | 8                    | 1             |
| 64                    | 1      | 64               | 1                     | 1                    | 1             |
| 64                    | 8      | 512              | 1                     | 8                    | 1             |

A fifth count-only case reproduced the maintained Vector2d lookup's four
normalized 64×1×64 grid bounds and positions `x=i&127`, `z=(17*i)&127` for
`i=0..1023`. Its 128 distinct XZ positions repeat eight times. Current 3D and
projected 2D point-candidate memberships were identical for every query: 384
queries had one candidate, 480 had two and 160 had four (1,984 total). All Y
hash cells were zero; exact admission and public planar grid lookup matched.

Maintained controls also completed all five selected benchmark cases (25 actual
measured iterations, 23 retained) and exited zero: Vector2d/Vector3d voxel
lookup means were 168.9/171.4 µs per 1,024 calls, with broad short-iteration
intervals; large-grid point lookup was 88.83/115.15/125.27 ns for the existing
8/64/256 oversized-grid parameters, plus their point grid. These are
current-path controls, not native-2D candidate measurements or universal scale
results.

**Scoped spatial disposition:** defer a parallel projected index for these point
recipes. Reopen with a measured existing operation where dimensional discovery
is material and total mixed-workload cost can improve. Area queries remain
unresolved in this initial screen. Its original 27-versus-nine neighborhood-
padding inference was incorrect for the actual caller and is corrected in the
completion section: exact `Query` uses padding zero. GridSpatialIndex may also
choose its full-active-grid fallback. The point screen does not reject all 2D
indexing opportunities.

#### Reproduction, validation and next boundary

Build the benchmark project in Standard Release using the source flags above,
then run from the repository root (restore the prior tiering environment after
measurement):

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet tests/GridForge.Benchmarks/bin/Release/net10.0/GridForge.Benchmarks.dll OccupantWaveBenchmarks --filter '*OccupantBuckets*' --warmupCount 10 --iterationCount 15 --artifacts artifacts/gf-v9-phase0/replay-occupancy
dotnet tests/GridForge.Benchmarks/bin/Release/net10.0/GridForge.Benchmarks.dll Vector2dLookupBenchmarks GridSpatialIndexLargeBenchmarks --filter '*TryGetVoxel_Vector*' '*PointLookup*' --warmupCount 3 --iterationCount 5 --artifacts artifacts/gf-v9-phase0/replay-spatial
```

Repeat the first command in three independent processes for the screen. All
executed reports, sample/launch counts and child/launcher status were inspected.
Profiler launch used the first command's same host and the churned-only filter,
with
`--invocationCount 256 --unrollFactor 1 --warmupCount 3 --iterationCount 20`.
Counts and input construction above make disposable probes reproducible without
retaining their temporary projects or ignored output. Supporting artifacts are
ignored under `artifacts/gf-v9-phase0`; they are not required to understand the
screen's results or resume the plan.

| Baseline gate                      | Standard Release                                    | ReleaseLean                             |
| ---------------------------------- | --------------------------------------------------- | --------------------------------------- |
| Solution build, warnings as errors | netstandard2.1 + net10.0 pass; zero warnings/errors | Both targets pass; zero warnings/errors |
| Full net10.0 GridForge tests       | 1,248 passed; zero failed/skipped                   | 1,248 passed; zero failed/skipped       |
| Reported GridForge lines           | 9,462 / 9,462                                       | 9,462 / 9,462                           |
| Reported branches                  | 4,243 / 4,243                                       | 4,243 / 4,243                           |
| Executable methods                 | 1,164 / 1,164                                       | 1,164 / 1,164                           |
| Existing strict coverage gate      | Passed                                              | Passed                                  |

No baseline runtime/build/test failures were found. Short benchmark iterations,
outliers, full-control variability and the isolated profiler export interaction
are the measurement limits described above. Linux CI and released-package mode
were not run locally. Lean performance was not compared; this screen makes no
cross-variant performance or adoption claim.

**Initial screen next step (completed below):** before any dense-map code,
define the smallest compatible representation for public long-generation tickets
and slot-ordered views, then freeze baseline output/order for a production
comparison. Include private uint retirement and pool-clear history. Admit at
most one prototype only if its mapping and ordering costs have a credible
bounded budget relative to the measured query-phase opportunity. Complete
churn/unload/reset lifecycle, retained heap accounting and balanced candidate
measurements remain unchecked. Neither an occupancy adoption nor Phase 0
completion is implied by the current screen.

## Phase 0 completion — 2026-11-24

**Decision: no adoption.** Retain SlotMap occupancy and existing 3D discovery;
defer TicketStore/quadtree as audited. Both production prototypes failed an
existing representative-regression gate. They were tested one at a time and
removed before expansion. Phase 0 and this feature evaluation are complete;
Phases 1/2 are not selected. Unreached adoption gates below are dispositions,
not passing results or work left pending.

### Controlled comparison and behavior evidence

The completion baseline was clean commit
`4b39a3077bcaface720d28bef856f49ee974fd35` (the initial screen committed), with
the same clean sibling revisions and Windows/SDK/runtime listed above. The only
retained C# addition is four area cases in the existing
[Vector2dLookupBenchmarks](../../../tests/GridForge.Benchmarks/Memory/Vector2dLookupBenchmarks.cs).
Existing occupant cases were reused unchanged. All shared-output builds/tests
and timings were serial. Exact baseline and candidate hosts were copied before
comparison; no runtime source changed during a timing series. Loaded local
source identities and DLL hashes were checked.

Each family used three ordered comparisons: baseline/candidate,
candidate/baseline, baseline/candidate (six processes, one launch per case).
Tiered compilation was disabled and its previous environment restored. These are
validation-inclusive synthetic workload results, not game-frame percentiles.
Positions and registration order follow the maintained recipes; the fixtures'
Guid keys are freshly generated per process, not a fixed random seed. Complete
reports, launch/sample counts and exits were inspected, including adverse cases.

- Dense prototype: **75 focused tests passed**. A disposable public-API oracle
  produced identical normalized output for dense and sparse storage: public
  slots/reuse, stale/default rejection, exact/typed/group-filtered queries,
  predicate scope, ordered 3D/planar radius results, multi-voxel multiplicity,
  unload/reload, ordinary reset and deactivation. Only log timestamps were
  normalized; messages and lifecycle event identities/order were retained.
  Oracle inputs: register 32 alternating typed/untyped occupants at one voxel,
  groups `id % 4`, remove the first 28, replace one survivor, register a
  survivor in a neighbor and another occupant one layer above, then
  unload/reload/reset. Stable construction IDs label references; a saved
  world/grid identity must fail after replacement. No new maintained
  regression/test-harness API was added for a representation that was
  subsequently rejected.
- A disposable retirement injection forced the actual private dense slot to
  `uint.MaxValue` and updated its private mapping consistently. Removal retired
  that private slot; re-add reused public slot zero with a new public long
  generation and a different private slot. The stale public ticket was rejected.
  This bounded result does not establish long-run retirement memory stability.
- Area prototype: **152 focused tests passed**. Eight small/wide route cases
  matched baseline/native projected IDs, full XYZ admission, sorted publication,
  exact covered-cell references/order and planar occupant layers. All four timed
  cases additionally assert the physical scan cell resolved independently from
  grid storage, including after reload reverses reusable grid-slot order.

### Occupancy: compatible dense representation rejected

The actual ScanCell prototype reused a public `SwiftSlotMap<SwiftTicket>` for
slot/free-history identity, a private `SwiftDenseTicketMap<OccupantEntry>` for
payload, and a `SwiftSortedList<int>` for increasing live public slots. The
public long generation remained in the entry and at its original issuer. A
struct enumerator avoided an extra heap iterator. It added two internal files,
about 88 nonblank lines, and three owners; all were removed after measurement.
This measured the compatibility cost at the production owner rather than a
container-only dense loop.

Five warmups, ten measured iterations and 64 invocations per iteration produced
240 actual measured iterations across 24 cases; retained samples by process were
35/39/37/34/36/36. Every process exited zero. Range below is the three
independent process means, in ms per existing eight-round phase:

| Production workload                           | SlotMap baseline | Compatible dense candidate |
| --------------------------------------------- | ---------------- | -------------------------- |
| Compact query                                 | 1.084–1.112      | 1.230–1.245                |
| Churned query                                 | 1.269–1.298      | 1.325–1.346                |
| Full query                                    | 6.468–6.715      | 7.481–7.668                |
| Refill/remove 7,168 registrations, then query | 6.690–6.832      | 7.444–7.613                |

The candidate reduced query allocation from approximately 240 to 224 KiB per
phase, but every measured latency range regressed. The allocation saving cannot
waive that gate. Actual array lengths and managed element sizes were inspected
outside timing (`Unsafe.SizeOf<T>` over the reachable backing arrays):

| 256-bucket fixture | Baseline backing payload | Candidate backing payload |
| ------------------ | ------------------------ | ------------------------- |
| Compact            | 49,152 bytes             | 98,304 bytes              |
| Churned / full     | 196,608 bytes            | 393,216 bytes             |

Baseline had 256 arrays/256 owner objects; candidate had 1,280 arrays/1,024
owner objects. These are backing payload/counts, excluding object headers, other
world state and fixture-held occupants, not complete retained heap sizes.
Private retirement can grow metadata beyond these capacities. Doubling even this
lower bound supplies no memory justification for the slower design.

**Disposition:** retain SlotMap. Reopen only for a real scan-dominant consumer
with measured occupancy/churn distribution and a simpler compatible ordered
lookup design that can remove the measured mapping/order cost. A separately
approved public ordering/ticket change would be a different design decision;
this evaluation does not authorize it.

### Spatial: actual area candidate rejected

**Corrected inference:** `SpatialHashCore.Query` calls
`ExecuteQuery(queryBounds, 0, true, ...)`; configured neighborhood padding
belongs to `QueryNeighborhood`, which GridForge does not call here. Null tracer
padding also means zero, followed by expansion by `MaxTopologyCellEdge`. The
original 27-versus-nine assertion was an incorrect documentation inference, not
a runtime defect. In the executed unit-cell recipe, small areas probe eight 3D
hash cells versus four projected cells. Wide areas probe 648 versus 324
potential cells, but the actual owner selects its existing full-active-grid scan
instead.

The 34-line candidate added one projected ordinary-tier hash at the existing
GridSpatialIndex owner, maintained it on insert/remove/clear, and passed
original flat-Y intent from the three existing tracer discovery helpers. It
preserved 3D classification, full-scan thresholds, oversized/contact BVHs, full
XYZ filtering, ascending slot publication and candidate-limit admission. No
backend framework, public API, configuration switch or navigation traversal was
added.

The count probe used 4/64 horizontal footprints × 1/8 layers, spacing 100, hash
cell edge 50, one physical sparse voxel and one occupant per grid. Small query
XZ `[-0.25,0.25]` used full scan for four registered grids and hash for
32/64/512. Wide `[-50,750]` used full scan in all cases. Separate projected-hash
queries multiply pre-Y hits by layer count; those are comparison counts, not
backend work attributed to the actual full-scan fallback. Exact filtered IDs,
covered-cell order and occupant layer/count matched in all eight rows.

The retained benchmark cases use 64 footprints × one/eight layers and the same
sparse/grid geometry. Each phase performs four rounds of 64 planar area queries
at Y zero, each interleaved with a true-3D query spanning Y `[-0.25,0.25]`: 512
independently checked cell hits. Reload cases additionally remove/re-add 64/512
grids (128/1,024 mutations) and resolve fresh physical expectations.

Three warmups, eight measured iterations and sixteen invocations produced 192
actual measured iterations across 24 cases; retained samples by process were
29/29/29/31/29/29. Every process exited zero. Ranges are µs per 512-query phase:

| Production workload              | Existing discovery | Projected candidate |
| -------------------------------- | ------------------ | ------------------- |
| Flat mixed area queries          | 171.4–172.5        | 164.4–165.1         |
| Stacked mixed area queries       | 184.2–185.4        | 212.3–215.0         |
| Reload flat world, then query    | 438.3–441.9        | 457.0–463.5         |
| Reload stacked world, then query | 5,366.8–5,417.2    | 6,095.7–6,207.2     |

The roughly 4% flat-query gain does not offset the repeated stacked-query and
lifecycle losses. Reload allocation increased from 105,276 to 111,452 bytes in
the flat fixture and about 1,428,010–1,428,028 to 1,434,434 bytes stacked. Warm
flat query reported zero allocation in both; the common 249-byte stacked BDN
observation is not a candidate improvement or per-call allocation claim.

A reflection graph walk outside timing counted index-reachable backing arrays,
deduplicating references, traversing SwiftCollections owners and value fields,
and summing array lengths × managed element sizes. It excluded headers and
external comparer/singleton objects. This is an array-payload lower bound:

| Warm index fixture | Baseline payload / arrays | Candidate payload / arrays |
| ------------------ | ------------------------- | -------------------------- |
| Flat               | 30,400 bytes / 76         | 42,208 bytes / 144         |
| Stacked            | 210,624 bytes / 524       | 253,664 bytes / 592        |

**Disposition:** retain existing discovery and defer projected indexing. Reopen
for a measured concrete bounded planar consumer with its actual query/mutation
ratio and vertical distribution, or a lower-cost exclusive design that preserves
3D/contact/bounded contracts. The measured flat gain shows why this is a scoped
no-go, not a claim that all 2D indexing is slower. No permanent shadow index or
experimental runtime branch remains.

### Reproduction and gate disposition

Both comparisons reuse the maintained source benchmark project. Build with
`-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`,
freeze the current host, apply only the described bounded owner change, and
build a separate candidate host. The ignored source snapshots are supporting
captures; the representation, inputs and results above stand without their
survival. Commands below operate on those frozen hosts, in A/B/B/A/A/B order:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet <host>/GridForge.Benchmarks.dll OccupantWaveBenchmarks --filter '*OccupantBuckets*' --warmupCount 5 --iterationCount 10 --invocationCount 64 --unrollFactor 1 --artifacts artifacts/dense-comparison
dotnet <host>/GridForge.Benchmarks.dll Vector2dLookupBenchmarks --filter '*PlanarAreas*' --warmupCount 3 --iterationCount 8 --invocationCount 16 --unrollFactor 1 --artifacts artifacts/area-comparison
```

Restore the prior tiering environment afterward. Logs, source snapshots, host
copies and disposable-oracle captures are ignored under
`artifacts/gf-v9-phase0-completion`. No profiling tool workaround was needed for
these unprofiled comparisons. Neither source provider nor CI dependency pins
changed. Baseline/candidate GridForge DLL SHA-256 identities:

- Occupancy baseline
  `bf14b10849a2f83ec9bdc22ff033c1f3d782f254e5ad5fbed1b7c4058e9c6640`.
- Occupancy candidate
  `645f0d7b40a9c682a730e1533b59943ee18aae79e3b10c66c1617ab248c08965`.

| Gate                                                                                           | Final disposition                                                                                                              |
| ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Candidate contract/behavior checks                                                             | Passed the focused tests and bounded oracles above; not a complete adoption certification.                                     |
| Balanced time/allocation comparison                                                            | Completed for both families; representative regressions caused both no-go decisions.                                           |
| Backing storage accounting                                                                     | Measured array payload/counts; complete retained heap, high-water endurance and host tail latency not reached after rejection. |
| Full occupancy movement/unload/reset host-cycle timing and broad topology/storage/churn sweeps | Not reached after rejection; bounded lifecycle behavior was checked, not timed as complete host acceptance.                    |
| Restore/serialization continuation and complete failure-atomicity matrix                       | Not reached; no state owner/schema changed or candidate retained.                                                              |
| Phases 1/2 and expanded candidate matrix                                                       | Not selected; no implementation obligation survives either no-go.                                                              |
| Final baseline validation                                                                      | Fresh Standard/Lean results below after removing both prototypes.                                                              |
| Final independent review                                                                       | Independent source/artifact review passed; no blocking findings. Final results and identity list were filled after the review. |

After both prototypes were removed, final source-mode validation passed:

| Final retained baseline gate       | Standard Release                                       | ReleaseLean                                                                          |
| ---------------------------------- | ------------------------------------------------------ | ------------------------------------------------------------------------------------ |
| Solution build, warnings as errors | Both netstandard2.1/net10.0 pass; zero warnings/errors | Both targets pass; zero warnings/errors                                              |
| Full net10.0 test suite            | 1,248 passed; zero failed/skipped                      | 1,248 passed; zero failed/skipped                                                    |
| Reported lines                     | 9,462 / 9,462                                          | 9,462 / 9,462                                                                        |
| Reported branches                  | 4,243 / 4,243                                          | 4,243 / 4,243                                                                        |
| Executable methods                 | 1,164 / 1,164                                          | 1,164 / 1,164                                                                        |
| Existing strict coverage gate      | Passed                                                 | Passed                                                                               |
| Retained area fixture              | Three balanced four-case comparisons above             | All six point/area methods passed one-invocation smoke; not a Lean timing comparison |

The Lean smoke completed six cases/six measured samples with zero launcher
errors. Scoped C# formatting, targeted Prettier and whitespace checks pass; all
relative links were checked after archiving this record. Linux CI,
released-package compatibility, candidate Lean timing, full retained-heap
endurance and game-frame percentiles were not run or claimed. These are not
remaining adoption requirements because both candidates were rejected before
those gates. No production source, dependency pin or CI policy change remains.

No runtime defect was attributed to the shipped implementation; candidate
regressions were resolved by removal, not promoted into the issue tracker or
benchmark backlog. Both runtime snapshots were restored from the owning HEAD
files. Final net runtime growth is zero; no new runtime files, fields, public
API or dependencies remain.

## TicketStore bucket follow-up — 2026-11-25

**Disposition: retain SlotMap for the tested contracts and workloads.** This
separately authorized comparison closes the previously untested TicketStore
question for one simpler compatible representation. It does not extend the
DenseTicketMap measurements to TicketStore or prove that every TicketStore
architecture loses. Spatial/quadtree decisions were not reopened.

### Representation and contract checks

The execution baseline was clean commit
`b513d37f52abbb75b228baf521ef6144c6545810`, using the same unchanged, clean
Chronicler/FixedMathSharp/SwiftCollections source revisions recorded above.
Windows x64, SDK 10.0.302, .NET 10.0.10 and BenchmarkDotNet 0.15.8 were used.
The source flags were
`-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`.

The actual ScanCell prototype used three existing owners:

- `SwiftSlotMap<byte>` only for public slot allocation and its existing free
  history, including empty-clear/pool reuse.
- `SwiftTicketStore<OccupantEntry>` with private bucket-local key
  `new SwiftTicket(publicSlot, 0)`. Generation zero is valid store-key data.
  Full public `long` generation checks stayed in ScanCell and the entry; no
  narrowing or private generation issuer was introduced.
- `SwiftSortedList<int>` for increasing live public-slot enumeration, with a
  struct enumerator. Exact reads and enumeration went directly to TicketStore,
  eliminating the previous DenseTicketMap prototype's mapping lookup.

Capacity was reserved before publishing a public registration. Sparse
reservation used `PeakCount + 1` only when the allocator had no holes; otherwise
`PeakCount`. Dense/live reservations stayed independent. This does not promise
unchanged private capacities after allocation failure across three owners. No
public API, monitor order, registry lifetime, generation issuer or serializer
changed. The prototype added two internal files with 64 nonblank code lines plus
owner and pool type substitutions; all were removed after the no-go decision.

The existing focused occupancy suite passed **75 tests**, including public
stale/default rejection, re-registration and generation exhaustion. A separate
disposable public-API oracle matched **33 normalized output lines** byte for
byte between frozen hosts. Only timestamps were normalized; messages, ordered
reference labels and lifecycle output remained. It reused the initial dense/
sparse query/filter/radius/multi-voxel/unload/reset recipe and added:

- Three fresh public slot-zero buckets across two worlds, with distinct public
  long generations; cross-bucket/world/default tickets must reject.
- Remove/re-add of public slot zero: the private `(0,0)` key repeats, but the
  old public long ticket must fail and the new occupant must resolve.
- Nonempty reset: old world/grid identity and public generation fail; only the
  fresh registration resolves.
- Fill 32 slots, remove all entries in ascending order, then rent the pooled
  bucket again. Public free history must reuse slots 31 then 30 and enumerate
  occupant IDs `1201,1200`; replacement must yield `1201,1202`, rejecting the
  old public ticket for slot 31. Reflection only cleared existing pools between
  isolated scenarios after previous worlds were disposed.

No DenseTicketMap private-retirement result was reused: TicketStore issues no
private generation in this design. Independent source review found no ordinary
contract blocker before measuring. The oracle and existing focused tests are
bounded behavior evidence, not complete adoption certification.

### Fresh balanced measurements

The maintained `OccupantWaveBenchmarks` cases were reused unchanged: 256
buckets, eight query rounds, compact 4-live/4-peak, churned 4-live/32-peak,
full32, and refill/remove 7,168 registrations before querying. Exact lookup,
ordered voxel views and caller-owned 3D/planar scans retain the same validation
work. No new benchmark runner or maintained test of benchmark infrastructure was
added.

Baseline and candidate hosts were built/frozen before timing. Six processes ran
in A/B/B/A/A/B order, with five warmups, ten measured iterations, 64 invocations
and unroll factor one. Each completed four cases and exited zero: 240 actual
measured iterations; retained counts were 35/40/39/38/39/38. Tiered compilation
was disabled, then the prior environment restored. Positions/order followed the
maintained recipes; Guid registration keys remained freshly generated per
process as in those fixtures. No competing build, test or profiler ran during
the series.

The table shows process means in **ms per phase**, with each pair's execution
order above. Fresh baselines were used; absolute times must not be compared with
the preceding day's DenseTicketMap series.

| Case                      | Baseline A1 | TicketStore B1 | TicketStore B2 | Baseline A2 | Baseline A3 | TicketStore B3 |
| ------------------------- | ----------- | -------------- | -------------- | ----------- | ----------- | -------------- |
| Compact query             | 1.158       | 1.265          | 1.248          | 1.181       | 1.191       | 1.318          |
| Churned query             | 1.344       | 1.346          | 1.324          | 1.420       | 1.353       | 1.374          |
| Full query                | 9.564       | 12.869         | 11.790         | 10.682      | 9.242       | 14.887         |
| Refill/remove, then query | 8.593       | 9.298          | 9.477          | 8.645       | 8.940       | 9.069          |

The churned query is near break-even/inconclusive: one candidate mean is lower,
while the other pairs are approximately equal or adverse. It is not a repeatable
win. Compact and full means were adverse in every pair. The full case varied
substantially, so the record does not claim a precise universal slowdown.
Mutation cycle means were also adverse, but the small final-pair gap is
inconclusive. Do not describe this as all four cases conclusively slower. The
combination of representative latency costs, no established whole-cycle gain and
added storage does not pass the existing adoption gates.

Query allocation fell from approximately **240 to 224 KiB per phase**; the
mutation/query phase fell from approximately 6,176.05–6,176.06 to
6,160.05–6,160.06 KiB. Those savings do not waive the latency/memory gate.

An outside-timing inspection summed actual backing-array lengths × managed
element sizes through the real buckets, using the same recipe as the initial
memory check:

| 256-bucket fixture | SlotMap payload | TicketStore design payload |
| ------------------ | --------------- | -------------------------- |
| Compact            | 49,152 bytes    | 90,112 bytes               |
| Churned / full     | 196,608 bytes   | 360,448 bytes              |

Baseline had 256 arrays/256 owner objects; candidate had 1,280 arrays/1,024
owner objects. Payload is **44 versus 24 bytes per reserved slot**,
approximately 83% more in these fixtures, excluding headers and other
world/fixture state. The byte-valued SlotMap entry is 12 bytes because it also
contains both free-link integers; it is not an eight-byte entry. This is smaller
than the earlier DenseTicketMap layout's 48 bytes/slot, but it still adds
substantial storage to the baseline. This is array-payload accounting, not a
complete retained-heap or long-run endurance claim.

### Decision, reproduction and final checks

The candidate was withdrawn under the existing representative-regression gate.
Keep the SlotMap implementation. Reopen only for a concrete measured workload
and a simpler order-preserving design or reusable provider capability that can
remove costs rather than merely move them. A different public slot/order
contract requires a separately justified API decision. The tested no-go says
nothing universal about TicketStore in consumers with natural external issuers
and no public slot-order repair requirement.

Build the source-mode benchmark host using the flags above and freeze it before
applying the described owner change. The comparison command, per frozen host,
was:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet <host>/GridForge.Benchmarks.dll OccupantWaveBenchmarks --filter '*OccupantBuckets*' --warmupCount 5 --iterationCount 10 --invocationCount 64 --unrollFactor 1 --artifacts artifacts/ticketstore-comparison
```

Restore the prior environment afterward and use three balanced pairs. Baseline
GridForge DLL SHA-256:
`93d9c4a49efb50cd4f065e95b6dccbc3a817134bb6188a0d724eb50b01d3e6bc`; candidate:
`53b54a9f25c8d33a0302371df3d667c3a135aa667fb50d59fd9bf733458e0c91`. Ignored
logs, hosts, oracle captures and the rejected prototype source are under
`artifacts/gf-ticketstore-comparison`. The representation, maintained fixture,
input/oracle recipes and results above survive removal of those captures.

After restoring the original runtime files, fresh source-mode checks passed:

| Retained baseline gate             | Standard Release                                  | ReleaseLean                             |
| ---------------------------------- | ------------------------------------------------- | --------------------------------------- |
| Solution build, warnings as errors | netstandard2.1/net10.0 pass; zero warnings/errors | Both targets pass; zero warnings/errors |
| Full net10.0 tests                 | 1,248 passed; zero failed/skipped                 | 1,248 passed; zero failed/skipped       |
| Reported lines                     | 9,462 / 9,462                                     | 9,462 / 9,462                           |
| Reported branches                  | 4,243 / 4,243                                     | 4,243 / 4,243                           |
| Executable methods                 | 1,164 / 1,164                                     | 1,164 / 1,164                           |
| Existing strict coverage gate      | Passed                                            | Passed                                  |

No baseline build/test failures were observed. Full-case variability and the
inconclusive churned/final mutation differences are measurement limits, not
failures concealed by the no-go decision. Candidate Lean timings, the broad
lifecycle/restore matrix, complete retained heap and host-frame tails were not
reached after rejection; Linux and released-package validation were not run. The
new public-API oracles and focused tests passed, but no complete cross-variant
candidate adoption claim is made. No runtime, dependency, benchmark, maintained
test or CI change remains. Only the plan and overview are updated, unstaged.

Independent final source, evidence and record review passed with no substantive
blocking findings; the completed review was checked against the raw captures.

## Cached-position and provider lookup experiments — 2026-11-25

**Disposition:** retain SlotMap in GridForge. The compatible cached-position
bucket established no repeatable query benefit and regressed the mutation cycle.
A small DenseTicketMap `TryGet` change did improve the tested provider lookup
batches; pursue it as focused SwiftCollections work. Neither experiment leaves
maintained runtime changes in either repository.

### Source isolation and reproduction

Use GridForge `b513d37f52abbb75b228baf521ef6144c6545810`, SwiftCollections
`7c2fa696f10d88194f5a895aaafd7debd8e27574`, FixedMathSharp
`8fdf889322f1bb82501c2afec63d6bb2f304ace2` and Chronicler
`0281469ecc40e65a05867f473bc10f030e7f69f8`. The two earlier documentation edits
were preserved. Sibling SwiftCollections advanced to `107c8868` during setup;
all experiment builds instead used detached sibling worktrees under
`$env:TEMP/gf-cached-position-stack`, pinned to the revisions above. Unrelated
changes in the original sibling checkout were untouched. This validates the
controlled source graph, not its newer HEAD.

Windows x64, SDK 10.0.302, .NET 10.0.10 and BenchmarkDotNet 0.15.8 were
retained. Every source build/test used
`-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`. One
coordinator serialized builds, tests and timings. Freeze benchmark hosts before
timing; no competing build/test/profile ran during either timing series. No
dependency or CI pin changed.

### Cached-position bucket

Starting with the prior TicketStore prototype, change `SwiftSlotMap<byte>` to
`SwiftSlotMap<int>` holding each public slot's current dense position. Both
SlotMap entry layouts occupy 12 bytes. Reads validate the public slot, acquire a
fresh `Values` view and index that position. ScanCell retains its full public
`long` generation check; sorted public-slot iteration and allocator free history
remain unchanged.

TicketStore explicitly invalidates dense-position associations after structural
changes. This compatible candidate therefore eagerly rebuilds the mapping with
`slots[store.GetTicketAt(i).Index] = i` for every live position after each
successful Add/Remove and actual `EnsureCapacity` growth. Reserve SlotMap and
the sorted view first, then Store; refresh immediately after actual Store growth
before publishing the registration. Reads never repair caches. Incremental
swap-only repair would depend on behavior beyond the documented invalidation
contract and was not substituted for this measured design. Mutations pay the
full O(live count) refresh cost.

The candidate added two internal files and **83 nonblank, noncomment runtime
lines**, plus owner/pool type substitutions. Independent source review found no
blocker within the bounded workloads. One unproven limit remains: Store requires
indices below `2^30`, whereas SlotMap has no identical explicit limit. Promotion
would require a supported-limit decision; these fixtures establish no global
arithmetic-limit equivalence.

The 75 existing focused occupancy tests passed. A disposable public-API oracle
matched **37 timestamp-normalized output lines** between frozen SlotMap and
cached hosts. It retains the preceding lifecycle/filter/order recipe and adds
exact ticket/reference and literal public-slot-order checks at 8, 9 and 17 live
entries in both Dense and Sparse grids. Actual dense-tail removal before swaps,
first/middle/missing removal, LIFO slot reuse and stale-long rejection passed.
This supplements existing reset, pool free-history and cross-world controls; it
is bounded behavior evidence, not complete adoption certification.

Fresh timings reused the four unchanged `OccupantWaveBenchmarks` cases above.
Run order was **A/B/C/C/B/A**: A=SlotMap, B=uncached TicketStore, C=cached
Store. The authored benchmark source was unchanged and provider dependencies
were byte-identical. Cached C had recompiled GridForge.Benchmarks.dll; A/B used
one frozen benchmark DLL. These compiled harnesses were not byte-identical, so
this initial series retains that confound rather than claiming a runtime-only
comparison. B reused the preserved prototype DLL from the same provider graph.
Five warmups, ten measured iterations, 64 invocations and unroll factor one were
used, with tiering disabled and prior environment restored. All six processes
exited zero and completed four cases: **240 actual measured iterations**,
retained counts 37/38/37/38/37/39 after BDN filtering.

Process means, **ms per phase**, in execution order:

| Case                      | A1 SlotMap | B1 Store | C1 cached | C2 cached | B2 Store | A2 SlotMap |
| ------------------------- | ---------- | -------- | --------- | --------- | -------- | ---------- |
| Compact query             | 1.110      | 1.179    | 1.210     | 1.283     | 1.202    | 1.134      |
| Churned query             | 1.318      | 1.315    | 1.340     | 1.294     | 1.294    | 1.350      |
| Full query                | 7.332      | 7.514    | 7.548     | 8.069     | 8.159    | 7.105      |
| Refill/remove, then query | 7.210      | 8.198    | 8.337     | 8.439     | 8.025    | 7.189      |

Churned differences remain inconclusive. Full-query results vary and establish
no consistent cache benefit over Store; compact caching is adverse in both runs.
Both cached mutation means exceed both baseline means by approximately 16–17%.
The cache does not earn its refresh logic. These measurements do not attribute
every difference to caching or establish a universal slowdown.

Final review caught the compiled-harness difference before acceptance. A
separate **D/E/E/D control** used the baseline frozen host for both arms,
replacing only GridForge.dll for cached E; every other DLL was byte-identical.
Settings and fixtures were unchanged. All four processes exited zero and
completed four cases: **160 actual measured iterations**, retained counts
39/38/37/38. This is the runtime-only comparison against retained SlotMap.

| Case                      | D1 SlotMap | E1 cached | E2 cached | D2 SlotMap |
| ------------------------- | ---------- | --------- | --------- | ---------- |
| Compact query             | 1.114      | 1.242     | 1.207     | 1.130      |
| Churned query             | 1.316      | 1.369     | 1.283     | 1.303      |
| Full query                | 7.509      | 7.901     | 7.583     | 6.965      |
| Refill/remove, then query | 7.536      | 8.267     | 8.551     | 7.838      |

The controlled mutation means average 7.687→8.409 ms, approximately **9.4%
slower**, while compact query means average 1.122→1.2245 ms. Churned results
cross and remain inconclusive; full-query dispersion prevents a precise general
slowdown claim. The cache still establishes no repeatable query gain, adds
retained storage and loses the complete mutation cycle. The initial series is
preserved with its limitation, rather than used to attribute its entire 16–17%
difference to the runtime change.

Both Store designs allocate about 224 versus 240 KiB per query phase and
6,160.06 versus 6,176.06 KiB per mutation phase. Actual backing-array payload is
unchanged by caching: compact 90,112 versus baseline 49,152 bytes; churned/full
360,448 versus 196,608 bytes. Store designs have 1,280 arrays/1,024 collection
owners versus 256/256 for SlotMap, excluding headers and other world/fixture
state: **44 versus 24 bytes per reserved slot**. This does not measure the whole
heap, endurance or host-frame tails.

### DenseTicketMap provider lookup

Disassembly of the real baseline `TryGet` API confirmed a repeated position
read: `cmp [slot+4],0` validates liveness, then `mov ..., [slot+4]` reloads it
on success. Store's `Find` already retains its resolved position; no
corresponding Store patch was justified.

In the isolated provider only, replace Dense `TryGet`'s `IsValid` call and
second slot lookup with this body, leaving `IsValid`, `GetRef`, API, layout and
persistence unchanged:

```csharp
if ((uint)ticket.Index < (uint)_issued)
{
    ref Slot slot = ref _slots[ticket.Index];
    int position = slot.Position;
    if (position >= 0 && slot.Generation == ticket.Generation)
    {
        value = _values[position];
        return true;
    }
}
value = default!;
return false;
```

This adds a net five nonblank runtime lines and no files. Independent review
confirmed supported externally synchronized semantics; concurrent mutation
remains unsupported. The real-API oracle passed **8,228 checks**, checksum
7,929,856, on both DLLs:
live/reused/removed/unissued/negative/excessive/default/wrong- generation
tickets, default miss outputs and throwing GetRef controls. Existing Dense tests
passed **125 tests**, including state/restore, retirement, growth, swap removal,
reference/default values and reuse. Candidate disassembly loads Position once
and reuses its register; wrapper size fell **125→108 bytes** for
reference-plus-long payload and **105→88 bytes** for int. Array bounds and
generation checks remain. GetRef's uninlined wrapper was not used to infer its
callee's loads.

Reuse the provider's maintained `Dense_Lookup` workloads. Each batch reads every
live ticket in ticket-array order and consumes the values. Int32 cases specify
live-count × peak multiplier. Referencing is a 16-byte int-plus-reference value;
the exact reference-plus-long probe payload was checked for behavior/codegen,
not timed here.

```powershell
dotnet <frozen-provider-host>/SwiftCollections.Benchmarks.dll all --filter '*DenseTicketMapInt32Benchmarks.Dense_Lookup*' '*DenseTicketMapReferencingBenchmarks.Dense_Lookup*' --inProcess --launchCount 1 --warmupCount 3 --iterationCount 8 --iterationTime 150 --artifacts <distinct-artifact-path>
```

Set tiered compilation and ReadyToRun to zero; restore prior environment values
afterward. Run baseline P / candidate Q / Q / P, with an identical host except
SwiftCollections.dll. The in-process toolchain avoids child source rebuilds.
Each process produced four successful reports and 32 actual measurements: **128
total**, retained counts 30/29/31/30. Launcher exit alone was not accepted as
success. An additional disposable throughput harness was prepared but never
built or run; no maintained harness was added.

Process means, **microseconds per complete lookup batch**:

| Payload / case       | P1 baseline | Q1 candidate | Q2 candidate | P2 baseline | Mean reduction |
| -------------------- | ----------- | ------------ | ------------ | ----------- | -------------- |
| Int32 / 128x1        | 0.3141      | 0.2519       | 0.2543       | 0.3064      | 18.4%          |
| Int32 / 4096x4       | 9.7173      | 7.9704       | 7.9328       | 9.7319      | 18.2%          |
| Int32 / 16384x10     | 38.9695     | 32.1307      | 32.0269      | 38.7848     | 17.5%          |
| Referencing / 4096x4 | 10.55       | 10.01        | 9.908        | 10.42       | 5.0%           |

Both Q means beat both P means in each case. The one-byte BDN allocation report
for the largest int case appears in both arms; no allocation gain is claimed.
This is a promising provider all-hit result under one Windows x64 fixed-codegen
configuration. Miss-heavy timing, normal tiered/PGO behavior, other platforms/
runtimes, full provider Standard/Lean coverage and downstream consumers remain
unchecked. GridForge's retained bucket does not consume Dense; this establishes
neither a GridForge gain nor a TicketStore gain.

**Next action:** take only Dense `TryGet` into separately scoped provider work;
complete provider compatibility/Standard/Lean gates and representative normal-
codegen/miss controls before accepting a maintained change. No provider defect
or new GridForge issue is asserted by this optimization signal. Keep GridForge
adoption closed unless a real workload and simpler contract-preserving design
meet its full cost gates. Update owning CI source pins after an accepted
upstream change lands; pins are outside this experiment.

### Final checks and evidence

Build/freeze the source-mode benchmark projects with the source graph/flags
above. The GridForge command is unchanged from the preceding follow-up; use
distinct artifact directories and A/B/C/C/B/A order. Source snapshots, patches,
hashes, logs, oracle sources, disassembly and reports are ignored under
`artifacts/gf-cached-position`. The designs, recipes and results above survive
removal of those captures.

GridForge baseline/uncached/cached DLL SHA-256 values are respectively
`93d9c4a49efb50cd4f065e95b6dccbc3a817134bb6188a0d724eb50b01d3e6bc`,
`53b54a9f25c8d33a0302371df3d667c3a135aa667fb50d59fd9bf733458e0c91`, and
`44ba3116e673cfade96b8e8c25c08fca032d206f5b9665383e921b5a1212d468`. Provider
baseline/candidate hashes are
`2ff7a7d889e9c0e7064936edd6f7431228574293a35c4a7eb3d1b4fae906df9e` /
`5b4c30e408d85b19edefff0a6cf1b764027b6dbe8c93ce674e7070ba2932e451`.

Both experimental patches were archived and removed from isolated worktrees.
Fresh retained-baseline source-mode verification passed:

| Gate                                      | Standard Release                             | ReleaseLean                        |
| ----------------------------------------- | -------------------------------------------- | ---------------------------------- |
| Solution build, warnings as errors        | netstandard2.1/net10.0; zero warnings/errors | Both targets; zero warnings/errors |
| Full net10.0 tests                        | 1,248 passed; zero failed/skipped            | 1,248 passed; zero failed/skipped  |
| Reported lines                            | 9,462 / 9,462                                | 9,462 / 9,462                      |
| Reported branches                         | 4,243 / 4,243                                | 4,243 / 4,243                      |
| Executable methods / strict coverage gate | 1,164; passed                                | 1,164; passed                      |

No baseline build/test failure or introduced correctness failure was observed.
The provider focused-test build emitted an existing `xUnit2013` warning in
unchanged `SwiftSlotMap.Tests.cs:138`; it is separate from these zero-warning
GridForge final builds. Cached candidate Lean timings/full lifecycle matrices
were not expanded after its cost gate failed; provider adoption certification
remains separate. Linux and released-package checks were not run. Independent
source/codegen and final evidence/record reviews passed with no blocking
findings. The final review independently checked the controlled host hashes,
timing/sample counts, oracle outputs, raw coverage and clean isolated runtime
state. No CI policy, runtime, dependency, benchmark or maintained test change
remains; only the two documentation files are updated, unstaged and uncommitted.

## Phase 1 — Smallest compatible occupancy slice, if selected

Not selected after the completed Phase 0 no-go; the conditional checklist below
is historical adoption work, not pending work for this closed evaluation.

- [ ] Write focused production regressions at ScanCell/GridOccupantManager/
      GridScanManager first. Use A/B/C occupants in one voxel, remove A or B,
      reuse the slot, then compare exact ticket identity and full slot-ordered
      enumeration with the baseline. Repeat in neighboring buckets sharing slot
      numbers and in another world with matching local indices. Verify
      default/removed tickets cannot read or remove replacements; same occupant
      re-registration is a new lifetime.
- [ ] Complete and expand only the compatible ScanCell prototype selected in
      Phase 0, using its existing pool owner. Retain process-generation
      admission and exact voxel grouping. If compatibility demands a persistent
      parallel slot owner plus dense map plus remapping, include all three costs
      and stop if that defeats the measured benefit. Do not retain the old and
      new machinery as selectable runtime paths after the decision.
- [ ] Exercise duplicate/reference-colliding Guid rejection, full voxel
      capacity, failed tracking rollback, throwing query predicates, filter
      scope/order, ticket reads concurrent with reset under existing monitors,
      pool release, world deactivation and reset without deactivation.
      Count/event/active-cell/ registry changes must match current behavior,
      including partial-result failure.
- [ ] Run focused tests and the same complete host-cycle comparison before
      expanding all query variants. Obtain independent source review of
      ownership, order and deletion opportunities; then update all affected
      typed/untyped and 2D/3D callers together. Revert the experimental
      representation on no-go and record the reason here. Delete superseded
      storage/pool helpers only when their final production consumer is removed.

## Phase 2 — Smallest planar broad-phase slice, if selected

Not selected after the completed Phase 0 no-go; these conditional items were not
reached. Reopening requires the evidence described above.

- [ ] Complete and expand the one planar production operation whose Phase 0
      prototype established a useful complete-cycle benefit. Compare projected
      BVH with native spatial hash using representative scale/churn, not a new
      generic backend hierarchy. Defer quadtree unless Phase 0 establishes its
      bounded-world consumer.
- [ ] Keep ownership in GridSpatialIndex/GridWorld. Specify exactly which
      registration/removal/reset operations maintain the index and which planar
      callers read it. A parallel projected index is acceptable only after
      charging its whole-world cost, including full rebuild, cold growth, all
      mutations, retained storage, exact Y filtering and sorting against the
      baseline; otherwise retain 3D discovery. Do not keep a permanent shadow
      index for comparison after the experiment. Any change to externally
      observable hard candidate-work-limit admission or terminal outcomes is a
      no-go even when complete unlimited queries return the same answers.
      Preserve the existing large-query fallback/cell-budget protections and
      sorted output. Do not project the contact-envelope or nearest-voxel proof
      into 2D.
- [ ] Prove vertical false-positive rejection with two grids having identical XZ
      footprints at different Y, ordering the ineligible one first. Then remove
      the eligible grid and require the query to miss. Include same-slot stale
      handles with different grid generations after unload/reload, selected
      layers crossing different voxel heights, negative floor boundaries, exact
      edge/corner touch, hex axial ranges, sparse holes, oversized bounds and an
      empty/reused world.
- [ ] Compare complete scalar/Into results and deterministic candidate order,
      exact voxel selection, predicates, multiplicity and failure mutation
      against the current path. Preserve native planar navigation's explicit
      eligible-layer validation, hard capacities, stamps and strict swept/closed
      endpoint semantics; this phase does not replace that trace's traversal.
- [ ] Review the first complete operation and retained-memory accounting before
      expanding. Share only applicable existing GridPlane2d projection/admission
      policy; avoid a forwarding facade, copied geometry or per-caller indexes.
      Remove superseded internals if an exclusive replacement is proven; remove
      the experiment if it is not. Rerun the mixed/full host cycle after
      expansion.

## Phase 3 — Verification and handoff

Completed for the retain/defer outcome through the final baseline gates and
review above. Candidate-adoption-only items below were not reached, as
explicitly accounted for in the gate-disposition table.

- [ ] Run focused real-world regressions before the full matrix: churn/order/
      stale-ticket controls for occupancy, and stacked/hex/sparse/extreme-range
      controls for spatial work. Preserve existing regressions rather than
      weakening them to fit dense or tree order. Repeated runs must produce the
      same canonical identities, ordered query results, observable events and
      continuation after reset; runtime process tokens are compared for their
      safety relations, not assumed identical numeric values across processes.
- [ ] Verify state/restore where an actual affected owner supports it: JSON and
      MemoryPack in Standard, JSON/state in Lean, invalid state/failure
      atomicity, subsequent reuse and stale abandoned-future handles. GridForge
      world/pool reconstruction must rebuild derived indexes and preserve public
      contracts. Provider-only state tests do not establish consumer lifecycle
      safety. Do not invent a world snapshot format merely to satisfy this
      checklist.
- [ ] Build Standard `Release` and `ReleaseLean` for `netstandard2.1` and
      `net10.0`; run the net10.0 GridForge tests in both configurations and the
      repository's Windows/Linux CI matrix. Collect fresh coverage using
      `tests/GridForge.Tests/coverlet.runsettings`, then run
      `.assets/scripts/assert-full-coverage.ps1` per configuration. Require 100%
      reported line, branch and executable-method coverage with raw
      uncovered-count inspection; remove proved unreachable branches, never add
      exclusions.
- [ ] Use existing `UseLocalLsfStack=true` for coordinated source validation;
      propagate it through restore/build/test and benchmark child builds. Carry
      `-p:DisableTransitiveProjectReferences=true` on every source-mode command
      and serialize shared-output builds/coverage/timings. Record exact provider
      and consumer revisions and any local source patches. Existing
      released-package validation remains distinct from source validation; do
      not rewrite references for this experiment.
- [ ] Provider changes are not presumed necessary. If a missing reusable
      contract or defect is proven, fix it in SwiftCollections first with its
      own behavior regression and Standard/Lean core+companion 100% line/branch/
      executable-method gates, then validate GridForge on that source graph.
      Remind the owner to update actual CI build/test dependency pins after
      upstream changes land. Pin updates or package releases do not keep
      verified feature work open.
- [ ] Reuse the same maintained benchmark inputs and run balances for final
      comparison; inspect complete results, adverse cases, warm allocations,
      retained-memory high water and full-cycle latency. A lookup win alone
      cannot close a scanning or host-cycle claim. Resolve introduced
      regressions in this change unless the user explicitly accepts the measured
      tradeoff.
- [ ] Update the affected conceptual guides/API comments for actual adopted
      behavior, then this plan and the feature-work overview. Review final
      connected owners independently, including net runtime growth, duplicate
      paths, remaining production callers and deletions. Leave edits
      unstaged/uncommitted. Archive this plan under `done/` by repository
      convention when evaluation and all selected implementation/verification
      work are complete.

Completion evidence belongs in this plan: selected/no-go decisions; exact source
graph and input recipes; build/test/coverage counts; focused counterexample
outcomes; deterministic continuation/state results where applicable; complete
performance/memory comparisons; removed machinery; remaining explicit deferrals.
No release gate or separate phase report is required.
