# GF-Issue-013: exact voxel occupant retrieval

Resolved in source on 2026-11-24; changes are unstaged and uncommitted for
review. The defective starting revision was
`dc1f3fb03d642e6c0db3ea3b5302f0dbfa1bd4a4` on develop. This is a verified fix,
not a package release claim. GF-Issue-014 is outside this change and remains
open.

## Cause and correction

`ScanCell` already stores occupant buckets by full `WorldVoxelIndex`. The
voxel-facing `GridScanManager.GetOccupants` and `GetConditionalOccupants`
methods selected the voxel's scan cell but then enumerated all its buckets.
Typed queries forwarded through the same incorrect unfiltered path. Thus a
matching neighbor could leak into results and invoke predicates even across
voxel layers.

The manager now uses the exact registration bucket. `GetOccupantsFor` and the
new internal filtered lookup share the existing SwiftCollections dictionary
lookup and one private static bucket iterator. Existing `OccupantPassesFilters`
continues to own occupant-before-group short-circuit filtering. No position
comparison is substituted for registration identity; moving an occupant's
Position alone does not change its occupied bucket. Scan-cell-wide public
queries, radius scans, registration/ticket handling, and world synchronization
remain unchanged. No public signatures, dependency pins, engine dependencies, or
runtime source files were added. Net runtime growth is 15 nonblank code lines
across the two existing files; there are no new runtime fields or ownership
containers.

