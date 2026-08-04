# Two-Tier Grid Spatial Index Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` or `superpowers:executing-plans` to
> implement this plan phase-by-phase. Use `superpowers:systematic-debugging`
> for unexpected behavior, `superpowers:test-driven-development` for runtime
> changes, `performance-optimization-engineer` for every benchmark decision,
> `superpowers:requesting-code-review` for independent phase/final review, and
> `superpowers:verification-before-completion` before claiming a phase is
> complete. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Phase 3 complete; ready for owner review. Longer ordinary-query
performance confirmation remains a Phase 4 release gate.

**Goal:** Make top-level `VoxelGrid` registration, removal, lookup, overlap,
neighbor discovery, and traversal scale with active grids rather than the empty
world-space volume covered by an extreme grid or query, while preserving the
ordinary spatial-hash fast path.

**Architecture:** Replace GridForge's exposed bespoke grid hash with one
internal, world-owned two-tier index. Ordinary grids live exclusively in a
`SwiftFixedSpatialHash<ushort>`; grids whose calculated hash-cell footprint
exceeds an internal evidence-selected budget live exclusively in a
`SwiftFixedBVH<ushort>`. Candidate queries inspect both tiers or scan active
grids when the query-cell volume itself is more expensive, then return exact
bounds-filtered grid slots in ascending deterministic order.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`,
`SwiftCollections.FixedMathSharp`, `GridForge`, xUnit v3, BenchmarkDotNet,
Gravitas mixed queries, standard and Lean package variants.

## Global Constraints

- Determinism, performance, maintainability, and correctness are all release
  gates; do not trade one away silently.
- Use only fixed-point runtime geometry. The numeric/System.Numerics BVH is not
  eligible.
- A grid belongs to exactly one index tier. Do not duplicate every grid across
  both structures or add cross-tier deduplication work.
- Keep the tier threshold internal and evidence-selected. Do not add another
  host-facing tuning knob.
- Preserve `GridWorld.SpatialGridCellSize` as an optional ordinary-workload
  tuning value, not a requirement that must match the world's largest grid.
- Large query volumes must not enumerate empty spatial-hash cells merely
  because ordinary grids exist.
- Candidate order is ascending world-local `GridIndex`, independent of hash
  bucket order, BVH tree shape, insertion order, removal history, or pooled
  reuse.
- Hot query paths must allocate `0 B` after warmup. Cold index growth and the
  BVH's first-thread traversal scratch must be measured and documented rather
  than hidden.
- GridForge and Gravitas must retain 100% reachable line, branch, and method
  coverage at every phase review boundary.
- FixedMathSharp and SwiftCollections source are not expected to change unless
  measurement exposes an upstream defect. Any such change requires a focused
  regression and its full test gate. SwiftCollections' existing repo-wide
  coverage restoration remains its separate non-blocking workstream; changed
  spatial-hash files must retain complete reachable coverage.
- Preserve local project links as unstaged validation scaffolding. Do not commit
  `.csproj`, `.slnx`, or `Directory.Build.props` local-link changes.
- Leave implementation changes unstaged and uncommitted for owner review.
- Do not modify SwiftCollections unless measurement exposes a defect in the
  existing fixed spatial hash or BVH contract. A merely different performance
  profile is not an upstream bug.

---

## Status And Working Agreement

- Planning date: 2026-08-03.
- Originating signal: Gravitas `Mixed public sweep traversal stalls on extreme
  sparse-grid spans`.
- Release posture: intentional GridForge v8-to-v9 breaking cleanup; v9 is not
  released.
- Current state: Phase 3 makes the two-tier index authoritative for every
  top-level lifecycle, lookup, overlap, traversal, scan, and neighbor caller.
  Public single-hash implementation APIs and obsolete hash regrouping are
  removed.
- Coverage context: GridForge retains 100% reachable line, branch, and method
  coverage. SwiftCollections remains at 97% in its separate owner-led hardening
  workstream; every changed spatial-hash source file is at 100% reachable line
  and branch coverage.
- Review cadence: stop after each phase for owner review unless explicitly
  asked to combine phases.
- Evidence rule: preserve raw before/after/confirmation artifacts under each
  repository's ignored `artifacts/benchmarks` directory and record the command,
  job, median, allocation, and artifact path in this plan.

## Why This Work Exists

`GridWorld` currently maps every active grid into every overlapping cell of one
fixed-resolution top-level spatial hash. With the default 50-unit hash cells, a
grid normalized to `[-100,000, +100,000]` on each axis spans 4,001 hash cells
per axis:

```text
4,001 x 4,001 x 4,001 = 64,048,012,001 cell visits
```

The reported Gravitas diagnostic therefore stalls while GridForge registers the
grid, before mixed-query narrow phase begins. Giving the host a matching
100,000-unit global hash cell makes the same query finish, but that is not an
acceptable runtime contract: a host should not need to tune one global scale to
the largest grid it may ever stream.

GridForge already avoids enumerating enormous query-cell volumes in
`GridCandidateDiscovery` by scanning active grids when that is cheaper. The
registration and removal paths have no equivalent protection. A linear
oversized-grid list would fix registration but make every query
`O(oversized-grid count)`. Replacing the whole hash with a BVH would avoid that
scan but regress ordinary uniform-grid lookups.

Directional SwiftCollections evidence captured during RCA supports a split:

| Scenario | Spatial hash | BVH | Directional conclusion |
| --- | ---: | ---: | --- |
| Sparse small-object needle queries, 2,048 entries | 343.4 us | 4,207.9 us | Keep the hash fast path |
| Sparse small-object needle queries, 8,192 entries | 271.3 us | 6,348.4 us | Do not replace the hash globally |
| Extreme size variance, 2,048 entries | 25.815 ms | 9.065 ms | Route large footprints away from the hash |
| Extreme size variance, 8,192 entries | 27.947 ms | 11.069 ms | A BVH is materially safer for heterogeneous scale |

These are System.Numerics collection-comparison rows, not proof of GridForge's
final fixed-point implementation. They justify the architecture hypothesis;
the GridForge-specific before/after matrix below decides whether it ships.

## Locked Design

### Ownership

Create one focused internal owner:

```text
GridWorld
  -> GridSpatialIndex
       -> SwiftFixedSpatialHash<ushort> ordinary grids
       -> SwiftFixedBVH<ushort> oversized grids
