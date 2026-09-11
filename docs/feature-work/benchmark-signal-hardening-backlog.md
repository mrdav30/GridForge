# Benchmark Signal Hardening Backlog

## Purpose

This document captures benchmark-derived hardening signals that fall outside the
active feature plan. It is intentionally undated and long-lived: individual
entries carry their own discovery dates, evidence, status, and next isolation
step.

Use this backlog for measured performance, allocation, scaling, and benchmark
evidence concerns. Bugs or correctness risks that are not primarily benchmark
signals belong in [`issue-tracker.md`](issue-tracker.md). Broad feature or
architecture work should be promoted into its own dated plan and referenced from
this backlog.

## Intake Rules

- Signal IDs use `GF-Benchmark-NNN`. The next available ID is
  `GF-Benchmark-004`.
- Assign an ID at intake and never reuse it, including after a signal closes or
  moves into a dated plan. Check this file's Git history before advancing or
  repairing the counter.
- Add a signal only when it comes from a benchmark, allocation guardrail,
  profiler trace, or repeated validation run.
- Record the command, date, affected row or test, measured value, why it
  matters, and the smallest useful next isolation step.
- Keep benchmark-only instrumentation in tests or benchmark support unless the
  runtime needs a durable diagnostic API.
- Prefer a focused fix when the signal has a narrow cause.
- Promote to a dated feature-work plan when the signal spans multiple
  subsystems, requires API design, or needs staged implementation.
- Close entries only after a runtime/test/docs change lands or after a written
  no-change decision explains why the signal is expected.

## Baseline Commands

Build the benchmark project before capturing evidence:

```powershell
dotnet build tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0
```

After runtime changes, validate the package paths:

```powershell
dotnet test GridForge.slnx --configuration Release
dotnet test GridForge.slnx --configuration ReleaseLean
```

## Active Signals

| Signal | Status | Priority | Tracking |
| ------ | ------ | -------- | -------- |
| GF-Benchmark-003 — Debug cursor/contact allocation guards fail | Open | Medium | Source-correlated SwiftDictionary value-key null checks |

### GF-Benchmark-003 — Debug Cursor/Contact Allocation Guards Fail

- **Discovered:** 2026-09-10 during extra Debug validation for
  `GF-Benchmark-002` / Trailblazer `TRB-Benchmark-003`.
- **Evidence:** Full local-stack Debug passes 818 tests and fails four of 822:
  `GridCoveredAddressCursorTests.Advance_ShouldAllocateNothingAfterWarmup`
  (112 bytes), `GridBoundaryContactCursorTests.FilteredAdvance_ShouldAllocateNothingAfterWarmup`
  (160 bytes), `Advance_ShouldAllocateNothingAndRetainNoVoxelReferencesAfterWarmup`
  (48 bytes), and
  `PairDirectory_ShouldNotAllocateWhenLastPairIsRemovedAndReaddedAfterWarmup`
  (880 versus 856 baseline bytes). Release/Lean each pass all 825 tests at exact
  100% coverage. Configuration-dependent test counts are retained as observed.
- **Counterfactual:** Temporarily removing only the body-bound optimization
  restores the complete pre-change GridForge production source and reproduces
  the same four Debug failures and byte counts. The new body tests are retained.
  Candidate source was restored exactly, and its 42 body-trace tests pass in
  Debug. This signal predates and is independent of the body patch; the full
  Debug gate is still red, not waived by the Release results.
- **Source lead:** SwiftCollections `SwiftDictionary<TKey,TValue>.FindEntry`
  and `Remove` compare a generic key with null. Value-key boxing in unoptimized
  code is consistent with one 112-byte configuration-key lookup, two 24-byte
  ushort lookups for pair traversal, and their 160-byte filtered sum. The churn
  delta has no clean single-lookup attribution. This is source-correlated
  evidence, not an allocation-stack capture or confirmed fix.
- **Reproduction:** `dotnet test GridForge.slnx -c Debug -p:UseLocalLsfStack=true`.
  Initial candidate, pre-change production counterfactual and restored body
  logs/TRX are retained in the Trailblazer checkout under
  `artifacts/benchmark003/validated/debug-gridforge`, `debug-baseline` and
  `validated/debug-body`, respectively; companion logs retain exact failures.
  No SwiftCollections source change was made in this pass.
- **Next isolation:** Confirm the generic null-check allocation with a focused
  SwiftCollections Debug value-key probe, preserve reference-key null behavior,
  then validate the owning upstream fix and downstream configurations together.
  Do not weaken or skip the guards or blame Debug assertions without attribution.

## Closed Signals

| Signal | Status | Priority | Tracking |
| ------ | ------ | -------- | -------- |
| GF-Benchmark-002 — Disjoint swept-body prisms reach expensive exact overlap | Closed locally | High | Trailblazer `TRB-Benchmark-003` |
| GF-Benchmark-001 — Top-level grid indexing scales with covered hash-cell volume | Closed | High | [`Two-Tier Grid Spatial Index`](done/2026-08-03-two-tier-grid-spatial-index-plan.md) |

