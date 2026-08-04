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
| _None_ | - | - | - |

## Closed Signals

| Signal | Status | Priority | Tracking |
| ------ | ------ | -------- | -------- |
| Top-level grid indexing scales with covered hash-cell volume | Closed | High | [`Two-Tier Grid Spatial Index`](done/2026-08-03-two-tier-grid-spatial-index-plan.md) |

### Signal: Top-Level Grid Indexing Scales With Covered Hash-Cell Volume

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

| Workload | Baseline median / allocation | After | Confirmation |
| --- | ---: | ---: | ---: |
| Register 64 adjacent grids | 2.073 ms / 1,126,352 B | 1.854 ms / 1,124,144 B | 1.755 ms / 1,124,144 B |
| Remove 64 adjacent grids | 1.704 ms / 25,824 B | 1.174 ms / 17,384 B | 1.186 ms / 17,672 B |
| Register one 24-cell-per-axis grid | 2.433 ms / 3,703,864 B | 10.1 us / 96 B | 10.0 us / 1,056 B |
| Oversized point lookup, 8/64/256 grids | n/a | 93.0 / 127.7 / 163.5 ns, 0 B | 92.6 / 127.9 / 154.8 ns, 0 B |
| Oversized bounds lookup, 8/64/256 grids | n/a | 197.6 / 225.2 / 239.3 ns, 0 B | 200.2 / 229.2 / 248.2 ns, 0 B |

Raw matched artifacts are retained under
`artifacts/benchmarks/2026-08-03-grid-spatial-index-baseline`,
`...-two-tier-after`, and `...-confirmation`. Longer isolated ordinary point,
trace, and neighbor runs found no repeatable regression after empty-tier query
guards; caller-owned hot paths retain zero-allocation regressions. The shared
SwiftCollections key-index map now captures its callbacks once, removing the
measured per-removal delegate allocations for every hash, BVH, and octree user.