```

`GridWorld` owns lifecycle and active-grid semantics. `GridSpatialIndex` owns
only top-level spatial classification, registration, removal, clearing, and
candidate collection. It does not own grids, topology, occupants, partitions,
streaming policy, or host configuration.

### Tier Selection

For each normalized grid bound:

1. Compute the exact fixed-spatial-hash cell range using the same mathematical
   floor contract as `SwiftFixedSpatialHash`.
2. Compare the X/Y/Z cell-count product to the internal cell budget with
   division-before-multiplication checks so neither signed nor unsigned
   overflow is reachable.
3. Insert the grid into exactly one tier.
4. Retain that tier until removal. Do not migrate existing grids when other
   grids are added or removed.

The initial benchmark candidates are 64, 512, and 4,096 cells (4x4x4, 8x8x8,
and 16x16x16 cubic footprints). Eliminate any candidate that causes a repeatable
ordinary-workload regression greater than 5%, a warmed allocation, or unsafe
registration growth. Among remaining candidates, choose the best mixed-scale
geometric mean; if candidates differ by less than 5%, choose the larger budget
to keep more ordinary grids on the hash fast path. Record the selected value
and evidence in this plan. Only the selected constant remains in production.

### Candidate Discovery

Candidate collection receives world-space min/max bounds, not precomputed hash
keys. It follows one deterministic plan:

```text
if query hash-cell volume > active grid count:
    scan active grids and retain exact intersecting bounds
else:
    query ordinary fixed spatial hash
    query oversized fixed BVH

sort candidate grid slots ascending
```

The comparison is overflow-safe and must not multiply a query span after it is
already known to exceed the active-grid count. Both indexed tiers perform exact
`FixedBoundVolume` intersection filtering. The active-grid scan must do the
same rather than returning every active grid and relying on later topology
work.

Point lookup uses a degenerate point volume and resolves the lowest valid grid
slot when overlapping grids contain the same position. Top-level neighbor and
overlap queries expand by the target topology's exact overlap tolerance, then
use the index's exact `FixedBoundVolume` intersection filter as the single
grid-bound relation.

### Public Boundary

Keep:

- `GridWorld.DefaultSpatialGridCellSize`
- `GridWorld.SpatialGridCellSize`
- `GridWorld(int spatialGridCellSize = DefaultSpatialGridCellSize)`

Remove from the v9 public surface:

- `GridWorld.SpatialGridHash`
- `GridWorld.GetSpatialGridCells(...)`
- `GridWorld.GetSpatialGridKey(...)`
- `VoxelGrid.IsGridOverlapValid(...)`

The three `GridWorld` APIs expose one implementation tier and become misleading
once some grids are intentionally absent from the hash. They are not valid host
extension points. The overlap helper duplicated the new index's exact
expanded-bound filter and had no production caller. Do not retain compatibility
facades, duplicate predicates, or merged hash snapshots.

### Allocation And Concurrency Boundary

- Reuse caller-owned `GridTraceScratch` and `GridScanScratch` candidate lists.
- Remove `ProcessedGrids` when the fixed spatial hash's query stamp and
  mutually-exclusive tiers make it redundant.
- Give `GridWorld` one retained candidate list for its scalar point/overlap
  helpers rather than allocating a list per call.
- Preserve the current single-owner world mutation contract. Do not introduce
  locks, parallel traversal, or a new concurrency promise in this workstream.
- Measure the BVH's per-thread first-query scratch allocation separately from
  warmed steady-state allocation. Prewarm only if doing so removes a real
  GridForge runtime spike without adding a new public lifecycle requirement.

## File And Responsibility Map

### GridForge production

- Create `src/GridForge/Grids/Managers/GridSpatialIndex.cs`
  - owns fixed hash/BVH tiers, tier selection, overflow-safe query-plan choice,
    exact candidate filtering, stable ordering, and clearing.
- Modify `src/GridForge/Grids/Managers/GridWorld.cs`
  - delegates registration/removal/point/overlap/neighbor candidate work;
    removes exposed hash authority and obsolete key/cell helpers.
- Modify `src/GridForge/Grids/VoxelGrid.cs`
  - removes the duplicate public overlap predicate after its single exact owner
    moves into the spatial index.
- Modify `src/GridForge/Utility/GridTracer.cs`
  - passes world-space candidate bounds to the shared index.
- Modify `src/GridForge/Utility/GridTracer.TraceLine.cs`
  - uses the same bounds-based candidate contract for line tracing.
- Modify `src/GridForge/Grids/Topology/VoxelNeighborResolver.cs`
  - uses the shared two-tier candidate contract.
- Modify `src/GridForge/Grids/Support/GridTraceScratch.cs`
  - removes redundant processed-grid state.
- Modify `src/GridForge/Grids/Support/GridScanScratch.cs`
  - removes redundant processed-grid state.
- Delete `src/GridForge/Utility/GridCandidateDiscovery.cs`
  - its adaptive plan moves into the single spatial-index owner.

### GridForge tests and benchmarks

- Create `tests/GridForge.Tests/Grids/GridSpatialIndexTests.cs`
  - covers tier exclusivity, threshold boundaries, overflow-safe span checks,
    exact query filtering, stable order, removal, reset, negative coordinates,
    and large-query active scans.
- Modify `tests/GridForge.Tests/Grids/GridWorld.Tests.cs`
  - covers huge-grid lifecycle, point lookup, overlap, duplicate configuration,
    stable overlapping-grid selection, and removed implementation APIs.
- Modify `tests/GridForge.Tests/Utility/GridTracer.Tests.cs`
  - covers normal, oversized, mixed-tier, and extreme sparse-span traversal.
- Modify `tests/GridForge.Tests/Grids/VoxelNeighborApiTests.cs`
  - covers hash/hash, hash/BVH, and BVH/BVH neighbor linking and unlinking.
- Create `tests/GridForge.Benchmarks/Memory/GridSpatialIndexBenchmarks.cs`
  - adds only missing mixed-scale, oversized lifecycle, and many-oversized-grid
    evidence; it does not duplicate ordinary benchmark rows already present.
- Reuse `GridRegistrationBenchmarks`, `GridTracerBenchmarks`,
  `Vector2dLookupBenchmarks`, and `NeighborLookupBenchmarks` for ordinary gates.

### Gravitas validation

- Create
  `tests/Gravitas.Tests/MixedDimensions/MixedQueryCcdTests.SparseGridSpan.cs`
  - retains the exact public mixed-query regression without a wall-clock
    assertion.
- Modify `tests/Gravitas.Benchmarks/Queries/MixedQueryBenchmarks.cs`
  - adds one exact extreme sparse-grid-span row and reuses existing mixed-query
    ordinary rows as non-regression evidence.
- Modify `docs/feature-work/benchmark-signal-hardening-backlog.md`
  - records upstream promotion, before/after evidence, and final closure.

### Documentation

- Modify `README.md` and `docs/wiki/GlobalGridManager.md`
  - describe automatic two-tier lookup and optional hash tuning.
- Modify `docs/wiki/GridTracer-and-Coverage.md` and
  `docs/wiki/Testing-and-Benchmarking.md`
  - document candidate scaling and benchmark aliases/artifacts.
- Modify `docs/MIGRATION.md`
  - add the v8-to-v9 removal of direct hash internals and the replacement
    host-facing guidance.
- Modify `docs/feature-work/benchmark-signal-hardening-backlog.md`
  - promote and later close the upstream signal.

## Evidence Protocol

Raw BenchmarkDotNet artifacts are ignored build output. Preserve them locally
and record concise summaries in this plan and the appropriate benchmark backlog.
Never compare different jobs, runtimes, build configurations, filters, or local
dependency graphs as though they were matched samples.

### Required artifact roots

GridForge:

```text
artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline
artifacts/benchmarks/2026-08-03-grid-spatial-index-two-tier-after
artifacts/benchmarks/2026-08-03-grid-spatial-index-confirmation
```

Gravitas:

```text
artifacts/benchmarks/2026-08-03-mixed-sparse-span-baseline
artifacts/benchmarks/2026-08-03-mixed-sparse-span-two-tier-after
artifacts/benchmarks/2026-08-03-mixed-sparse-span-confirmation
```

### GridForge matched benchmark command

Run the same command before implementation, after adoption, and for final
confirmation; change only the `--artifacts` destination:

```powershell
dotnet build tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0

