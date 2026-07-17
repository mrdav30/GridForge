# Issue Tracker

## Tracker Rules

- Add new items when feature work uncovers a suspected bug, stale doc, test
  smell, performance anomaly, or correctness risk.
- Keep each item scoped tightly enough to fix and verify independently.
- Record the date on the item, not in this filename.
- Move an item to `Resolved Issues` only after the fix has tests or documented
  verification evidence.
- Do not use this tracker as a substitute for tests, benchmarks, or release
  notes.
- Performance issues should stay in
  [`benchmark-signal-hardening-backlog.md`](benchmark-signal-hardening-backlog.md)
  unless they become a confirmed runtime defect. Do not add performance issues
  here until they have been investigated and confirmed as runtime defects.

## Active Issues

### 3. Recycled Occupant Tickets Can Resolve Replacement Occupants

Status: planned on 2026-07-17.

Source: runtime identity audit following the pooled-grid generation defect.

Plan:

- `docs/feature-work/2026-07-17-runtime-identity-hardening-plan.md`, Phase 4.

Concern:

The public occupant ticket is currently only a `SwiftBucket` slot. Bucket slots
are reused immediately, so a ticket retained after removal can resolve or remove
a different occupant registration later assigned to that slot.

Required outcome:

- Replace the raw slot contract with a generation-aware occupant ticket.
- Validate both slot and generation during lookup and removal.
- Cover different-occupant replacement, same-occupant re-registration, pooling,
  reset, and callback-failure cleanup.

## Performance Investigation Queue

Performance issues should stay in the benchmark plan unless they become a
confirmed runtime defect. Current queue:

- None currently.

## Resolved Issues

### 2026-07-17: GridForge Reused Grid Spawn Tokens Across Pooled Generations

Status: resolved on 2026-07-17.

Source: Gravitas release-hardening investigation.

Concern:

World and grid allocation identity was derived from structural hashes. An
identical grid remove/re-add could reuse the slot and token, allowing a stale
`WorldVoxelIndex` to resolve replacement state; object hashes were also used as
unique traversal identity.

Resolution:

- Added process-unique 64-bit world identity and world-local nonrepeating grid
  generations, preserved across non-deactivating reset.
- Widened identity carriers and kept lookup O(1) by slot plus exact generation.
- Changed traversal and Gravitas query deduplication to exact
  `WorldVoxelIndex`, and removed hash-derived voxel/scan-cell token APIs.
- Fixed the shared SwiftCollections Debug value-key boxing exposed by the wider
  exact key without adding a GridForge or Gravitas workaround.

Verification:

- GridForge identical configuration, pooled reuse, cross-world, reset, hash
  collision, duplicate, and allocation regressions pass.
- Gravitas same-configuration replacement passes in 2D, 3D, and mixed modes;
  its focused identity/query/order suite passed `159/159`.
- Independent review found no unresolved code, determinism, performance,
  benchmark, or test-quality blockers.

### 2026-07-17: Identical-Bounds Blockers Shared One Registration Identity

Status: resolved on 2026-07-17.

Source: runtime identity audit following the pooled-grid generation defect.

Concern:

`BoundsKey` correctly described exact geometry, but `Blocker` also used it as a
supposedly unique blockage token. Two distinct blockers with identical bounds
therefore collapsed into one voxel obstacle entry and could not stack or be
removed independently.

Resolution:

- Added opaque process-unique `ObstacleToken` registration identities,
  allocated through active worlds, and kept `BoundsKey` as geometry only.
- Migrated blocker, direct obstacle, voxel tracker, and event paths to the exact
  token contract.
- Preserved one token across dynamic grid and sparse-voxel reconciliation while
  issuing a fresh token for each later explicit apply lifetime.
- Kept rollback exact to the registration that performed the mutation and
  moved the per-voxel maximum-count recheck inside the existing obstacle lock.

Verification:

- Dense and sparse same-bounds blockers stack and remove independently with
  both cached and retraced coverage.
- Dynamic replacement, sparse reconciliation, explicit reapply, rollback,
  default-token, reset, event, concurrent capacity, and cross-world isolation
  regressions pass.
- Full Debug suite: `444/444` passed.
- Independent re-review reported no findings.

### 2026-06-14: Coverlet Branch Instrumentation Guard-Target Misses

