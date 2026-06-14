# Feature Work Issue Tracker

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:systematic-debugging before implementing fixes, use
> superpowers:test-driven-development for runtime behavior changes, and use
> superpowers:verification-before-completion before claiming an issue is fixed.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Active

**Goal:** Keep bugs, correctness risks, documentation defects, and
feature-work-discovered issues separate from feature design plans so each fix
can be triaged, tested, and committed independently.

**Architecture:** This document is intentionally undated. Each tracked item has
its own discovery date, source, status, affected files, and recommended
verification. Feature plans may reference this tracker instead of carrying bug
fixes inside API or design phases.

**Tech Stack:** `netstandard2.1` and `net8.0` runtime targets, xUnit,
BenchmarkDotNet when performance evidence is needed, FixedMathSharp core
runtime and tests.

---

## Tracker Rules

- Add new items when feature work uncovers a suspected bug, stale doc, test
  smell, performance anomaly, or correctness risk.
- Keep each item scoped tightly enough to fix and verify independently.
- Record the date on the item, not in this filename.
- Move an item to `Resolved Issues` only after the fix has tests or documented
  verification evidence.
- Do not use this tracker as a substitute for tests, benchmarks, or release
  notes.

## Active Issues

- None currently.

## Performance Investigation Queue

Performance issues should stay in the benchmark plan unless they become a
confirmed runtime defect. Current queue:

- None currently.

## Resolved Issues

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
- Result: `line-rate 1`, `branch-rate 1`, `5269/5269` lines,
  `1943/1943` branches.
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

`RectangularDirectionUtility` and `HexDirectionUtility` exposed direction sets as
public `static readonly` arrays. The field references were readonly, but array
contents remained mutable. Because topology code reads those direction sets for
neighbor slot counts, offsets, boundary ranges, and hex slot resolution,
consumer code could accidentally corrupt core neighbor behavior process-wide.

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
