# Issue Tracker

## Tracker Rules

- Issue IDs use `GF-Issue-NNN`. The next available ID is `GF-Issue-009`.
- Assign an ID when an issue enters this tracker, keep it through resolution,
  and never reuse an ID even if an entry is later removed. Check this file's Git
  history before advancing or repairing the counter.
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

### GF-Issue-006 - Planar strict-miss benchmark launch reported a null-reference exception

- **Discovered:** 2026-09-11 during `GF-Benchmark-005` baseline measurement.
- **Status:** Open; root cause is not established. This is an incomplete
  benchmark capture, not a confirmed GridForge runtime defect.
- **Failure:** `GridPlanarIntervalBenchmarks.StrictMiss(Topology: "PointyHex")`,
  launch 2, throws `NullReferenceException` after seven warmup and 13 actual
  iterations. The stack identifies generated
  `Runnable_5.WorkloadActionUnroll` at `.notcs:1287`, the fifteenth of sixteen
  identical `consumer.Consume(workloadDelegate())` calls. The benchmark process
  exits -1; its third launch is not executed. Other cases complete.
- **Source boundary:** GridForge `b3e60f3`, FixedMathSharp `e8a2ab5`, and the new
  benchmark fixture, before the runtime edge guard. The pointy-hex query is a
  horizontal line one raw unit above the top vertex. Its normal strict-miss
  path returns before point containment and before the proposed edge loop.
  The benchmark's geometry fields are value types written only by setup.
  The generated constructor initializes its delegate and consumer, with no
  later reassignment. These observations do not identify the failing object.
- **Evidence:** The full log, JSON and actual executed child/source/hash
  snapshots are retained in the coordinating Trailblazer checkout under
  `artifacts/benchmark005/planar-edge-baseline*`. The capture has 26 child
  executions, 25 successful exits and one failed exit. Its partial pointy-hex
  miss statistics must not be presented as a complete three-launch result.
- **Bounded follow-up:** Two direct executions of the same frozen child with
  `--benchmarkId 5` both complete successfully with zero measured allocation.
  Their logs are `planar-strictmiss-frozen-repro-{1,2}.log` beside the capture.
  These use the generated runner's console host instead of BenchmarkDotNet's
  parent-driven host; they neither complete the original three-launch baseline
  nor establish a fix. The exception remains unresolved.
- **Candidate watch:** The separate edge-guard candidate capture completes all
  nine cases / 27 child launches, including all three pointy-hex strict-miss
  launches. The runtime guard was then withdrawn after mixed performance results.
  This unchanged strict-miss path and its clean follow-up do not explain or
  resolve the original failure; no exception workaround is retained.
- **Next check:** If it recurs, capture the exception state before changing
  code. Do not add null guards,
  retries, disable runtime optimization or change the geometry to conceal it.
- **Coordination:** Trailblazer `TRB-Issue-119` retains a separate A* setup
  exception with a different reported stack. No common cause or fix is proven.

#### Focused investigation - 2026-09-11

The frozen child still matches all **64 original file hashes**. Source and IL
inspection found no nullable reference or retained-stack lifetime defect in
the normal strict-miss geometry path: the prism stores copied value fields,
its six vertices lie strictly below this ray, and it returns before containment
or segment intersection. The generated runner's consumer and delegate are
initialized and rooted. Its wrapper requests `AggressiveOptimization`; the
late failure is not evidence that this wrapper entered a new tier at that
iteration. Callees can compile independently. The optimized source line does
not identify the faulting object or native instruction.

Additional unchanged-child probes on Windows x64 / .NET 8.0.29:

| Probe | Child exits | Actual iterations | Result rows | Outcome |
| --- | --- | --- | --- | --- |
| Console host, name-only exception collector | 1/1 zero | 15 | 15 | No recurrence; collector limitation below |
| Custom acknowledged-pipe parent | 3/3 zero | 15 / 15 / 15 | 14 / 15 / 15 | All four lifecycle signals complete |
| Same pipe parent, validated collector attached before first acknowledgement | 3/3 zero | 15 / 15 / 15 | 13 / 15 / 15 | Complete lifecycle; no matching fault or dump |

Fewer result rows reflect BenchmarkDotNet's outlier filtering, not a missing
launch. All seven probes report zero measured allocation. These are diagnostic
replays, **not** replacement launches for the failed baseline, throughput
comparisons, or evidence of a fix. The custom parent preserves the exact child,
benchmark ID 5, benchmark name, job arguments and pipe acknowledgement order,
requests High priority, and verifies hashes after each run. It is not the
original BenchmarkDotNet parent: scheduling, redirected stderr and diagnoser
callbacks differ; captured runs additionally change debugger/handshake timing.
No production code, runtime optimization setting or dependency was changed.

Evidence is local in the coordinating Trailblazer checkout under
`artifacts/gf-issue006/`: `console-firstchance-1.log`,
`pipe-normal-1/run-{1,2,3}` and `pipe-capture-1/run-{1,2,3}`. Each pipe run retains
arguments, PID, exit, priority/affinity, lifecycle, logs and a hash manifest.
`pipe-replay/README.md` documents the ignored diagnostic harness and its limits.
Independent source and evidence reviews found no justified runtime patch.

