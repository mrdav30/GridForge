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
  `GF-Benchmark-008`.
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

- None currently.

## Closed Signals

| Signal | Status | Priority | Tracking |
| ------ | ------ | -------- | -------- |
| GF-Benchmark-007 — Ordered trace sorting repeatedly copies full interval payloads | Closed locally | Medium | Smaller heap-sort data movement; Trailblazer `TRB-Benchmark-005` remains active, with short/long protocol results retained |
| GF-Benchmark-006 — Rectangular traces enumerate and solve unnecessary geometry | Closed locally | High | Exact candidate pruning/slab intervals; Trailblazer `TRB-Benchmark-005` remains active for full-frame budgets |
| GF-Benchmark-005 — Strictly separated edges still enter exact interval solving | Closed locally; runtime candidate withdrawn | High | Mixed measured benefit; Trailblazer `TRB-Benchmark-005` remains active |
| GF-Benchmark-004 — Disjoint segment candidates reach expensive planar intersection | Closed locally | High | Trailblazer `TRB-Benchmark-005` remains active for remaining guided-frame costs |
| GF-Benchmark-003 — Debug cursor/contact allocation guards fail | Closed locally | Medium | SwiftDictionary value-key boxing fixed and verified downstream |
| GF-Benchmark-002 — Disjoint swept-body prisms reach expensive exact overlap | Closed locally | High | Trailblazer `TRB-Benchmark-003` |
| GF-Benchmark-001 — Top-level grid indexing scales with covered hash-cell volume | Closed | High | [`Two-Tier Grid Spatial Index`](done/2026-08-03-two-tier-grid-spatial-index-plan.md) |

### GF-Benchmark-007 - Ordered trace sorting repeatedly copies full interval payloads

- **Discovered/resolved locally:** 2026-09-14 UTC during Trailblazer
  `TRB-Benchmark-005`, against GridForge `847458b`.
- **Signal/change:** A Flow/500 profile attributes about 77 ms of 3,340 ms
  sampled measured-root time to interval sorting. The existing heap sift swaps
  the full interval at every level and passes comparison arguments by value.
  Carry the root once, move each selected child up once, then write the saved
  interval; take comparison arguments by readonly reference. Keep the comparator,
  child selection and exact equal-key permutation. A bounded parent loop replaces
  the open-ended loop. No public API, cache, retained memory, dependency, geometry
  or failure-order change; the implementation supports `netstandard2.1` and
  `net8.0` without target-specific code.
- **Containing-workload evidence:** Two original/candidate comparisons of
  Trailblazer's guided A*/100, Flow/100 and Flow/500 cases improve LOS medians
  **6.9% then 5.5% / 8.6% then 6.2% / 8.3% then 8.2%**, respectively. Final
  medians are **23.7 / 14.5 / 67.5 ms**. A*/100 and Flow/500 block medians improve
  in both pairs, but ordinary-frame gains do not repeat. The final Flow/500
  profile attributes about 40 ms of 3,149 ms to sorting; these overlapping
  sampled observations include validation and are not isolated-sort predictions.
- **Required qualification:** Short-protocol Flow/100 ordinary medians regress
  **51.6% then 18.2%** and complete-block medians **25.7% then 11.7%**. A separate
  longer-warmed pair has ordinary medians **0.7869 to 0.7927 ms (+0.7%)**, LOS
  **15.3143 to 13.6863 ms (-10.6%)** and blocks **107.9576 to 101.7507 ms (-5.8%)**.
  Retain the short regressions; do not infer a JIT cause or universal frame win.
- **Protocol/provenance:** Serial Windows/i7-9700K/.NET 8.0.29 local-stack Release,
  BDN 0.15.8; unchanged authored harness and default synchronous 16-frame LOS
  cadence. Four short captures use three launches/case, one warmup and three
  actual 64-frame blocks/launch. Two separate Flow/100 captures use three launches,
  ten warmup and twenty actual blocks/launch, candidate then original. All
  observations/outliers are retained. Independent raw/PDB audits verify exact
  replay and zero observed allocation/GC in **366 records / 23,424 frames**, all
  **378** frozen child-file hashes and only one changed runtime source document.
  This is not a randomized trial or cross-platform performance guarantee.
