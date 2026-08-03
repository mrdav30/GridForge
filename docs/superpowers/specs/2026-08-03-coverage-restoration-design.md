# Coverage Restoration Design

## Goal

Restore GridForge's authoritative Release coverage to 100% line, branch, and
method coverage without hiding live code behind exclusions or bloating the test
suite.

## Baseline

The fresh Release run on 2026-08-03 passed 483 of 483 tests and reported:

- 98.5% line coverage (5,215 of 5,292 lines)
- 95.7% branch coverage (2,312 of 2,415 branches)
- 98.5% method coverage (836 of 848 methods)

The largest gap is `GridDiagnosticChange`; smaller gaps remain in diagnostic
sessions and traversal, blockers, storage visitors, scan cells, neighbor
resolution, tokens, and several already line-covered conditional branches.

## Coverage Contract

The 100% target uses the same Release configuration, test project, Coverlet
settings, and assembly filters as `.github/workflows/coverage.yml`.

`ReleaseLean` has a different compilation surface and denominator. It will be
restored, built, and tested as a compatibility check, but it will not be mixed
into the Release coverage percentage.

## Approach

1. Cover the diagnostic comparison, equality, session, geometry, and traversal
   contracts first because they contain both CRAP hotspots and most uncovered
   lines.
2. Cover remaining live runtime branches with focused behavioral tests in the
   nearest existing test class.
3. Cover trivial constructors and value semantics with compact tests, reusing
   existing test helpers and theories where that reduces repetition.
4. Re-run fresh Release coverage after each coherent batch and use Cobertura or
   OpenCover sequence/branch points to drive only the remaining work.
5. Finish with a clean Release coverage run plus Debug, Release, and ReleaseLean
   validation.

Production code will be deleted only when caller analysis and runtime invariants
prove it unreachable. Coverage exclusions will not be added to reach the target.

## Test Hygiene

Independent audits found no assertion-free tests, swallowed exceptions, exact
duplicate test bodies, or safe production zombie code. Similar guard tests map
to distinct public branches and remain valuable.

The no-op `IDisposable` implementation in `GridForgeFixture` may be removed as
test-only hygiene. Existing large multi-contract tests will not be reorganized
unless a coverage change already touches them and consolidation clearly reduces
code without weakening diagnostics.

## Review And Verification

Each implementation batch receives an independent subagent review for spec
compliance and code quality. A separate final reviewer checks the complete diff,
including whether tests assert behavior rather than merely executing lines.

Completion requires fresh evidence for all of the following:

- Release line coverage: 100%
- Release branch coverage: 100%
- Release method coverage: 100%
- Debug build and tests pass
- Release build and tests pass
- ReleaseLean build and tests pass
- no new coverage exclusions or unreviewed production-code deletions