dotnet tests/GridForge.Benchmarks/bin/Release/net8.0/GridForge.Benchmarks.dll `
  grid-registration grid-tracer vector2d-lookup neighbor-lookup grid-spatial-index `
  --filter "*" `
  --exporters json `
  --artifacts "artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline"
```

The benchmark types already apply `InProcessShortRunConfig`. Do not add the
redundant CLI `--job short`: it creates a second out-of-process job, and
BenchmarkDotNet rejects the duplicate benchmark project name while the owner's
ignored `.worktrees/coverage-restoration` worktree is present.

The new benchmark type must exist before capturing the authoritative baseline.
Its safe pre-change scaling rows may stop below the pathological 64-billion-cell
case. The exact pathological baseline is captured as a bounded RED integration
run rather than allowing BenchmarkDotNet to hang indefinitely.

### Bounded RED signal command

After adding the focused GridForge and Gravitas regressions but before changing
production code:

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj `
  --configuration Release `
  --filter "FullyQualifiedName~HugeBounds" `
  --blame-hang-timeout 30s `
  --results-directory "artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline/gridforge-red"

dotnet test tests/Gravitas.Tests/Gravitas.Tests.csproj `
  --configuration Release `
  --filter "FullyQualifiedName~ExtremeSparseGridSpan" `
  --blame-hang-timeout 30s `
  --results-directory "artifacts/benchmarks/2026-08-03-mixed-sparse-span-baseline/gravitas-red"
```

Expected before implementation: the runner terminates the stalled test host and
retains blame/log evidence. Expected afterward: both commands pass normally
without relying on the watchdog.

### Gravitas matched benchmark command

Capture after and confirmation artifacts with the same command:

```powershell
dotnet build tests/Gravitas.Benchmarks/Gravitas.Benchmarks.csproj -c Release -f net8.0

dotnet tests/Gravitas.Benchmarks/bin/Release/net8.0/Gravitas.Benchmarks.dll `
  mixed-query `
  --filter "*SweepCircleAgainst3DAll*" `
  --job short `
  --exporters json `
  --artifacts "artifacts/benchmarks/2026-08-03-mixed-sparse-span-two-tier-after"
