# GF-Issue-014: validate direct obstacle ownership

Resolved in source on 2026-11-24. Changes are left unstaged and uncommitted for
review. The verified starting revision is
`90137abb206ca23572335a7e76633113e84a5b74`; this record does not claim a package
release.

## Defect and correction

Discovered on 2026-11-22 at GridForge
`5f96fded5803e62dffb8291d15485beb9be65c65`, priority P2. The original stack
manifest was not retained. A new reproduction at the starting revision confirms
the defect independently: direct `GridObstacleManager.TryAddObstacle`,
`TryRemoveObstacle`, and `ClearObstacles` accepted a foreign physical voxel,
mutated it, and charged the supplied unrelated grid's counts, version and
committed changes. All 12 initial dense/sparse, same-world/cross-world
regression cases failed for that behavior before the runtime edit.

The three direct mutation boundaries now share a physical-ownership check. It
reuses `Voxel.IsValidOwnerGrid` for active-grid, slot, world-token and
grid-generation validation, then resolves the local address and requires the
same voxel reference. The reference check matters because `Voxel.WorldIndex` has
a public setter; copying a valid identity onto a foreign voxel does not make
that voxel part of the grid. No active-world registry lookup is used: grid
teardown unregisters the grid before clearing its voxel state.

Validation and state-dependent guards execute under the existing world read ->
grid obstacle -> committed-change locks. Sparse mutation uses the
committed-change lock, so membership must be checked there to prevent an empty
sparse voxel being removed between validation and obstacle attachment. The
focused interleaving regression failed against an interim
guard-before-commit-lock implementation (add reported success after removal),
and the final placement corrects it. Removal and clear recheck the count in the
same lock scope, avoiding use of pre-lock counts. Add checks `IsBlockable`
there; its separate capacity branch was redundant and was removed. The existing
threaded capacity regression remains a control. Clear retains its
world-write-lock teardown path and deferred event delivery.

Foreign, unallocated, missing-coordinate and mismatched-generation inputs make
add/remove return `false` and clear do nothing. They do not change obstacle
counts, versions or publish events. Valid-owner calls keep the existing token,
stacking, capacity, event identity and ordering behavior. Blocker,
world-identity, and position callers use this shared direct boundary. No public
signatures, serialization schema, dependencies, runtime fields or source files
were added.

## Contract disposition and limits

Disposition: enforce current physical membership plus world/grid generation at
all three direct mutation boundaries. Keep the existing borrowed-reference
lifetime: finish using grid/voxel references before removal, reset or pooled
reuse. A reference recycled into a valid new object cannot reveal the caller's
old intent. Arbitrary concurrent recycling of retained references is outside
that contract.

For delayed mutation, resolve `WorldVoxelIndex` through its owning world. It
rejects removed world/grid generations, but identifies an address within a grid,
not a persistent physical voxel: sparse removal/recreation at the same
coordinate within the same grid generation keeps that address identity.
Per-voxel generation identities would be a separate API decision, not an
unimplemented promise of this fix. Reopen GF-Issue-014 if a foreign, mismatched
or unallocated input can again change obstacle state through these boundaries,
or valid teardown fails to clear owned state. Revisit identity design only when
a concrete consumer requires sparse physical-incarnation identity or
retained-reference concurrency.

The [obstacle guide](../../api/guides/Blockers-and-Obstacles.md) and XML
comments now describe enforced rejection and these lifetime limits.

## Regression and review

The focused fixture is
[GridObstacleManager.Ownership.Tests.cs](../../../tests/GridForge.Tests/Grids/GridObstacleManager.Ownership.Tests.cs),
with 29 cases:

- Twelve dense/sparse add/remove/clear cases for a foreign grid in the same
  world and in another world. Rejection preserves both grids' and voxels'
  counts, versions, cached version, and manager/voxel/world events. A
  valid-owner control verifies exact successful counts, versions, event
  identities and event kinds.
- Ten dense/sparse cases for a foreign world token, grid slot, saved pre-reset
  grid generation, missing coordinate, and foreign physical reference carrying
  the copied current identity. All three mutation forms reject each mismatch.