- **Behavior/validation:** New 31/32/33-cell forward/reverse traces assert every
  ordered interval and full payload. They pass the original implementation,
  fail a deliberately reversed heap comparison, and pass the final source;
  83 focused tracing tests pass. A local ignored original/final sort diagnostic
  verifies 56 exact permutations, including equal/duplicate keys and heap
  boundaries. Release and ReleaseLean each pass **890 tests**, with exact
  **8,957/8,957 lines, 3,857/3,857 branches and 1,118/1,118 fully covered methods**;
  both solution builds have zero warnings/errors. Independent correctness and
  Ponytail reviews find no actionable issue. No test project or CI framework is
  added.
- **Evidence/disposition:** The coordinating Trailblazer benchmark tracker
  retains the commands and full comparison under the interval-sort follow-up.
  Evidence is in its `artifacts/benchmark005/guided-{prepared-prism-baseline,
  interval-sort-*}*`, profiles, mutation logs, permutation check and
  `interval-sort-final-verification`. The baseline label reflects an investigation,
  not an implemented prepared-prism cache. Close this focused data-movement
  change locally; `TRB-Benchmark-005` remains open because every measured final
  Flow/500 LOS frame still exceeds 31.25 ms. This does not close historical
  exceptions, establish Linux CI or authorize release.

### GF-Benchmark-006 - Rectangular traces enumerate and solve unnecessary geometry

- **Discovered/resolved locally:** 2026-09-13 during Trailblazer
  `TRB-Benchmark-005`, against GridForge `b19f373`.
- **Signal:** After earlier rejection/containment improvements, rectangular
  tracing still enumerates the ray's full address box and invokes general
  polygon intersection. Planar intervals occupy about 24-27% of sampled
  guided measured-root time, which also includes benchmark validation/hashing.
- **Implementation:** Keep complete-range logical candidate admission/counts,
  but narrow eligible rectangular columns to conservative closed Z ranges.
  Use actual cell centers, guarded rounding and unchanged Y candidates.
  Horizontal/vertical rays, unrepresentable arithmetic and unsupported topology
  retain the original enumeration. Canonical rectangles use closed-bound
  containment and exact slab intersection before rounding the two final
  parameters. Sparse presence, candidate order, capacity/failure precedence
  and public interval results remain unchanged; no public API/cache is added.
- **Review correction:** Preserve the original horizontal path because a
  collinear extrapolated parameter can round onto a finite endpoint. Literal
  forward/reverse regressions reproduce this old behavior. The intermediate
  `column` capture predates that correction; later captures use the hardened
  fallback. This is not the withdrawn per-edge experiment (`GF-Benchmark-005`).
- **Isolated containing-frame evidence:** Trailblazer's `distance` to `slab`
  captures change exactly one authored runtime document,
  `GridCellGeometry.NavigationBodySegment.cs`. Recheck medians improve from
  **50.049 / 35.076 / 163.764 ms** to **36.293 / 21.479 / 105.392 ms** for
  A*/100, Flow/100 and Flow/500: **27.5% / 38.8% / 35.6%** less time.
  All nine launch-level recheck and block medians improve; ordinary timing is
  mixed. Cumulative reductions also include separate FixedMathSharp changes
  and must not be attributed to GridForge alone.
- **Protocol:** Serial Windows/i7-9700K/.NET 8.0.29 local-stack Release,
  BenchmarkDotNet 0.15.8; three launches per case, one warmup and three actual
  64-frame blocks per launch. Every capture has nine successful children and
  45 records, including diagnostics. Exact signed-Int64 replay and zero frame
  allocation/GC persist. Child manifests and portable-PDB source hashes are
  independently verified. These are descriptive local comparisons, not
  randomized paired trials; no outlier is removed.
- **Validation:** Release and ReleaseLean each pass **887 tests**, with exact
  **8,957/8,957 lines**, **3,857/3,857 branches** and **1,118/1,118 fully covered
  methods**. Both solution builds have zero warnings/errors. New tests assert
  raw interval results, closed tangencies, full-domain/one-raw behavior, sparse
  results and unchanged budgets. An intentionally rounded emptiness comparison
  fails the narrow-miss regressions; the restored implementation passes.
  Independent correctness/Ponytail reviews find no actionable issues.