```

Do not manufacture a completed pre-change latency for the exact row. Its honest
baseline is the retained 30-second termination plus the safe GridForge scaling
curve. Compare ordinary mixed-query rows before/after and compare the new exact
row between after/confirmation runs.

### Focused workload matrix

The new GridForge benchmark must isolate top-level indexing from voxel
materialization by using sparse grids with empty or one-voxel physical storage
and topology metrics large enough to keep the voxel address space bounded.

| Workload | Inputs | Purpose |
| --- | --- | --- |
| Safe footprint scaling | 1, 4, 8, 16, and 24 top-level hash cells per axis | Show the current volume curve without attempting the 64-billion-cell case |
| Ordinary tiled world | Existing `GridRegistrationBenchmarks`, `Vector2dLookupBenchmarks`, `GridTracerBenchmarks`, and `NeighborLookupBenchmarks` inputs unchanged | Protect the current fast path |
| Mixed scale | 256 ordinary tiled grids plus 8 spatially separated grids spanning at least 17 hash cells per axis | Exercise both tiers in one world |
| Many oversized | 8, 64, and 256 spatially separated oversized grids with point and bounded-region queries that intersect one grid | Demonstrate BVH scaling against the rejected linear scan |
| Exact huge grid | Default 50-unit world hash; bounds `[-100,000, +100,000]`; 100,000-unit rectangular topology cells | Reproduce and close the 64,048,012,001-cell registration failure |
| Exact Gravitas sweep | Start `(-200,000, 0)`, end `(200,000, 0)`, radius `100,000`, layer Y `0`, half-thickness `1`, through the exact huge grid | Close the originating public mixed-query signal |

Use fixed deterministic positions and insertion permutations checked into the
benchmark/test source. Do not generate benchmark layouts from wall-clock or
process-random seeds.

### Acceptance gates

- The exact default-hash huge grid registers, resolves, traverses, and removes
  without work proportional to 64,048,012,001 hash cells.
- The exact Gravitas public sweep completes and returns the expected hit.
- No ordinary tiled registration, removal, point lookup, neighbor lookup,
  bounds coverage, or line-trace row regresses by more than 5% repeatably under
  matched runs.
- Any apparent 5% regression is rerun in an isolated longer job before code is
  changed to chase noise.
- Many-oversized-grid queries demonstrate sublinear candidate discovery versus
  the rejected direct-list design.
- Warmed point, bounds, neighbor, trace, and Gravitas sweep rows allocate `0 B`.
- Candidate order and replay-visible results are stable across repeated
  insertion/removal orders; overlapping point lookup uses the newly explicit
  lowest-live-slot contract.
- GridForge and Gravitas retain 100% reachable line, branch, and method coverage.

If the fixed spatial hash wrapper causes a repeatable ordinary regression over
5%, keep the `GridSpatialIndex` boundary and retain the existing custom hash as
its normal tier; do not abandon the oversized BVH or expose both structures.
If the two-tier design itself cannot meet the gates, revert the production
experiment and record a no-change result rather than shipping complexity on
hope.

## Phase 0: Baseline, RED Regressions, And Threshold Evidence

Intent: make the current failure and ordinary costs reproducible before runtime
behavior changes.

- [x] Record `git status --short` for SwiftCollections, GridForge, and Gravitas;
      preserve the intentional local-link files and all unrelated owner work.
- [x] Confirm the locally linked FixedMathSharp, SwiftCollections, GridForge,
      and Gravitas projects build in `Release` from their direct project files.
- [x] Add `GridSpatialIndexBenchmarks` with safe pre-change grid-footprint
      scaling, mixed-scale point/bounds queries, and many-large-grid scenarios.
- [x] Add the GridForge `HugeBounds` and Gravitas `ExtremeSparseGridSpan`
      regressions before changing production code.
- [x] Run the bounded RED commands and preserve the termination artifacts.
- [x] Capture the matched GridForge baseline artifact.
- [x] Run the current full GridForge and Gravitas tests and record counts.
- [x] Capture current 100% GridForge and Gravitas coverage reports and CRAP
      summaries.
- [x] Record baseline medians, allocations, scaling curve, coverage, and exact
      artifact paths in this plan.

Exit criteria:

- [x] The exact failure is retained as bounded evidence rather than anecdote.
- [x] Ordinary and safe heterogeneous baselines exist before production edits.
- [x] No unexplained failing test or dirty source change is mixed into Phase 1.

### Phase 0 Evidence

Repository state and build gate:

- All four stack repositories were on `develop` on 2026-08-03.
- FixedMathSharp was clean. SwiftCollections, GridForge, and Gravitas retained
  their known unstaged local-link `.csproj`, `.slnx`, and
  `Directory.Build.props` changes. The two benchmark-backlog edits were also
  preserved.
- Direct `Release` builds of `FixedMathSharp.csproj`,
  `SwiftCollections.FixedMathSharp.csproj`, `GridForge.csproj`, and
  `Gravitas.csproj` each completed with zero warnings and zero errors.

Bounded RED evidence:

- GridForge `TryAddGrid_WithHugeBounds_ShouldRegisterResolveAndRemoveAtDefaultSpatialCellSize`
  was terminated after 30 seconds inside `RegisterGridSpatialCells`. The run
  retained sequence, Cobertura/OpenCover, and two hang dumps under
  `artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline/gridforge-red/f35667fe-3719-4996-8287-0af9382175e7/`.
- Gravitas `SweepCircleAgainst3DAll_WithExtremeSparseGridSpan_ShouldReturnExpectedHit`
  was independently terminated after 30 seconds during the same upstream grid
  registration. It retained sequence and two hang dumps under
  `artifacts/benchmarks/2026-08-03-mixed-sparse-span-baseline/gravitas-red/a1401294-fb82-4a97-8090-75fcc73ccb2f/`.
- The downstream run never reached mixed narrow phase. This confirms that the
  public Gravitas signal is an upstream GridForge registration-scale defect.

Authoritative matched benchmark artifact:

- Command: the matched command above, using the repository-owned in-process
  ShortRun job and no redundant CLI job.
- Runtime: .NET 8.0.28, BenchmarkDotNet 0.15.8, Intel Core i7-9700K, Windows 11.
- Run log:
  `artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline/BenchmarkRun-20260803-162641.log`.
- Machine-readable and rendered results:
  `artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline/results/`.
- Result: 40 of 40 benchmark cases completed in 1 minute 49 seconds.

Safe registration footprint scaling at the default 50-unit hash:

| Hash cells per axis | Cell visits | Median | Allocated |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 14.4 us | 712 B |
| 4 | 64 | 46.85 us | 17,368 B |
| 8 | 512 | 175.9 us | 164,872 B |
| 16 | 4,096 | 913.2 us | 1,049,632 B |
| 24 | 13,824 | 2.4329 ms | 3,703,864 B |

The curve and allocation growth follow hash-cell volume, not active-grid count.
The exact 4,001-cells-per-axis case was therefore retained only as the bounded
RED run.

`SpatialGridCellSize` sensitivity for one fixed 800-unit grid:

| Cell size | Effective cells per axis | Median | Allocated |
| ---: | ---: | ---: | ---: |
| 25 | 33 | 8.5778 ms | 10,905,232 B |
| 50 | 17 | 257.3 us | 1,206,496 B |
| 100 | 9 | 55.6 us | 206,536 B |
| 200 | 5 | 20.9 us | 40,432 B |

Larger global cells materially reduce registration cost for that one scale, but
no single value preserves fine ordinary lookup resolution while safely handling
arbitrarily large streamed grids. Keep `SpatialGridCellSize` optional; do not
turn it into the correctness mechanism.

Mixed and many-large query baselines:

| Scenario | Median | Allocated |
| --- | ---: | ---: |
| Mixed 256 ordinary + 8 large, point | 22.423 ns | 0 B |
| Mixed 256 ordinary + 8 large, bounds | 274.258 ns | 448 B |
| 8 large, point / bounds | 24.025 ns / 269.778 ns | 0 B / 448 B |
| 64 large, point / bounds | 22.576 ns / 270.909 ns | 0 B / 448 B |
| 256 large, point / bounds | 22.290 ns / 271.527 ns | 0 B / 448 B |

The current hash keeps one-cell point and bounds probes constant as active-grid
count grows; the bounds API creates a 448-byte result set. These rows isolate
top-level candidate discovery without measuring a large query volume. The later
BVH evidence must retain the point fast path, remove warmed bounds allocation,
and demonstrate sublinear candidate discovery as the number of oversized grids
grows.

Representative ordinary guard baselines are retained in the same artifact:
adjacent registration/removal medians were 2.0733 ms / 1.7043 ms; warmed line
trace was 77.1 us with 1,704 B; warmed bounds coverage was 343.9 us with
1,760 B; Vector2d/Vector3d voxel lookup medians were 170.2 us / 146.0 us with
1,296 B. All ordinary rows in the artifact remain comparison gates, including
neighbor families rather than only these concise representatives.

Tests and coverage:

- GridForge: 504 existing tests passed with the one intentional RED excluded.
  ReportGenerator recorded 100% line (5,254/5,254), branch
  (2,367/2,367), and method (845/845) coverage. CRAP analysis found zero
  methods above 30. Raw coverage is under
  `artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline/gridforge-full/830c4b8a-5968-4fc2-9964-6dd0db1cf7bc/`;
  rendered reports are under
  `TestResults/coverage-analysis/phase0-grid-spatial-index/`.
- Gravitas: 3,928 existing tests passed with the one intentional RED excluded.
  ReportGenerator recorded 100% line (55,869/55,869), branch
  (15,833/15,833), and method (5,321/5,321) coverage. CRAP analysis found
  27 complexity-only scores above 30, all at 100% coverage and therefore no
  coverage gap. Raw coverage is under
  `artifacts/benchmarks/2026-08-03-mixed-sparse-span-baseline/gravitas-coverage/4b420c8b-e04e-4304-93ae-9204cf23b6af/`;
  rendered reports are under
  `TestResults/coverage-analysis/phase0-grid-spatial-index/`.

## Phase 1: Internal Two-Tier Index Foundation

Intent: introduce the smallest reusable owner for the approved architecture
without changing every caller at once.

- [x] Write focused tests for exact cell-range classification at positive,
      negative, reversed, zero-volume, threshold-minus-one, threshold,
      threshold-plus-one, and full-domain bounds.
- [x] Prove the cell-budget comparison cannot overflow when all three spans are
      near the complete signed cell-coordinate range.
- [x] Add `GridSpatialIndex` with one `SwiftFixedSpatialHash<ushort>`, one
      `SwiftFixedBVH<ushort>`, and no interface/factory/strategy hierarchy.
- [x] Add mutually exclusive insert/remove/clear behavior and assert through
      tests that a slot cannot survive in both tiers or neither tier.
- [x] Add bounds-based candidate collection with exact filtering, the
      overflow-safe active-grid scan decision, and final ascending slot sort.
- [x] Measure threshold candidates 64, 512, and 4,096 under the new focused
      benchmark rows; apply the selection rule and delete experimental values.
- [x] Verify first-use and warmed allocations separately. Do not change
      SwiftCollections merely to make a cold benchmark column read zero.
- [x] Run focused tests, full GridForge tests, and 100% GridForge coverage.
- [x] Request an independent review of classification arithmetic, fixed-volume
      semantics, deterministic ordering, and collection ownership.
- [x] Record the selected threshold and Phase 1 evidence in this plan.

Exit criteria:

- [x] The internal index contract is complete and independently testable.
- [x] Threshold selection is measured, internal, and documented.
- [x] No public GridWorld behavior has been migrated piecemeal.
- [x] GridForge remains at 100% coverage.

### Phase 1 Evidence

The internal owner contains only the two approved collections plus one
oversized-slot membership set required because `SwiftFixedBVH` permits duplicate
keys. Inserts are tier-exclusive, removal follows recorded membership, queries
exact-filter both tiers or the active-grid scan, and every result is sorted by
ascending `GridIndex`.

The full-domain regression exposed a real upstream boundary defect rather than a
GridForge classification exception: `SwiftSpatialHash` used `int` loop counters,
so insert/query/remove could wrap at `int.MaxValue`, while padded queries could
overflow before iteration. The bounded RED run terminated after five seconds and
retained its sequence under
`../SwiftCollections/artifacts/phase1-spatial-hash-red/`. The shared Swift loop
owner now uses `long` counters, clamps padded ranges to the signed cell domain,
and casts only each representable cell coordinate. The exact maximum-cell
signed-minimum and signed-maximum insert/query/neighborhood/remove regressions
pass, as do all 1,098 SwiftCollections Release tests and 1,070 ReleaseLean
tests. No downstream boundary routing workaround remains.

Threshold evidence used 24 deterministic entries: eight each at 64, 512, and
4,096 hash cells. All warmed query candidates allocated `0 B`; the separate cold
owner row includes construction, first BVH scratch where applicable, insertion,
and one query.

| Cell budget | Warmed mixed query | Cold maximum candidate | Warmed mixed lifecycle per entry |
| ---: | ---: | ---: | ---: |
| 64 | 372.9 ns / 0 B | 1.532 us / 4,560 B | 2.795 us / 3,064 B |
| 512 | 318.7 ns / 0 B | 1.502 us / 4,560 B | 22.305 us / 24,931 B |
| 4,096 | 293.4 ns / 0 B | 1.372 ms / 1,096,220 B | 300.317 us / 199,723 B |

Budget 4,096 is rejected by unsafe hash registration growth. Budget 512 saves
54.2 ns per warmed scale probe relative to 64, but makes mixed lifecycle
work about eight times slower. Budget 64 therefore has the strongest mixed
geometric result and is retained as the sole internal default. The experimental
values remain only as benchmark parameters.

The nine-case ShortRun completed in 1 minute 4 seconds. Machine-readable and
rendered artifacts are under
`artifacts/benchmarks/2026-08-03-grid-spatial-index-phase1-threshold/`.

Verification:

- 29 focused `GridSpatialIndex` cases passed.
- 533 GridForge tests passed with the intentional Phase 0 `HugeBounds` RED held
  out until public adoption in Phase 2.
- ReportGenerator recorded 100% line (5,328/5,328), branch (2,393/2,393), and
  method (859/859) coverage under
  `TestResults/coverage-analysis/phase1-grid-spatial-index/`.
- Independent review found only the missing symmetric signed-minimum Swift hash
  regression. That case was added and both Swift package variants were
  reverified; no other finding remains.

## Phase 2: GridWorld Lifecycle, Lookup, And Neighbor Adoption

Intent: make the two-tier owner authoritative for all top-level grid lifecycle
and direct world queries.

- [x] Replace `RegisterGridSpatialCells` with neighbor-candidate collection
      followed by exact linking and one-tier insertion.
- [x] Replace `UnregisterGridSpatialCells` with exact neighbor unlinking and
      removal from the recorded tier before the grid is returned to its pool.
- [x] Route `TryGetGrid(position)`, `FindOverlappingGrids`, reset, and dispose
      through `GridSpatialIndex`.
- [x] Resolve overlapping containing grids by ascending grid slot and add
      insertion/removal-order regressions for that contract.
- [x] Cover hash/hash, hash/BVH, and BVH/BVH link/unlink pairs for rectangular,
      hex, dense, and sparse grid combinations where topology permits contact.
- [x] Ensure duplicate-configuration rejection and failed registration do not
      leak a tier entry or neighbor link.
- [x] Remove `SpatialGridHash`, `GetSpatialGridCells`, and `GetSpatialGridKey`
      from the public surface and delete tests that only existed to mutate stale
      implementation buckets.
- [x] Remove the now-duplicate public `VoxelGrid.IsGridOverlapValid(...)`
      predicate rather than retain a second expanded-bound authority.
- [x] Keep `SpatialGridCellSize` and its constructor validation unchanged.
- [x] Run focused tests, full GridForge tests, 100% GridForge coverage, and the
      first matched `two-tier-after` benchmark artifact.
- [x] Pause for owner review before further traversal hardening.

Exit criteria:

- [x] Huge-grid registration/removal no longer enumerates its hash-cell volume.
- [ ] Ordinary point and lifecycle behavior meets the 5% and allocation gates.
- [x] All neighbor relationships survive cross-tier registration and removal.
- [x] GridForge remains at 100% coverage.

### Phase 2 Completion Evidence

`GridWorld` now inserts each live grid into one recorded tier before neighbor
linking, removes it from that tier before unlinking its recorded reciprocal
neighbors, and clears the index before returning pooled grids during reset.
Point and overlap queries use exact two-tier candidates sorted by grid slot, so
hash order, BVH shape, removal history, and pooled slot reuse cannot change the
winner. The old public hash/key/cell surface and tests that corrupted its
implementation buckets were deleted rather than preserved as facades.

The adoption exposed two policy-neutral SwiftCollections defects and fixed them
at their shared owners: fixed spatial-cell mapping now floors exact Q32.32 raw
ratios across negative and fractional coordinates, and spatial cell hashes now
use deterministic `SwiftHashTools` combination rather than
`System.HashCode.Combine`. `SwiftFixedSpatialHash` exposes that one exact cell
mapping authority plus the broad-phase point candidate operation required by
GridForge; the experimented exact-query and collection-specialized forwarders
were deleted when they added no retained value.

Matched ShortRun evidence under
`artifacts/benchmarks/2026-08-03-grid-spatial-index-two-tier-phase2-accepted/`
shows the retained architecture removes the catastrophic footprint curve and
keeps its new point-index paths allocation-free:

| Workload | Baseline | Phase 2 | Direction |
| --- | ---: | ---: | ---: |
| Register 64 adjacent grids | 2.078 ms / 1,099.95 KB | 1.862 ms / 1,106.05 KB | -10.4% time; +0.6% allocation |
| Remove 64 adjacent grids | 1.708 ms / 25.22 KB | 1.170 ms / 32.93 KB | -31.5% time; +7.71 KB per batch |
| Mixed-scale point lookup | n/a | 91.67 ns / 0 B | New two-tier row |
| Point lookup, 8/64/256 oversized grids | n/a | 90.21/129.41/151.98 ns / 0 B | Sublinear BVH growth |

The ordinary `TryGetVoxel` ShortRun rows were directionally 7.8% slower for 3D
and 12.9% slower for 2D, but each benchmark iteration is only about 0.15-0.20
ms and BenchmarkDotNet reports confidence ranges too broad to establish a
repeatable regression. The removal allocation increase is also retained as an
explicit signal. Phase 4 must run isolated longer confirmation before either is
accepted or optimized; the unchecked performance exit criterion above is
intentional.

Verification:

- 537/537 GridForge Release and 537/537 ReleaseLean tests passed.
- Release and ReleaseLean both report 5,166/5,166 lines and 2,323/2,323
  branches covered; method coverage remains complete.
- 1,106/1,106 SwiftCollections Release and 1,078/1,078 ReleaseLean tests
  passed. Every changed spatial-hash file reports 100% reachable line and
  branch coverage; its unrelated repository-wide 97% restoration remains
  owner-led.
- The Release benchmark project builds with zero warnings and errors.

## Phase 3: Traversal, Coverage, Scan, And Neighbor-Resolver Adoption

Intent: remove the remaining hash-specific query machinery and prove both large
grid bounds and large query bounds scale safely.

- [x] Change GridTracer coverage and line workers to pass expanded world-space
      candidate bounds directly to the shared index.
- [x] Change `VoxelNeighborResolver` to the same bounds-based candidate API.
- [x] Preserve `MaxTopologyCellEdge` expansion before index collection and exact
      topology/voxel filtering afterward.
- [x] Remove `ProcessedGrids` from `GridTraceScratch` and `GridScanScratch` once
      no caller needs it.
- [x] Delete `GridCandidateDiscovery` and all obsolete cell-bound/hash-key
      forwarding methods after the final caller moves.
- [x] Add repeated-order regressions for coverage, line tracing, scans, and
      mixed-topology neighbor queries across both tiers.
- [x] Add a large-query/small-active-world regression proving the active-grid
      scan wins without iterating the empty hash volume.
- [x] Run focused tests, full GridForge tests, 100% GridForge coverage, and
      allocation guards.
- [x] Request an independent zombie-code and deterministic-order review.
- [x] Pause for owner review.

Exit criteria:

- [x] There is one candidate-discovery implementation in GridForge.
- [x] Neither grid extent nor query extent forces empty-world cell traversal.
- [x] Scratch objects contain only state still required by reachable code.
- [x] GridForge remains at 100% coverage.

### Phase 3 Completion Evidence

The Phase 2 mechanical adoption already routed line tracing, voxel coverage,
scan-cell coverage, and contact-neighbor discovery through the shared index.
Phase 3 verified those callers rather than duplicating another adapter. Existing
public regressions cover a 96,000-unit query and a one-million-cell sparse span,
proving the active-grid scan filters exact bounds without enumerating empty hash
volume.

The independent audit found one remaining deterministic-order defect after the
index: enumerable line and voxel-coverage APIs inserted sorted candidates into
a `SwiftDictionary<VoxelGrid, SwiftList<Voxel>>`, then emitted hash-table order.
The new cross-tier regression went RED with expected slots `0,1,2,3...` but
actual order `0,7,6,2,5...`. Both enumerable paths now append pooled
`GridVoxelSet` values directly in candidate order. This deletes the obsolete
hash regrouping, preserves enumerable/caller-owned parity through slot reuse,
and avoids sorting a second time.

Coverage then exposed `VoxelGrid.GetHashCode()` as reachable only through that
deleted dictionary. The override hashed mutable pooled-grid fields without a
matching equality contract, so it and its unused `SwiftHashTools` dependency
were deleted instead of receiving a hollow test.

Verification:

- Cross-tier line, voxel-coverage, scan-cell, and mixed-topology neighbor order
  remain ascending after removal and pooled slot reuse.
- Warmed caller-owned line, voxel-coverage, scan-cell, and mixed-neighbor paths
  allocate `0 B` in focused guards.
- 541/541 GridForge Release and 541/541 ReleaseLean tests passed.
- Both configurations report 5,163/5,163 lines and 2,323/2,323 branches
  covered; all 847 reachable methods are covered.
- CRAP analysis reports zero methods above 30; the highest score is 28 at 100%
  coverage. Reports are retained under
  `tests/GridForge.Tests/TestResults/coverage-analysis/phase3/`.

## Phase 4: GridForge Performance Confirmation And API Documentation

Intent: decide from matched evidence whether the implementation deserves to
ship, then document only the retained design.

- [ ] Capture the `grid-spatial-index-two-tier-after` artifact using the exact
      baseline command and runtime.
- [ ] Isolate any apparent ordinary regression over 5% with a longer matched
      job; optimize only a confirmed hotspot.
- [ ] Capture the independent `grid-spatial-index-confirmation` artifact after
      source and machine state stabilize.
- [ ] Compare medians, distributions, allocations, and scaling rather than one
      favorable mean.
- [ ] Confirm many-oversized-grid lookup remains sublinear and no direct-list
      fallback survived.
- [ ] Update README/wiki architecture and testing guidance.
- [ ] Add the concise v8-to-v9 migration note for removed hash implementation
      APIs and optional cell-size tuning.
- [ ] Update GridForge's benchmark backlog with the measured before/after table
      and retained artifact paths.
- [ ] Run GridForge Debug, Release, ReleaseLean, package, and 100% coverage gates.
- [ ] Pause for owner review before downstream closure.

Exit criteria:

- [ ] The two-tier implementation passes every acceptance and rollback gate.
- [ ] Documentation describes behavior and tuning, not internal collection
      trivia that hosts must reproduce.
- [ ] GridForge remains at 100% coverage in its retained source shape.

## Phase 5: Gravitas Signal Closure And Cross-Stack Release Gates

Intent: prove the upstream fix resolves the actual public physics workload and
does not shift cost or nondeterminism downstream.

- [ ] Run the retained `ExtremeSparseGridSpan` Gravitas regression without the
      hang watchdog and verify the exact public hit.
- [ ] Capture `mixed-sparse-span-two-tier-after` and
      `mixed-sparse-span-confirmation` artifacts with matched commands.
- [ ] Compare all existing `SweepCircleAgainst3DAll` rows, not only the new
      favorable extreme row.
- [ ] Verify warmed mixed public sweeps allocate `0 B` and preserve candidate
      count/result order.
- [ ] Run focused GridForge-backed Gravitas partition, query, 2D, 3D, mixed,
      CCD, replay, and allocation tests.
- [ ] Run full Gravitas Debug, Release, ReleaseLean, package, and 100% coverage
      gates.
- [ ] Run final GridForge coverage after any downstream-discovered correction.
- [ ] Update the Gravitas benchmark backlog from `Promoted` to `Closed` with the
      RCA, matched evidence, and upstream release dependency.
- [ ] Request independent final reviews for GridForge architecture/performance
      and Gravitas end-to-end correctness.
- [ ] Record final test counts, coverage, artifact paths, benchmark comparison,
      review outcomes, and any intentionally deferred signal in this plan.
- [ ] Mark this plan complete and move it to `docs/feature-work/done` only after
      owner review.

Exit criteria:

- [ ] The original Gravitas signal is closed by an upstream GridForge fix with
      no Gravitas band-aid.
- [ ] GridForge and Gravitas both retain 100% coverage.
- [ ] Standard, Lean, local-link, and package-consumption gates pass.
- [ ] The final source is smaller or more cohesive than the combined bespoke
      hash, adaptive discovery, and oversized workaround it replaces.

## Release And Commit Guidance

The implementation removes released public GridForge v8 APIs, so the coherent
GridForge implementation commit should include `+semver:breaking`. Do not add
the semver marker to planning-only documentation commits unless the owner wants
the plan committed with the runtime change.

Release order remains:

1. SwiftCollections only if a proven upstream defect required a change.
2. GridForge with the two-tier index and v9 migration note.
3. Gravitas after replacing local links with the released GridForge package and
   repeating the package-only validation.

## Progress Log

| Date | Phase | Summary |
| --- | --- | --- |
| 2026-08-03 | Planning | Confirmed registration-scale root cause, rejected full-BVH and dynamic-global-hash designs, selected the evidence-gated fixed hash plus fixed BVH architecture, and locked before/after artifact and rollback requirements. |
| 2026-08-03 | Phase 0 | Added the two exact RED regressions and bounded hang artifacts, captured the 40-case matched GridForge baseline including cell-size sensitivity, verified 504 GridForge and 3,928 Gravitas existing tests, and recorded fresh 100% line/branch/method coverage plus CRAP summaries. No production behavior changed. |
| 2026-08-03 | Phase 1 | Added the internal exclusive hash/BVH owner, selected a 64-cell default from nine measured threshold cases, corrected SwiftCollections' shared signed-cell loop overflow instead of routing around it downstream, passed 29 focused and 533 full GridForge tests plus all 1,098 SwiftCollections Release and 1,070 ReleaseLean tests, and retained 100% GridForge coverage. Public `GridWorld` behavior remains unchanged pending Phase 2. |
| 2026-08-03 | Phase 2 | Made the two-tier owner authoritative for lifecycle, point/overlap lookup, neighbors, and mechanically dependent traversal callers; removed the public single-hash surface plus dead adaptive/deduplication machinery; corrected exact fixed cell flooring and deterministic cell hashes upstream; passed 537 GridForge tests in Release and ReleaseLean plus 1,106 SwiftCollections Release and 1,078 ReleaseLean tests; and retained 100% GridForge and touched-Swift spatial-hash coverage. Longer ordinary-query and removal-allocation confirmation remains the explicit Phase 4 gate. |
| 2026-08-03 | Phase 3 | Proved large-query active scanning and cross-tier traversal/neighbor order, replaced nondeterministic enumerable hash regrouping with ordered pooled `GridVoxelSet` storage, deleted the resulting mutable `VoxelGrid.GetHashCode()` zombie, added warmed zero-allocation guards, passed 541 GridForge tests in Release and ReleaseLean, retained 100% line/branch/method coverage, and recorded zero CRAP hotspots above 30. |