- Six dense/sparse remove/reset/deactivate cases with three physical voxels and
  two tokens each. Every saved identity produces one exact clear snapshot and
  committed clear event, all voxel state is released, and old world-identity
  forms reject a replacement grid while fresh forms succeed.
- One controlled sparse-removal interleaving case verifies that a waiting add
  rejects a voxel removed before it acquires the committed-change lock, with no
  extra obstacle count, version increment or event.

A test-author subagent wrote the fixture; an independent `ponytail-review`
subagent reviewed the connected source, production callers, pooling/teardown and
tests. Final independent review found no actionable issues. The coordinator ran
and inspected the serial builds, tests, coverage and benchmarks.

## Validation

All source-mode restore/build/test commands carry
`-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`.
Unchanged, clean sibling revisions at validation:

- Chronicler: `0281469ecc40e65a05867f473bc10f030e7f69f8`.
- FixedMathSharp: `8fdf889322f1bb82501c2afec63d6bb2f304ace2`.
- SwiftCollections: `7c2fa696f10d88194f5a895aaafd7debd8e27574`.

| Check                                 | Standard Release                                                                                           | ReleaseLean                             |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| Solution build, warnings as errors    | netstandard2.1 and net10.0 pass; zero warnings/errors                                                      | Both targets pass; zero warnings/errors |
| Full net10.0 test suite               | 1,248 passed; zero failed/skipped                                                                          | 1,248 passed; zero failed/skipped       |
| GridForge reported lines              | 9,462 / 9,462                                                                                              | 9,462 / 9,462                           |
| GridForge reported branches           | 4,243 / 4,243                                                                                              | 4,243 / 4,243                           |
| GridForge executable methods          | 1,164 / 1,164                                                                                              | 1,164 / 1,164                           |
| Strict existing coverage gate         | Passed                                                                                                     | Passed                                  |
| Changed direct-overload XML contracts | Both targets: summary, exact parameter names, returns where applicable, remarks and identity links checked | Both targets checked                    |

The baseline suite contained 1,219 tests; the final suite includes all 29
focused regressions. No coverage exclusions or CI policy changes were
introduced. Representative commands from the repository root:

```powershell
dotnet build GridForge.slnx -c Release -p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1 -warnaserror
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c Release --no-build -p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1 --collect:"XPlat Code Coverage" --settings tests/GridForge.Tests/coverlet.runsettings --results-directory artifacts/gf-issue014/Release-final
.assets/scripts/assert-full-coverage.ps1 -CoverageDirectory artifacts/gf-issue014/Release-final
```

Repeat with `ReleaseLean` and a fresh `ReleaseLean-final` result directory. The
coordinator inspected the raw counts above, not just rounded percentages.
Standard and Lean source artifacts include both target-framework `GridForge.dll`
and adjacent `GridForge.xml`, plus `GridForge` / `GridForge.Lean` local source
packages (`0.0.0-gf-local-lsf`). Source-mode package generation does not
validate released-package dependency compatibility.

DocFX 2.78.5 built with
`dotnet tool run docfx docs/api/docfx.json --warningsAsErrors`: zero warnings or
errors. Its input is Standard Release net10.0 DLL plus adjacent XML. The
generated `GridForge.Grids.GridObstacleManager.html` was inspected for all three
changed direct forms: current physical ownership, parameter names, Boolean
return text where applicable, no-op clear summary, lifetime remarks, and
resolved `WorldVoxelIndex` links. The updated guide was included in the site
build. All four shipped target/variant XML files carry the corrected direct
contracts.

Scoped C# cleanup and verification use the Release net10.0 workspace with both
LSF flags set in the environment. Targeted Prettier 3.9.5 and `git diff --check`
pass. Generated output stays ignored.

No baseline build failures were observed. The 12 pre-fix regressions and the one
interim lock-placement regression are intentional red controls, recorded above;
the final runs have no failures introduced or left unresolved. Validation was on
Windows x64, SDK 10.0.302 / .NET 10.0.10; Linux and released-package mode were
not run in this source-mode task.

## Blocker workload check