- **Evidence/remaining boundary:** The coordinating Trailblazer tracker retains
  the command and cumulative results under `TRB-Benchmark-005`; local evidence
  is in its `artifacts/benchmark005/guided-*`,
  `guided-final-endpoints-review-audit.json` and `reduction-verification`.
  Trailblazer full-frame acceptance remains incomplete. This closes the focused
  GridForge source change, not the broader workload signal, Linux CI,
  released-package validation or historical `GF-Issue-006` exception.

### GF-Benchmark-005 - Strictly separated edges still enter exact interval solving

- **Discovered:** 2026-09-11 during Trailblazer `TRB-Benchmark-005`.
- **Status:** Closed locally on 2026-09-11 with a **no-runtime-change decision**.
  The tested edge guard is withdrawn: its narrow touch-case gains do not establish
  enough benefit in the consuming workload to retain added per-edge work/state.
  The earlier whole-footprint rejection remains unchanged.
- **Hypothesis:** A prior guided Flow/100 sampled-thread-time profile attributes
  roughly 49.0% inclusively to tracing and 36.7% to planar interval solving
  (overlapping shares). Avoid exact intersection work for individual edges whose
  endpoints have equal nonzero widened orientation signs relative to the ray.
  Reuse consecutive signs; preserve mixed/zero contacts and the existing exact
  parameter solve. This candidate added no cache or public API.
- **Capture:** Windows/.NET 8.0.29, BenchmarkDotNet 0.15.8, local-stack Release.
  Baseline GridForge `b3e60f3`, FixedMathSharp `e8a2ab5`, plus the new benchmark
  fixture; the candidate changes only the planar-interval implementation in
  production source. Default adaptive iterations and outlier policy, three
  launches per case; all timing runs are serial. With the unreleased siblings,
  set `$env:UseLocalLsfStack = 'true'` before building and running so generated
  children also select the local stack:

  ```powershell
  dotnet build tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0 -p:UseLocalLsfStack=true
  dotnet tests/GridForge.Benchmarks/bin/Release/net8.0/GridForge.Benchmarks.dll grid-planar-interval --filter '*' --launchCount 3 --exporters json --keepFiles --artifacts artifacts/planar-edge-baseline
  ```

  Use a distinct candidate artifact directory for a comparison; do not overwrite
  the baseline. The actual coordinated captures are under Trailblazer's
  `artifacts/benchmark005/planar-edge-{baseline,candidate}*`.

  | Case | BDN mean before / candidate, ns | BDN change | All-raw Actual mean change |
  | --- | ---: | ---: | ---: |
  | Flat hex crossing | 3,070.308 / 3,094.237 | +0.8% | +0.7% |
  | Flat hex vertex touch | 3,602.272 / 3,470.694 | -3.7% | -3.7% |
  | Flat hex strict miss | 45.441 / 47.124 | +3.7% | +3.9% |
  | Pointy hex crossing | 3,040.900 / 3,031.906 | -0.3% | -0.1% |
  | Pointy hex vertex touch | 3,654.261 / 3,412.504 | -6.6% | -6.7% |
  | Rectangle crossing | 3,423.084 / 3,480.132 | +1.7% | +1.7% |
  | Rectangle vertex touch | 3,928.495 / 3,900.625 | -0.7% | -0.8% |
  | Rectangle strict miss | 42.847 / 43.702 | +2.0% | +2.1% |

- **Interpretation:** Negative means less time. BDN means use its retained,
  overhead-corrected results; all-raw means retain every Actual iteration without
  trimming or overhead subtraction. Each complete case has 45 raw observations.
  All three launch-level raw medians improve for hex touches, but not for all
  crossings. Strict misses return before the new guard: their higher measured
  times are not evidence of guard execution. Capture drift and code-generation
  effects are possibilities, not proven causes or a universal noise threshold.
  These are separate process batches, not randomized paired trials. Hex crossings
  pass through opposite vertices; rectangle crossings pass through edge interiors.