The exact bucket is resolved at query invocation; entry enumeration and
predicates remain lazy. Finish enumeration before occupancy mutation, reset, or
pooled reuse, including between query creation and its first enumeration.
Results are borrowed views, not snapshots. Bucket slot order is preserved;
pooled slot reuse means this is not necessarily registration order. XML
contracts and the
[scan guide](../../api/guides/Scan-Cells-and-Query-Flow.md#current-retrieval-scope)
now describe the corrected scope and lifetime.

Disposition: retain this exact registration-bucket contract and the separate
intentional whole-scan-cell APIs. Reopen GF-Issue-013 if a voxel query again
emits or invokes predicates for another bucket, including after
removal/re-registration, world/grid generation changes, or through a forwarding
overload. Concurrent or retained-across-mutation enumeration is outside the
supported borrowed-view contract and requires separately scoped API design.

## Regression and independent review

The focused fixture is
[GridScanManager.VoxelOccupants.Tests.cs](../../../tests/GridForge.Tests/Grids/GridScanManager.VoxelOccupants.Tests.cs).
Its first eight dense/sparse cases were run **before** the runtime edit and all
failed with the expected neighboring-occupant leakage, rather than setup errors.
After correction, all eight passed. Two additional storage/filter cases bring
the final focused fixture to ten passing cases:

- Dense and sparse unfiltered, typed, and conditional retrieval through world
  identity, world position, local index, and direct voxel forms.
- An occupied neighbor on another Y layer sharing the same scan cell; a
  requested physical voxel with no occupants; mixed local types and a matching
  neighbor type. One occupant's Position moves after registration to distinguish
  bucket membership from current-position filtering.
- Predicates inspect only the selected bucket. Occupant rejection short-circuits
  group evaluation; null, accepting, and rejecting predicates retain their
  meaning.
- Bucket removal/re-registration and stale ticket rejection. Exact ticket lookup
  and intentional whole-cell enumeration remain controls.
- Missing dictionary, missing bucket, and wrong grid generation do not invoke
  predicates. Whole-cell predicate controls explicitly preserve both buckets.

A test-author subagent owned the focused fixture; an independent
`ponytail-review` subagent reviewed connected source, tests, and XML. The
reviewer caught pool-history-sensitive registration-order assertions. The tests
now derive ordered expectations from actual ticket slots or assert exact
membership/count. The final implementation review found no remaining actionable
findings. The coordinator ran and inspected all builds, tests, coverage, and
measurements.

The initial full Standard run passed 1,219 tests but had one uncovered branch in
the intentionally whole-cell predicate path, which the old manager route had
previously exercised. An explicit whole-cell predicate control restored that
behavioral coverage. No coverage exclusions, unreachable guards, or policy
changes were introduced.

## Validation

All builds and tests used
`-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`. The
sibling source graph was unchanged and clean at validation:

- Chronicler: `0281469ecc40e65a05867f473bc10f030e7f69f8`.
- FixedMathSharp: `8fdf889322f1bb82501c2afec63d6bb2f304ace2`.
- SwiftCollections: `7c2fa696f10d88194f5a895aaafd7debd8e27574`.

| Check                                    | Standard Release                                             | ReleaseLean                               |
| ---------------------------------------- | ------------------------------------------------------------ | ----------------------------------------- |
| Solution build, warnings as errors       | Both netstandard2.1 and net10.0 passed; zero warnings/errors | Both targets passed; zero warnings/errors |
| Existing suite plus regressions, net10.0 | 1,219 passed; zero failed/skipped                            | 1,219 passed; zero failed/skipped         |
| Fresh raw line coverage                  | 9,457 / 9,457                                                | 9,457 / 9,457                             |
| Fresh raw branch coverage                | 4,235 / 4,235                                                | 4,235 / 4,235                             |
| Executable methods covered               | 1,163 / 1,163                                                | 1,163 / 1,163                             |
| Repository strict coverage gate          | Passed                                                       | Passed                                    |

Commands, with ReleaseLean substituted for configuration and result path for the
second lane:

```powershell
dotnet build GridForge.slnx -c Release -p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1 -warnaserror
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c Release --no-build -p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1 --collect:"XPlat Code Coverage" --settings tests/GridForge.Tests/coverlet.runsettings --results-directory artifacts/gf-issue013/final-coverage/Release
./.assets/scripts/assert-full-coverage.ps1 -CoverageDirectory artifacts/gf-issue013/final-coverage/Release
```

The unfixed and first green runs used the same source flags and
`--filter FullyQualifiedName~GridScanManagerVoxelOccupantTests` without
`--no-build`. DocFX is checked with the repository command
`dotnet tool run docfx docs/api/docfx.json --warningsAsErrors`. Validation is on
Windows/.NET 10.0.10, SDK 10.0.302. Linux, released-package compatibility, and
publication were not part of the requested source-mode validation.

Scoped `dotnet format --verify-no-changes --severity error` passed with
Configuration=Release, TargetFramework=net10.0 and the same LSF source flags in
the environment, including an unchanged existing test file as a control. The
initial default-configuration workspace had incorrectly flagged the required
xUnit import as unused; matching the built Release workspace resolved that
formatter discrepancy. The compiler-confirmed import is retained. Changed
Markdown passed Prettier 3.9.5 and local-file link checks; `git diff --check`
passed. No formatter rewrote unrelated files.

## Allocation and bounded performance evidence

A disposable .NET 10 console queried the real manager methods. The baseline
assembly was copied before runtime edits; the same compiled caller ran against
baseline and corrected DLLs in A-B-B-A order, without competing builds/tests.
`DOTNET_TieredCompilation=0` kept JIT tiers fixed. Each process warmed each case
30,000 times and measured five samples of 200,000 complete query enumerations;
two processes per revision give ten samples per case. Allocation used
`GC.GetAllocatedBytesForCurrentThread`; time used `Stopwatch.GetTimestamp`.
Query/predicate delegates and registrations were created before measurement. The
source fixture used one 8x8x8 grid with scan-cell size 8, four target occupants
split evenly between groups 0/1, and either zero or 64 other occupied voxel
buckets with four occupants each. Occupants had distinct IDs. Conditional
queries accepted group 1; typed queries selected the shared concrete occupant
type. Counts were accumulated to keep enumeration observable.

Measured binary SHA256 identities:

- Defective baseline:
  `24CEE941CDE996DA2EE5D38073EEEFA330CC0044C757A061F2F3F855AEC0E798`.
- Corrected runtime:
  `D034B9F46E819C5865E964301D30F08A29270FB2F94B2312C99AF464900D2898`.

The core measurement loop was:

```csharp
for (int warmup = 0; warmup < 30000; warmup++)
    Query();
for (int sample = 0; sample < 5; sample++)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    long bytes = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    long count = 0;
    for (int iteration = 0; iteration < 200000; iteration++)
        count += Query();
    long ticks = Stopwatch.GetTimestamp() - start;
    bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
    // Record count / 200000, bytes / 200000, and ticks / frequency / 200000.
}
```

`Query` selected `grid.GetOccupants(target)`,
`grid.GetConditionalOccupants(target, static o => o.OccupantGroupId == 1)`, or
`grid.GetVoxelOccupantsByType<ProbeOccupant>(target)` and counted a `foreach`
without materializing a result collection. For portable repetition, build a
console against copied baseline DLL dependencies, preserve that
executable/output, then clone it and replace GridForge.dll with the corrected
Standard source-mode DLL. Run the two preserved executables A-B-B-A under the
same tiering setting. The same-loop source graph, runtime, query counts, group
distribution, warmup, sample size, and assembly identities above define this
bounded experiment.

The zero-neighbor case returns identical results on both revisions:

| Query       | Result count | Baseline B/query | Corrected B/query | Baseline median ns/query (min-max) | Corrected median ns/query (min-max) |
| ----------- | -----------: | ---------------: | ----------------: | ---------------------------------- | ----------------------------------- |
| Unfiltered  |            4 |              136 |               120 | 94.35 (91.8-102.3)                 | 87.70 (85.0-95.9)                   |
| Conditional |            2 |              168 |               120 | 107.85 (107.1-111.5)               | 90.95 (90.0-95.5)                   |
| Typed       |            4 |              184 |               168 | 180.35 (178.0-233.2)               | 165.10 (163.8-166.1)                |

With 64 unrelated buckets, corrected counts remain 4/2/4, allocations remain
120/120/168 B, and median times were 90.4/94.4/164.7 ns. The defective baseline
returned 260/130/260, so its longer traversal is not a correctness-equivalent
speed comparison. The result and direct dictionary lookup establish that
unrelated buckets are no longer traversed. Small timing differences and this
isolated profile do not prove performance on every host/workload.

The first working correction retained a full WorldVoxelIndex in its iterator and
added 24 B/query. It was replaced before handoff by eager exact bucket selection
and one static bucket iterator; the final figures above remove that introduced
allocation cost. Initial default-tier runs showed JIT transition timing drift;
those runs were not mixed into the fixed-tier comparison. Their observations are
not evidence of steady-state throughput.

Two existing radius-scan controls also completed using the repository runner:

```powershell
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release --no-build -p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -- scan-radius-memory --filter '*ScanRadius_WarmPools*' '*ScanRadiusInto2D_WarmScratch*' --artifacts artifacts/gf-issue013/benchmarks
```

Both expected benchmarks ran one in-process ShortRun launch, three warmups and
three measured iterations; the launcher exited zero with six measured samples
and no failed report. Means were 5.644 ms/320 B for the warm 3D scan and 3.544
ms/3,312 B for the warm caller-scratch planar scan. Both reported short
iteration-time warnings and wide confidence intervals. These are existing
workload smoke/continuity evidence, not a precise performance comparison or a
zero-allocation claim for the radius workloads.

Raw logs, binary copies, probes and coverage/benchmark artifacts are disposable.
The inputs, revisions, commands, original defect reproducer, outcomes,
allocation tradeoff and limitations are retained here so follow-up does not
require those artifacts to survive.

## Original intake (2026-11-22)

The following archived intake preserves the original source revision and
portable reproducer. Its open status and documentation-only next action describe
that historical discovery; the resolution and acceptance evidence above
supersede them.

### GF-Issue-013 — Voxel occupant enumeration includes neighboring buckets

- Discovered: 2026-11-22. Status: confirmed implementation defect; open,
  documentation updated to describe current behavior. Priority: P2.
- Owner: `GridScanManager.GetOccupants(VoxelGrid, Voxel)`,
  `GetConditionalOccupants(VoxelGrid, Voxel)`, and `GetVoxelOccupantsByType<T>`,
  including forwarding position/address/world overloads. `ScanCell.GetOccupants`
  correctly enumerates the entire scan cell; the voxel-facing manager fails to
  select the requested bucket.
- Source revision: GridForge `5f96fded5803e62dffb8291d15485beb9be65c65`.
  Original discovery used a develop sibling-source build. Its linked XML-pass
  manifest was not retained in the repository; the current source graph and
  fresh reproduction evidence are recorded above.
- Portable reproducer: after a source-mode Release build, run the following in a
  disposable .NET 10 console with a project reference to GridForge. Pass
  `-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`
  through its build/run. `System.Linq` is used only by the probe.

  ```csharp
  using System;
  using System.Linq;
  using FixedMathSharp;
  using GridForge.Configuration;
  using GridForge.Grids;
  using GridForge.Spatial;

  using GridWorld world = new GridWorld();
  world.TryAddGrid(new GridConfiguration(
      new Vector3d(0, 0, 0), new Vector3d(2, 0, 0)), out ushort slot);
  world.TryGetGrid(slot, out VoxelGrid grid);
  grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel first);
  grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel second);
  grid.TryAddVoxelOccupant(first, new ProbeOccupant(first.WorldPosition, 1));
  grid.TryAddVoxelOccupant(second, new ProbeOccupant(second.WorldPosition, 2));
  Console.WriteLine(first.ScanCellKey == second.ScanCellKey);
  Console.WriteLine(string.Join(",", grid.GetOccupants(first)
      .Cast<ProbeOccupant>().Select(value => value.Id)));
  Console.WriteLine(grid.GetVoxelOccupantsByType<ProbeOccupant>(first).Count());
  Console.WriteLine(grid.GetConditionalOccupants(first).Count());

  sealed class ProbeOccupant : IVoxelOccupant
  {
      public ProbeOccupant(Vector3d position, int id)
      {
          Position = position;
          Id = id;
          GlobalId = new Guid(id, 0, 0, new byte[8]);
      }
      public int Id { get; }
      public Guid GlobalId { get; }
      public Vector3d Position { get; }
      public byte OccupantGroupId => 0;
  }
  ```

- Expected: the first voxel's queries return only occupant 1; typed and
  conditional counts are 1. Observed: shared scan-cell key is true, enumeration
  returns `1,2`, and both counts are 2. The first voxel must itself be occupied;
  an unoccupied voxel returns empty even when its scan cell contains occupants.
  Ticket-based lookup remains exact.
- Next action: separately scope exact-bucket selection at the manager/scan-cell
  boundary, add a failing two-voxel regression, and validate all forwarding
  forms and both package variants. Keep scan-cell-wide enumeration available on
  `ScanCell` itself. Do not change runtime behavior in this XML pass.
