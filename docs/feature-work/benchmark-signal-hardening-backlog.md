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
| Top-level grid indexing scales with covered hash-cell volume | Promoted | High | [`Two-Tier Grid Spatial Index`](2026-08-03-two-tier-grid-spatial-index-plan.md) |

### Signal: Top-Level Grid Indexing Scales With Covered Hash-Cell Volume

**Discovered:** 2026-08-03  
**Source:** Gravitas mixed public sweep sparse-span investigation  
**Status:** Promoted to phased implementation

With the default 50-unit spatial-grid cell size, registering one sparse grid
whose normalized bounds span `[-100,000, +100,000]` on all three axes attempts
to visit `4,001^3`, or `64,048,012,001`, top-level hash cells before the public
Gravitas query begins. Matching the hash cell size to the grid makes the query
complete, which isolates the dominant failure to GridForge grid registration
rather than Gravitas narrow phase.

The approved design retains the ordinary fixed spatial-hash fast path and
routes automatically classified oversized grids into a fixed-point BVH. Large
query bounds retain an active-grid scan when that is cheaper than enumerating
empty hash cells. The linked plan owns the before/after artifacts, threshold
evidence, ordinary-workload non-regression gates, 100% coverage, and downstream
Gravitas closure.

## Closed Signals

| Signal | Status | Priority | Tracking |
| ------ | ------ | -------- | -------- |
| _None_ | -      | -        | -        |