**Capture blind spot established by a separate positive control:**
BenchmarkDotNet's generated program catches and prints the exception, so
unhandled-only collection is inadequate. An isolated net8.0 program deliberately
dereferenced null and caught the resulting exception without loading GridForge.
On this host, ProcDump 12.01 with only `*NullReferenceException*` captured nothing;
including native `C0000005` captured the fault before managed exception creation.
Dump analysis identifies the control's `Program.Touch` frame, fault instruction
address and zero registers. This is a collector validation, **not a reproduction
or crash dump of GF-Issue-006**.

For the next fault capture, use the validated first-chance filter
`-ma -e 1 -f 'C0000005,*NullReferenceException*'`. With the frozen pipe harness,
read each new run's `attach-ready.json`, attach to that exact PID, verify the
collector's monitoring-ready message, then create that run's `continue` file.
The harness has a 180-second per-child budget, stops on failure and never retries.
Do not interpret the collector's own exit code as the benchmark child's status.
Use native fault context plus disassembly/registers, runner/consumer/delegate
references, prism inputs and loaded-module identities to identify the failing
operation before changing code. A matching native event can also be handled
internally, so correlate any dump with the child's exception log and stack.
The one-dump collector stops after its first match. If that event is unrelated,
the remaining execution is no longer covered; inspect the dump before arranging
another bounded capture, and never retry a failed child to erase its failure.
The smoke logs and analyzed control dump remain in `capture-smoke*`; full dumps
stay local and private, with no global debugger/WER registration or upload.
See [ProcDump's capture options](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump).

**Disposition:** Remains open, awaiting a captured recurrence. No confirmed
GridForge, FixedMathSharp, BenchmarkDotNet, runtime or hardware cause was found.
Do not spend further runs treating successful replays as a fix. The separately
confirmed launcher-status defect (`GF-Issue-007`) is now resolved below; that
correction neither explains this exception nor completes its failed capture.
Keep the failed case excluded from comparisons and retain full-log validation.

## Performance Investigation Queue

Performance issues should stay in the benchmark plan unless they become a
confirmed runtime defect. Current queue:

- None currently.

## Resolved Issues

### GF-Issue-008 - Generated local-stack benchmark builds rediscover unversioned dependencies

- **Discovered/resolved locally:** 2026-09-15 during Trailblazer
  `TRB-Benchmark-005`; benchmark invocation guidance, not a geometry defect.
- **Cause:** A generated BenchmarkDotNet entry project does not inherit the
  benchmark project's local-stack transitive-reference setting. Rediscovery can
  build an unqualified sibling project beside its explicitly versioned reference.
  Serializing the build does not correct that identity mismatch.
- **Reproduction:** Two single-case Dry jobs inherit `UseLocalLsfStack=true`
  and `BuildInParallel=false`, differing only in inherited
  `DisableTransitiveProjectReferences=false/true`. The failing archived child
  contains SwiftCollections and its FixedMathSharp bridge at **0.0.0.0** and
  exits before workload execution when loading required SwiftCollections
  **7.0.0.0**. The corrected child contains **7.0.0.0 / 7.1.0.0**, exits zero,
  and completes its actual observation and zero-byte allocation diagnostic.
  GridForge and benchmark DLLs are byte-identical across that pair.
- **Correction:** Inherit both `UseLocalLsfStack=true` and
  `DisableTransitiveProjectReferences=true`, as documented in
  [Testing and Benchmarking](../wiki/Testing-and-Benchmarking.md#benchmarking-unreleased-sibling-libraries).
  The corrected normal parallel-build capture also completes all six cases,
  18 child launches, 54 warmups, 162 actual observations and 18 zero-byte
  diagnostics. No project graph, runtime, dependency package or CI change is
  needed; package-backed commands remain unchanged.
- **Evidence:** Trailblazer `artifacts/benchmark005-edge-ray` retains the
  `candidate-captures.ps1` reproduction command, `identity-bad/good` logs and
  frozen children, and `isolated-candidate-2` with complete raw/JSON/archive
  evidence. Independent review verifies all 192 frozen runtime-file hashes
  across those three jobs. Earlier incomplete attempts remain excluded; their
  reused output paths are not historical identity proof. This is the generated
  entry-point variant of Trailblazer `TRB-Issue-103`, not a resolution of the
  separate `GF-Issue-006` null-reference watch.

### GF-Issue-007 - Benchmark launcher returns success after a failed child

- **Discovered:** 2026-09-11 while investigating `GF-Issue-006`.
- **Status:** Resolved locally on 2026-09-11; benchmark tooling only.
- **Failure:** Every execution branch discarded `BenchmarkSwitcher.Run(...)`
  summaries and returned zero. The historical planar baseline therefore returned
  zero despite launch 2 exiting -1, launch 3 never running and partial statistics
  surviving. Trailblazer had the same defect, coordinated as `TRB-Issue-120`.
- **Resolution:** Every launcher execution route now returns `1` for critical
  validation errors, unsuccessful reports/builds, missing executables, and
  nonzero or unknown exits in returned executions. Successful help/list commands
  still return `0`. No runtime geometry, dependency or benchmark job changed.
- **Verification (2026-09-11):** Temporary checks reproduced the original false
  success and verified rejection of a later failed launch and a child exiting
  `23` after producing results. Result-model checks covered mixed reports,
  validation/build failure and unknown exits. Local Windows `Release` and
  `ReleaseLean` builds passed without warnings/errors, with `852/852` tests each;
  Debug passed `849/849`. Production DLLs returned `1` for invalid invocation/
  unroll settings and `0` for help/list.
- **Scope (2026-09-12):** Retain the launcher fix and documented evidence only.
  The temporary verification projects and their CI additions were removed at
  maintainer review; no permanent benchmark-tooling test system is introduced.
- **Evidence location:** The coordinating Trailblazer checkout retains
  `artifacts/gf-issue006/launcher-red-gridforge.log` and
  `artifacts/gf-issue006/launcher-validation/GridForge-*` logs. Full per-case
  output is local to this checkout under `artifacts/launcher-regression/`.
- **Remaining limits:** In BenchmarkDotNet 0.15.8,
  [`ExecuteResult.IsSuccess`](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Toolchains/Results/ExecuteResult.cs)
  checks result measurements, not exit status; the launcher checks both.
  The [runner](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Running/BenchmarkRunnerClean.cs)
  does not retain extra diagnoser executions in returned reports. Empty summaries
  can also represent information or invalid/no-match requests. A zero exit is
  therefore not a complete capture audit: inspect full logs and expected child,
  launch and sample counts. This fixes neither `GF-Issue-006` nor `TRB-Issue-119`.

### GF-Issue-005 — Recycled Occupant Tickets Could Resolve Replacement Occupants

Status: resolved on 2026-07-17.

Source: runtime identity audit following the pooled-grid generation defect.

Concern:

The public occupant ticket was only a reused `SwiftBucket` slot, so a retained
ticket could resolve or remove a replacement registration.

Resolution:

- Replaced raw integer slots with `OccupantTicket`, which carries the O(1) slot
  plus a process-wide nonzero registration generation.
- Stored generation-bearing scan-cell entries and validate exact generation
  before lookup or removal.
- Preserved tracked cleanup, pooling, callback recovery, deterministic query
  ordering, and allocation-free current-ticket lookup.

Verification:

- Different-occupant and same-occupant stale-slot regressions pass.
- Cross-world, identical-grid replacement, pooled scan-cell reuse,
  non-deactivating reset, tracked cleanup, throwing callback, and allocator
  exhaustion coverage pass.

### GF-Issue-003 — GridForge Reused Grid Spawn Tokens Across Pooled Generations

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
- Changed traversal and Gravitas query deduplication to exact `WorldVoxelIndex`,
  and removed hash-derived voxel/scan-cell token APIs.
- Fixed the shared SwiftCollections Debug value-key boxing exposed by the wider
  exact key without adding a GridForge or Gravitas workaround.

Verification:

- GridForge identical configuration, pooled reuse, cross-world, reset, hash
  collision, duplicate, and allocation regressions pass.
- Gravitas same-configuration replacement passes in 2D, 3D, and mixed modes; its
  focused identity/query/order suite passed `159/159`.
- Independent review found no unresolved code, determinism, performance,
  benchmark, or test-quality blockers.

### GF-Issue-004 — Identical-Bounds Blockers Shared One Registration Identity

Status: resolved on 2026-07-17.

Source: runtime identity audit following the pooled-grid generation defect.

Concern:

`BoundsKey` correctly described exact geometry, but `Blocker` also used it as a
supposedly unique blockage token. Two distinct blockers with identical bounds
therefore collapsed into one voxel obstacle entry and could not stack or be
removed independently.

Resolution:

- Added opaque process-unique `ObstacleToken` registration identities, allocated
  through active worlds, and kept `BoundsKey` as geometry only.
- Migrated blocker, direct obstacle, voxel tracker, and event paths to the exact
  token contract.
- Preserved one token across dynamic grid and sparse-voxel reconciliation while
  issuing a fresh token for each later explicit apply lifetime.
- Kept rollback exact to the registration that performed the mutation and moved
  the per-voxel maximum-count recheck inside the existing obstacle lock.

Verification:

- Dense and sparse same-bounds blockers stack and remove independently with both
  cached and retraced coverage.
- Dynamic replacement, sparse reconciliation, explicit reapply, rollback,
  default-token, reset, event, concurrent capacity, and cross-world isolation
  regressions pass.
- Full Debug suite: `444/444` passed.
- Independent re-review reported no findings.

### GF-Issue-002 — Coverlet Branch Instrumentation Guard-Target Misses

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

### GF-Issue-001 — Direction Utility Arrays Are Public And Mutable

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