- **Incomplete baseline:** [GF-Issue-006](issue-tracker.md#gf-issue-006---planar-strict-miss-benchmark-launch-reported-a-null-reference-exception)
  excludes pointy-hex strict miss from this table. Baseline has 26 child executions,
  25 clean exits and one failure; candidate has 27 clean exits. All complete cases
  have explicit zero allocation/GC evidence; the failed baseline case has unknown
  allocation, not zero. Two clean direct frozen-runner replays are separate
  evidence, not replacements for the failed parent-driven launch or a fix.
- **Containing frames:** Trailblazer's three-case guided capture has 27 actual
  64-frame blocks per version. Whole-block median changes are **-2.3% A*/100,
  -0.5% Flow/100 and +2.3% Flow/500**. Each case still has 36/576 frames above
  31.25 ms. Replay is exact and allocation observations are zero, but there is no
  consistent workload-level gain. The coordinating tracker retains frame and
  launch distributions; do not describe the rejected candidate as shipped savings.
- **Retained checks:** Eight literal interval cases cover reversed crossings,
  closing-edge entry, vertex entry/exit and exact one-sixth/five-sixths rounding.
  They pass on unchanged runtime and the candidate; all eight fail an intentionally
  incorrect whole-prism-rejection mutation. All 27 focused interval tests pass
  after restoring correct behavior. A separate frozen-binary differential probe
  compares 33,324 ordered queries across rectangle/both hex footprints, extreme
  origins/endpoints and one-raw offsets with identical boolean and raw interval
  outputs. It is characterization evidence, not an independent geometry oracle.
- **Final validation:** After withdrawing the guard, Release and ReleaseLean each
  pass **852 tests** with exact **8,861/8,861 lines**, **3,787/3,787 branches** and
  **1,115/1,115 fully covered methods**. Both solution configurations build both
  library targets with zero warnings/errors. Downstream Trailblazer core/adapter
  matrices also pass with exact coverage. These are local Windows unreleased-stack
  checks, not Linux CI or released-package validation. Evidence is in the
  coordinating checkout's `artifacts/benchmark005/ray-verification`. A final
  restored-source confirmation also passes both exact Release/Lean gates and
  **849 Debug tests**, with zero build warnings/errors, under
  `ray-restored-gridforge`.
- **Provenance/review:** Independent code/proof and raw-evidence reviews pass.
  All 64 files in each micro child archive match their manifest. Portable-PDB
  checks identify only the intended GridForge implementation document as a changed
  authored input; all 110 GridForge and 200 FixedMathSharp authored documents match
  their corresponding guided capture. This is a rebuilt comparison, not a claim
  of identical generated native code. Runtime source is restored to committed HEAD.
- **Next boundary:** Keep Trailblazer `TRB-Benchmark-005` active. Measure repeated
  transformed-vertex work in FixedMathSharp containment separately; preserve wide
  arithmetic and early-exit behavior. Do not change candidate accounting, sparse
  snapshots, failure order or LOS cadence to manufacture a win. Neither this
  experiment nor clean follow-up runs resolve `GF-Issue-006` or `TRB-Issue-119`.

### GF-Benchmark-004 - Disjoint segment candidates reach expensive planar intersection

- **Discovered:** 2026-09-10 while measuring Trailblazer `TRB-Benchmark-005`.
- **Status:** Closed locally on 2026-09-10 for the strict disjoint-candidate
  rejection. The broader guided-frame budget remains unmet and tracked by
  Trailblazer; this is not a claim that tracing is now fast enough.
- **Evidence:** Trailblazer's real guided Flow100 frame fixture spends
  1.14–1.19 seconds on synchronized periodic blocked line-of-sight rechecks.
  A separate measured-block profile places approximately 90% inclusively in
  `GridTracer.TraceIntervalsInto`, 87% in `TryGetPlanarSegmentInterval` and
  60% in wide point containment. Shares overlap; this is not isolated per-call
  cost or a universal host timing claim.
- **Cause:** The segment-AABB range includes many cells whose convex
  footprint lies strictly on one side of the supporting line. Exact interval
  math currently processes those false positives as well as intersecting cells.
- **Focused change:** Use existing exact widened `Vector2d.OrientationSign`
  for conservative same-side rejection, then retain the existing interval solve
  for every ambiguous/crossing/tangent/point case. Do not alter raw candidate
  enumeration, ceilings, canonical output, prism validation or failure order.
- **Matched result:** Three cases (A*/100, Flow/100, Flow/500), each with three
  launches, one warmup and three actual 64-frame blocks. Observed maximum host
  frames fall **1,250.667 -> 146.656 ms**, **1,220.792 -> 116.166 ms**, and
  **4,117.407 -> 501.471 ms**. Median whole-block times improve
  **85.8% / 87.0% / 82.4%**. Only `GridForge.dll` and its PDB differ between
  the 47 frozen before/after **parent** output files. A later provenance audit
  found a different FixedMathSharp binary in the retained generated child;
  parent hashes alone do not prove exact historical child-binary attribution.
  The timings remain recorded observations. Trailblazer's `TRB-Benchmark-005`
  records the qualification and a subsequent frozen-child control; new captures
  archive actual executed children separately. All captured replay hashes
  match, and all frame allocation/collection-change records remain zero.
- **Limits and tradeoff:** Each case still has 36/576 frames above 31.25 ms,
  at the same synchronized recheck positions. Ordinary A*/100 median increases
  **2.371 -> 2.624 ms** in this capture; retain that follow-up rather than
  claiming every frame improved. A*/500 was excluded from the repeated matrix.
  This rejection does not reduce candidate enumeration's asymptotic size.
  Trailblazer's separate `TRB-Issue-119` A* setup exception is not claimed as
  fixed by this performance change.
- **Verification:** Nineteen new cases use independent literal geometry,
  closed-contact, extreme-coordinate, finite-segment, candidate-budget and
  failure-order assertions. Release/Lean each pass **844 tests** with exact
  **8,861/8,861 lines**, **3,787/3,787 branches** and **1,115/1,115 fully covered
  methods**. Both solution configurations build both library targets with zero
  warnings/errors. An additional Debug solution run passes all **841 applicable
  tests**. Downstream Trailblazer core/adapter matrices also pass with
  exact coverage. Independent code/proof and raw-evidence reviews pass.
  These are local Windows, unreleased-stack checks, not Linux/released-package CI.
- **Coordination:** Trailblazer's benchmark tracker owns the complete host
  protocol, remaining profile leads and retained `artifacts/benchmark004`
  evidence, including `guided-{baseline,candidate}` and `verification-final`.

### GF-Benchmark-003 — Debug Cursor/Contact Allocation Guards Fail

- **Discovered:** 2026-09-10 during extra Debug validation for
  `GF-Benchmark-002` / Trailblazer `TRB-Benchmark-003`.
- **Status:** Closed locally on 2026-09-10. The SwiftCollections-owned fix and
  focused tests are implemented, independently reviewed and validated through
  the unreleased local stack. No GridForge runtime change is required.
- **Baseline:** GridForge `0ac12155344828047f0c4f69adfa53883227d0da` with
  SwiftCollections `df71a0fad083f42a8dde528f8a0634ab8d5a5a54` reproduces the
  original four Debug failures: 818 pass, four fail, 822 total. The earlier
  pre-body-optimization counterfactual also reproduced exactly these failures;
  they are independent of `GF-Benchmark-002`.

  | Debug guard | Before | After |
  | ----------- | -----: | ----- |
  | Covered-address cursor advance | 112 B | 0 B |
  | Filtered boundary-contact advance | 160 B | 0 B |
  | Unfiltered boundary-contact advance | 48 B | 0 B |
  | Last-pair remove/readd churn | 880 B versus 856 B control | Passes the unchanged no-incremental-allocation comparison |

- **Isolation:** Six new SwiftDictionary Debug rows measure lookup hit/miss,
  removal hit/miss/reinsert and indexer hits with `ushort` and a 96-byte
  `IEquatable<T>` key. At 256 warmed iterations, each baseline row allocates
  12,288 / 57,344 bytes respectively: two 24 / 112-byte boxes per iteration.
  Caching whether `TKey` can be null, then short-circuiting the existing
  `FindEntry`/`Remove` null guards, makes lookup/removal allocate exactly zero
  and passes all 822 original GridForge Debug tests before any GridForge test
  change. This controlled change confirms the downstream fix, including the
  churn comparison, without claiming a unique allocation stack for its net
  24-byte difference.
- **Adjacent confirmed cause:** The typed indexer still allocates one box per
  hit (6,144 / 28,672 bytes per 256 iterations) after the null-guard-only fix.
  Calling the existing object-based error helper only on a miss removes that
  successful-read boxing too. All six rows then pass at exact zero, without
  Debug exclusions, retries, forced GC or relaxed tolerances.
- **Contracts and tests:** Reference-null and empty nullable keys preserve
  lookup/removal failure, default output, comparer bypass and exception types.
  Valid nullable keys still work. Probing, comparer behavior for valid keys,
  mutation/versioning, public API and serialization are unchanged. The GridForge
  churn test now also rejects its helper's `-1` failure sentinel on the contact
  side, preventing failed add/remove operations from masquerading as low
  allocation. The allocation comparison itself is unchanged.
- **Coverage follow-up:** Initial Swift ReleaseLean coverage exposed two lines
  and three branches in cached key-view reuse, enumeration completion and
  packed-set populated-state restoration. The same gaps reproduced on unchanged
  production source. Two transport-independent behavior tests close them; no
  additional runtime change or coverage exclusion was needed. The original
  indexer also failed the new allocation regressions in that instrumented Lean
  counterfactual. Swift's complexity register records the fresh review and
  distinguishes source complexity from Coverlet's exported metric.
- **Verification:** All builds succeed and cover both runtime target frameworks.
  The Swift ReleaseLean build records one `MSB3026` file-copy retry warning;
  it succeeds on retry with zero errors. The retained matrix is not described
  as warning-free. A separate serial (`-m:1`) Swift Lean build confirmation
  succeeds with zero warnings/errors; the original warning remains in evidence.
  Tests pass without skips:
  - Swift core: 1,097 Debug / 1,099 Release / 1,071 Lean; companion: 43 each.
    Combined exact coverage is 7,527 lines / 2,530 branches / 1,278 methods in
    Debug, 5,676 / 2,522 / 1,278 in Release, 5,664 / 2,522 / 1,272 in Lean.
  - GridForge: 822 Debug / 825 Release / 825 Lean. Both Release variants retain
    exactly 8,848 lines / 3,777 branches / 1,114 fully covered methods.
    Debug tests are green; its separately recorded instrumented coverage is
    11,066/11,078 lines, 3,790/3,807 branches and 1,109/1,114 fully covered
    methods, not a claim of full Debug coverage.
  - Trailblazer: 2,467 core + 65 adapter in Release; 2,405 + 65 in Lean.
    Both retain exactly 30,842 lines / 12,089 branches / 3,011 fully covered
    methods. Existing staged Trailblazer changes were preserved.
- **Benchmark scope:** Matched serial BenchmarkDotNet ShortRun integer
  lookup/removal cases at 100 / 1K / 10K / 100K entries report zero allocation
  both before and after. These are smoke measurements, not a Release speedup,
  steady-state or tail-latency claim. The decisive evidence is the controlled
  boxing regressions and downstream Debug guards. Measurements are local
  Windows x64/.NET 8.0.29 results, not Linux CI or released-package validation.
  Standalone benchmark builds use the repository's `0.0.0` assembly identity;
  local-stack test copies use the declared `7.0.0` core identity. The two smoke
  runs use matching standalone build settings, but they are not byte-identical
  to the version-stamped downstream test binaries. No cross-binary timing claim
  is made.
- **Reproduction and evidence:** Run
  `dotnet test GridForge.slnx -c Debug -p:UseLocalLsfStack=true`; build and test
  SwiftCollections in Debug/Release/ReleaseLean and Trailblazer in both release
  configurations with the same local-stack property and each project's coverage
  runsettings. The Swift allocation cases are named
  `ValueKeyLookup_ShouldAllocateZeroAfterWarmup`,
  `ValueKeyRemoval_ShouldAllocateZeroAfterWarmup` and
  `ValueKeyIndexerHit_ShouldAllocateZeroAfterWarmup`.
  Trailblazer's `artifacts/gf-benchmark003` retains baseline and null-guard-only
  logs/TRX, the unchanged-production Lean coverage counterfactual, final matrices
  in `final-validated`, and `benchmark-baseline`/`benchmark-candidate` reports.
  Baseline setup mistakes (the benchmark project's declared TFM is `net8`, not
  `net8.0`, and an initially incorrect test constructor argument) remain in
  separate logs; they are not runtime failures or flaky-test retries.

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
  retained as `GF-Benchmark-003`, not fixed by the body patch. The later
  SwiftCollections follow-up above now closes that separate Debug gate.
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