Status: resolved on 2026-06-14.

Source: release coverage hardening pass.

Affected files:

- `src/GridForge/Blockers/Blocker.cs`
- Guard-heavy runtime paths in `src/GridForge/Grids`, `src/GridForge/Utility`,
  and `src/GridForge/Configuration`.
- Closest matching tests under `tests/GridForge.Tests`.
- `docs/complexity-exceptions.md`

Concern:

The initial 2026-06-14 coverage run reached 100% line, method, and full-method
coverage with zero CRAP scores above 30, but Coverlet still reported 97.0%
branch coverage (`1882/1939`) with 57 uncovered branch points. The remaining
branch points were concentrated on tested guard/log targets and short-circuit
targets such as inactive-world guards, duplicate/invalid input warnings,
topology factory warnings, blocker watcher no-ops, sparse storage pruning, and
trace de-duplication.

Resolution:

- Removed a dead blocker cache-allocation branch and simplified several
  short-circuit hot-path guards into sequential checks.
- Trimmed a dead nullable scan-cell occupant map path after the occupied-cell
  invariant is established.
- Added focused diagnostics-enabled and diagnostics-disabled tests for guard
  logging paths so interpolated diagnostic handlers are covered without changing
  runtime logging defaults.
- Added focused tests for stale scan-cell state, sparse storage miss classes,
  closest-voxel tie comparison, topology normalization clamps, trace
  de-duplication, hex coverage boundaries, and neighbor resolver guard paths.
- Updated `docs/complexity-exceptions.md` for the current >10 complexity list.

Verification:

```bash
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Debug --settings tests/GridForge.Tests/coverlet.runsettings --results-directory TestResults/coverage-analysis/current/raw --collect:"XPlat Code Coverage"
pwsh -NoProfile -File /mnt/c/Users/david/.codex/skills/coverage-analysis/scripts/Compute-CrapScores.ps1 -CoberturaPath TestResults/coverage-analysis/current/raw/84fddac1-5b69-47d9-b472-420ea8940f5b/coverage.cobertura.xml -CrapThreshold 30 -TopN 20
```

Evidence:

- Coverage report:
  `TestResults/coverage-analysis/current/raw/84fddac1-5b69-47d9-b472-420ea8940f5b/coverage.cobertura.xml`
- Result: `line-rate 1`, `branch-rate 1`, `5269/5269` lines, `1943/1943`
  branches.
- CRAP result: `TOTAL_METHODS:695`, `FLAGGED_METHODS:0`.

### 2026-06-14: Direction Utility Arrays Are Public And Mutable

Status: resolved on 2026-06-14.

Source: feature-roadmap implementation review.

Affected files:

- `src/GridForge/Spatial/RectangularDirectionUtility.cs`
- `src/GridForge/Spatial/HexDirectionUtility.cs`
- `tests/GridForge.Tests/Grids/VoxelNeighborApiTests.cs`
- `tests/GridForge.Tests/Grids/Voxel.Tests.cs`
- `tests/GridForge.Tests/Grids/HexPrismGrid.Tests.cs`

Concern:

`RectangularDirectionUtility` and `HexDirectionUtility` exposed direction sets
as public `static readonly` arrays. The field references were readonly, but
array contents remained mutable. Because topology code reads those direction
sets for neighbor slot counts, offsets, boundary ranges, and hex slot
resolution, consumer code could accidentally corrupt core neighbor behavior
process-wide.

Resolution:

- Replaced the public mutable array fields with allocation-free
  `ReadOnlySpan<T>` properties backed by private arrays.
- Kept runtime lookup paths indexed and deterministic without exposing mutable
  global array references.
- Added a reflection regression test that rejects public static array fields or
  properties on both direction utility types.
- Scanned `src/GridForge` and `tests/GridForge.Tests` for similar public mutable
  array exposure; no additional public array fields or array-returning
  properties were found.

Verification:

```bash
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Debug --filter "FullyQualifiedName~DirectionUtilities_ShouldNotExposeMutablePublicArrayMembers"
dotnet test GridForge.slnx --configuration Debug --filter "DirectionUtility|Neighbor|HexPrismGrid|VoxelGrid"
dotnet build GridForge.slnx --configuration ReleaseLean
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
dotnet test GridForge.slnx --configuration Debug --no-build
```