Existing `BlockerApplyVsRemoveBenchmarks` exercised cached/uncached apply and
remove through the real blocker caller: 64 blockers, span 6, four tiled flat
grids. Initial three-iteration before/after runs completed all four cases each,
but had broad intervals and tiered-compilation sensitivity (cached apply means
10.558 versus 16.342 ms). They are retained as smoke evidence, not an attributed
regression or a throughput conclusion.

A follow-up comparison used the same existing workload with tiered compilation
disabled, ten warmups and fifteen measured iterations per case. Baseline source
was restored temporarily from the starting commit into the two changed runtime
files, built with the source flags, and copied to an ignored isolated host;
fixed sources were restored in `finally` before building the current host. No
other source or dependency changed. Four serial runs used baseline/fixed/fixed/
baseline order (A1/B1/B2/A2):

| Workload, mean ms/wave | A1 baseline | B1 fixed | B2 fixed | A2 baseline |
| ---------------------- | ----------- | -------- | -------- | ----------- |
| Apply, uncached        | 3.570       | 3.739    | 3.678    | 3.715       |
| Apply, cached          | 3.765       | 3.476    | 3.624    | 3.761       |
| Remove, uncached       | 4.123       | 3.983    | 3.925    | 4.179       |
| Remove, cached         | 3.968       | 3.990    | 3.907    | 4.551       |

All 16 case launches completed, each with 15 measured samples (240 actual
samples total); BenchmarkDotNet retained 54/54/55/59 samples after outlier
removal. Each launcher exited zero and reported four executed benchmarks. The
first three workloads reported identical allocation in every stable run:
1,596.34 / 1,906.08 / 1,020.73 KiB per wave. Cached removal reported 996.73 KiB
except A2 at 996.45 KiB. The added ownership predicate performs lookups and
reference comparison, with no new runtime allocation or retained state. These
local results show no confirmed material regression; the small uncached-apply
difference and run noise do not establish zero overhead or a general throughput
guarantee. Short-iteration warnings remain, and no gameplay-frame tail-latency
claim is made.

Reproduce from a source-mode Release benchmark build:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet tests/GridForge.Benchmarks/bin/Release/net10.0/GridForge.Benchmarks.dll BlockerApplyVsRemoveBenchmarks --filter '*' --warmupCount 10 --iterationCount 15 --artifacts artifacts/gf-issue014/stable-replay
Remove-Item Env:DOTNET_TieredCompilation
```

The host was Intel Core i7-9700K, Windows x64. GridForge baseline DLL SHA-256:
`8241f44d39904fc5c13ae5e0c3e3197fd55635b363fe1d74b14b529e5818c1e3`; fixed DLL:
`b76c708daf42dfaa338c86e78dfb8925fcec1b19c4dd61be1f7e09a482b07c14`. Supporting
logs, raw coverage, isolated baseline host and benchmark outputs are ignored
under `artifacts/gf-issue014`. This record and the maintained regressions retain
the reproducible inputs and conclusions if those artifacts are removed. No
benchmark infrastructure, dependency pins or CI gates were changed.

## Portable original counterexample

This console reproducer needs a disposable .NET 10 project referencing GridForge
and the same source flags used above. At the original discovery revision, the
reported output was `True; 0; 1; 1`; the verified starting revision's initial
regression matrix independently confirmed foreign-pair mutation. With this fix,
the output is `False; 0; 0; 0`:

```csharp
using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;

using GridWorld world = new GridWorld();
world.TryAddGrid(new GridConfiguration(
    new Vector3d(0, 0, 0), new Vector3d(1, 0, 0)), out ushort firstSlot);
world.TryAddGrid(new GridConfiguration(
    new Vector3d(10, 0, 0), new Vector3d(11, 0, 0)), out ushort secondSlot);
world.TryGetGrid(firstSlot, out VoxelGrid owner);
world.TryGetGrid(secondSlot, out VoxelGrid other);
owner.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel);
bool added = other.TryAddObstacle(voxel, world.AllocateObstacleToken());
Console.WriteLine($"{added}; {owner.ObstacleCount}; " +
    $"{other.ObstacleCount}; {voxel.ObstacleCount}");
```

Next action: review and commit this scoped fix; no runtime follow-up remains for
GF-Issue-014 under the documented lifetime contract. Reopening criteria are
above.
