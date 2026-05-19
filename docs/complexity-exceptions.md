# Cyclomatic Complexity Exception Register

This document records methods that intentionally exceed the current cyclomatic complexity review threshold.

## Policy

- Review threshold: cyclomatic complexity greater than 10.
- Risk threshold: CRAP score greater than 30 requires immediate test hardening or refactoring.
- Current status: the fresh coverage/CRAP report generated on 2026-05-19 has no methods above CRAP 30.
- Source report: `TestResults/coverage-analysis/reports-final3-complexity-20260519/Summary.txt`.

Complexity exceptions are acceptable when the method is compiler-generated iterator machinery, a deterministic fixed-order comparer, or a hot fixed-shape path where extraction would add indirection without lowering real maintenance risk. These exceptions should be revisited when coverage drops, behavior changes, or the implementation becomes harder to reason about.

## Exception Register

| Module | Method | Complexity | Coverage | Rationale | Revisit if |
| --- | --- | ---: | --- | --- | --- |
| `GridForge.Grids.ScanCell` | `<GetConditionalOccupants>d__32.MoveNext()` | 14 | 100% line / 100% branch | Compiler-generated state machine for the lazy `GetConditionalOccupants` iterator. The source method is compact filtering over pooled occupant buckets; replacing it just to reduce generated complexity would change API shape or add allocation pressure. | The iterator becomes a measured allocation/performance issue, filter behavior changes, or an allocation-free `Into` API replaces lazy enumeration. |
| `GridForge.Grids.Voxel` | `<GetNeighbors>d__95.MoveNext()` | 12 | 100% line / 100% branch | Compiler-generated state machine for cached neighbor enumeration. The source keeps cache refresh and yielded neighbor traversal local and fully covered. | Neighbor cache semantics change, consumers need allocation-free enumeration, or coverage drops below full line/branch coverage. |
| `GridForge.Grids.GridOccupantManager` | `CompareTrackedOccupancies(TrackedOccupancy, TrackedOccupancy)` | 12 | 100% line / 100% branch | Deterministic multi-key ordering over world token, grid index, voxel coordinates, grid token, and ticket. Explicit comparisons avoid comparer allocation and make sort precedence auditable. | Snapshot ordering changes, additional sort keys are added, or a generated/source-shared comparer becomes available without runtime cost. |

## Review Notes

- The 2026-05-19 refactor reduced methods above the complexity threshold from 32 to 3 while keeping CRAP scores below 30.
- Generated iterator `MoveNext` methods should be reviewed through the source iterator body first; do not rewrite public lazy APIs only to reduce generated-method complexity.
- For deterministic ordering and fixed-shape spatial predicates, prefer benchmark-backed changes over structural edits made only to lower a metric.
- Re-run coverage and CRAP analysis after changes to scan-cell iteration, neighbor enumeration, or occupancy tracking, then update this register.