### GF-Benchmark-002 — Disjoint Swept-Body Prisms Reach Expensive Exact Overlap

- **Discovered:** 2026-09-10, during Trailblazer's `TRB-Benchmark-003` follow-up.
- **Evidence:** Matched local-stack profiling attributes about 50% of sampled
  obstructed volume A* time and 66% of warm Flow acquire/sample time inclusively
  to positive body/prism overlap. These are overlapping attribution shares,
  not predicted speedups. The query already has checked whole-sweep bounds,
  but candidate and missing-neighbor checks enter expensive exact sweep math
  even when the body's and prism's bounds are disjoint.
- **Focused change:** Use the existing inclusive zero-tolerance AABB overlap
  primitive before exact positive-overlap checks. Keep candidate/prism validation,
  work debits, closure handling, sorting, physical evidence, capacities and
  failure ordering unchanged. Tangent bounds still reach exact geometry.
  No cache, retained state, public API, or FixedMathSharp change is needed.
- **Status:** Closed locally on 2026-09-10 for the focused body-trace change.
  Matched downstream combined-stack volume medians improve from 164.125 to
  84.491 ms for obstructed internal A*, and 3.737 to 1.273 ms for public warm
  Flow acquire/sample/dispose. Each case has 300 observations/version with
  100 warmups per launch. Allocations and reported semantic/work counters are
  unchanged. These are combined GridForge/Trailblazer results, not isolated
  per-patch attribution or a universal timing guarantee.
- **Verification:** Six new characterization rows protect both translating
  endpoints, radius and vertical extent. All 825 tests pass in Release and
  Lean, each with exactly 8,848/8,848 lines, 3,777/3,777 branches and 1,114/1,114
  fully covered methods. All 42 body-trace tests pass in Debug; the wider Debug
  failures are independently reproduced on pre-change production source and
  retained above as `GF-Benchmark-003`, not declared fixed or passing.
  Independent source and downstream-evidence review found no blocking body-
  patch finding. Full evidence and provenance limits are consolidated in
  Trailblazer's `docs/feature-work/benchmark-signal-hardening-backlog.md`
  (`TRB-Benchmark-003`), with local artifacts under `artifacts/benchmark003`.
  Local results do not replace CI or released-package validation.

### GF-Benchmark-001 — Top-Level Grid Indexing Scales With Covered Hash-Cell Volume

**Discovered:** 2026-08-03  
**Source:** Gravitas mixed public sweep sparse-span investigation  
**Status:** Closed upstream and confirmed through the downstream Gravitas public
sweep

With the default 50-unit spatial-grid cell size, registering one sparse grid
whose normalized bounds span `[-100,000, +100,000]` on all three axes attempts
to visit `4,001^3`, or `64,048,012,001`, top-level hash cells before the public
Gravitas query begins. Matching the hash cell size to the grid makes the query
complete, which isolates the dominant failure to GridForge grid registration
rather than Gravitas narrow phase.

The retained design keeps ordinary grids on the fixed spatial hash, routes
automatically classified oversized grids into a fixed-point BVH, and scans
active grids when a query's empty cell volume would cost more. The internal
64-cell threshold was selected from measured 64/512/4,096 candidates.

| Workload                                | Baseline median / allocation |                         After |                  Confirmation |
| --------------------------------------- | ---------------------------: | ----------------------------: | ----------------------------: |
| Register 64 adjacent grids              |       2.073 ms / 1,126,352 B |        1.854 ms / 1,124,144 B |        1.755 ms / 1,124,144 B |
| Remove 64 adjacent grids                |          1.704 ms / 25,824 B |           1.174 ms / 17,384 B |           1.186 ms / 17,672 B |
| Register one 24-cell-per-axis grid      |       2.433 ms / 3,703,864 B |                10.1 us / 96 B |             10.0 us / 1,056 B |
| Oversized point lookup, 8/64/256 grids  |                          n/a |  93.0 / 127.7 / 163.5 ns, 0 B |  92.6 / 127.9 / 154.8 ns, 0 B |
| Oversized bounds lookup, 8/64/256 grids |                          n/a | 197.6 / 225.2 / 239.3 ns, 0 B | 200.2 / 229.2 / 248.2 ns, 0 B |

Raw matched artifacts are retained under
`artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline`,
`...-two-tier-after`, and `...-confirmation`. Longer isolated ordinary point,
trace, and neighbor runs found no repeatable regression after empty-tier query
guards; caller-owned hot paths retain zero-allocation regressions. The shared
SwiftCollections key-index map now captures its callbacks once, removing the
measured per-removal delegate allocations for every hash, BVH, and octree user.
