# Grid Coordinate Ranges And Sectors Plan

**Opened:** 2026-11-26

## Purpose And Status

**Status: planned; source investigation only.** Establish an explicit supported
coordinate domain and failure contract before selecting any sector hierarchy or
large-world representation. No runtime change, probe, regression, benchmark or
new validation result is claimed by this plan.

The audit used GridForge `ea52599521f3b86a80d6c3c5d1f2dacb36fc6c82` and the
current sibling FixedMathSharp source. Refresh affected source identities before
implementation. This document owns the design, phase decisions and evidence;
keep confirmed defects in the issue tracker and measured signals in the
benchmark backlog rather than treating every unsupported input as a bug.

## Existing Coordinate Contract

Each `VoxelGrid` already has topology-local indices and world-space bounds. Two
grids can contain local `(0,0,0)` at distinct world positions: rectangular
projection adds `BoundsMin` to the metric-scaled index, while hex projection
adds the quantized axial offset. Hosts can author grids around a local origin
and translate their configurations before registration while arithmetic fits.

[RectangularPrismTopology](../../src/GridForge/Grids/Topology/RectangularPrismTopology.cs),
[HexPrismTopology](../../src/GridForge/Grids/Topology/HexPrismTopology.cs) and
[WorldVoxelIndex](../../src/GridForge/Spatial/WorldVoxelIndex.cs) are the
current owners. Voxel identity includes world allocation, grid slot, grid
generation and local index. Runtime tokens are not persistent sector or content
identifiers.

The [named-grid plan](named-grids-and-priority-lookup-plan.md) owns
Name/priority, registration identity and overlap precedence. Current runtime
registration still rejects duplicate normalized geometry keys; the selected
named configuration model will permit differently named grids with matching
geometry. Sector grouping must not independently bypass Name uniqueness or
invent another position-selection policy. Reuse the accepted contract when it is
implemented.

[GridConfiguration.TryNormalize](../../src/GridForge/Configuration/GridConfiguration.cs)
checks topology, metrics, dimensions and the `Int32.MaxValue` address-count
ceiling. It is not a universal arithmetic-overflow admission boundary. The
[determinism guide](../api/guides/Determinism-Snapping-and-Pooling.md) requires
representable, unsaturated intermediates. Accepted centers do not automatically
prove that complete prisms, padded bounds, query offsets or distances fit.
[GridCellGeometry](../../src/GridForge/Grids/Topology/GridCellGeometry.cs)
already rejects unrepresentable exact geometry through checked construction.

FixedMathSharp owns checked arithmetic. Reuse `Fixed64.TryAdd`, `TrySubtract`,
`TryMultiplyAdd` and other suitable existing operations before proposing a new
upstream primitive. Its anchors preserve geometry in explicit frames; they do
not establish a GridForge sector-routing or persistent-world contract.

## Separate The Decisions

| Need                                             | Candidate ownership                                                       | Decision before implementation                                                                                  |
| ------------------------------------------------ | ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Reject unsupported finite-grid arithmetic        | GridForge configuration/topology admission, using FixedMathSharp          | Define the supported operation domain and observable failure; do not silently clamp.                            |
| Group grids for loading, persistence or interest | Existing host/world orchestration unless a core consumer proves otherwise | Establish membership, lifecycle and routing needs; a sector label alone changes no numeric range.               |
| Represent positions beyond one Fixed64 chart     | Reusable arithmetic upstream; spatial identity/routing in GridForge       | Retain an integer sector address plus bounded local values; do not require a global Fixed64 sum.                |
| Translate a live simulation origin               | Coordinated host, GridForge and affected physics/navigation owners        | Specify atomic translation, invalidation and continuation; this is distinct from grouping or offline placement. |

Do not mandate a `Sector` between `GridWorld` and `VoxelGrid`, replace every
position type, or add a global registry to finish the first phase. The target
scenario remains open: streaming/grouping alone and seamless cross-sector
interaction have different costs and contracts. Select it from a real consumer,
not an assumed hierarchy. No simulation clock belongs to this work; runtime
identity and revision-counter policy is a separate concern.

## Phase 0 — Numeric Admission And Scenario Selection

- [ ] Inventory configuration normalization, center projection, exact cell
      prisms, spatial-index bounds, padded coverage, relative offsets and
      nearest/distance calculations. Follow rectangular and both hex
      orientations, dense/sparse storage and planar/spatial callers.
- [ ] Separate documented unsupported arithmetic from desired stronger
      admission. Record which operations a successful configuration must support
      and which queries retain independent checked failure. Do not imply that
      arbitrary query radii or distances become safe merely because registration
      succeeds.
- [ ] Capture a small ordinary baseline and boundary counterexamples without
      materializing enormous grids. Use normalized descriptors and explicit
      sparse addresses where suitable. Classify failures against the current
      supported contract before calling them implementation defects.
- [ ] Check this source-derived, **unexecuted hypothesis**: rectangular X bounds
      `-1,500,000,000 .. +1,500,000,000`, cell width `128`, and one address on
      Y/Z. Both endpoints fit Fixed64 and the exact X count is `23,437,501`, but
      `CalculateDimensions` subtracts endpoints before division, so its
      intermediate span can saturate. This lies outside the documented
      unsaturated-intermediate requirement; no observed result or defect is
      asserted here. Existing unit-cell extreme-dimension tests do not settle
      this coarse-cell case.
- [ ] Select a concrete host scenario: loading named regions, repeated authored
      local origins, or movement/query continuity across distant sectors. Record
      required extent, cell metrics, interaction reach, active-grid counts and
      serialization expectations. Missing selection does not block drafting
      numeric admission, but it blocks committing to a sector representation.
- [ ] Record the disposition: implement bounded admission, retain existing
      documented limits, or stop for a specific missing upstream operation.
      Prefer admission hardening as the first coherent production slice; a
      sector hierarchy is not its prerequisite.

## Phase 1 — Prove Supported Finite Grids

- [ ] Define success and failure for normalization and registration, including
      output validity and whether a checked failure returns false or throws.
      Preserve existing defaults and ordinary valid configurations. Make any
      intentional narrowing of supported input explicit and document migration.
- [ ] Validate before publication. Rejected input must not consume a grid slot
      or generation, publish events, change versions/counts, retain storage or
      partially attach to the spatial index. Audit actual existing failure
      mutation before strengthening this into a new atomicity guarantee.
- [ ] Reuse lower-stack arithmetic and topology owners. Check full cell extents,
      hex basis/projection and cube coordinates, snapping/padding and supported
      index conversions; checking only bounds endpoints or voxel centers is
      insufficient. Never make saturation an implicit coordinate mapping.
- [ ] Add focused production-behavior regressions with independent expected
      dimensions, positions and geometry. Exercise each axis, negative bounds,
      raw-unit neighbors, zero/invalid metrics, tiny/large metrics, dimension
      limits, extrema and restored configuration paths. Rejection must preserve
      world state; subsequent ordinary registration and queries must still work.
- [ ] Review the first working slice independently before expanding. Run real
      lookup/coverage/index callers, preserve warm allocation and measure
      affected registration/query costs. Record net runtime growth, new files
      and removed duplicate arithmetic. Stop rather than spreading guards
      without a coherent supported-domain contract.

## Phase 2 — Gate Any Large-World Sector Design

This phase is conditional on a selected consumer after numeric admission. An
explicit retain/host-owned-grouping decision is a completed outcome.

- [ ] Specify sector address width/range, extent and axis meaning independently
      of reusable grid slots and transient world tokens. Define a canonical
      bounded local interval, negative-coordinate carry/borrow and
      exact-boundary ownership. Equivalent positions must normalize to one
      representation.
- [ ] Define checked conversion into a chosen working frame, including finite
      relative displacement and distance limits. Large absolute locations must
      remain representable without first summing into global Fixed64. Reuse
      upstream checked/wide operations; do not copy arithmetic or silently wrap
      integer sector differences. Define explicit failure when a relative query
      cannot fit its working chart.
- [ ] Specify cross-sector query scope, seam ownership, deterministic ordering,
      deduplication and hard candidate/output capacities. Account for every
      traversed sector; reject or report incomplete work explicitly. A sector
      directory is not proof of voxel presence, topology contact or clearance.
- [ ] Separate durable content/sector addresses from runtime registration
      identity. Define unload/reload, reuse, reset and abandoned-future
      behavior. A matching local index or persisted sector key must not validate
      an old live handle.
- [ ] Decide whether live rebasing is needed. If selected, enumerate all
      translated state: grid/voxel coordinates, indexes, blockers, host-owned
      occupants, partitions, diagnostic snapshots, pending queries and borrowed
      data. Define one publication/invalidation boundary and the effects of
      failure; grouping must not secretly translate existing state.
- [ ] Review Gravitas, Trailblazer and adapters at their actual context
      boundaries. They bind explicit GridWorld instances and consume world-space
      positions. Cover bodies, accepted motion, occupancy transfer, maps/graph
      publication, guide proofs, prepared frames and host/render transforms. Do
      not hide an offset inside GridForge while consumers interpret another
      frame.
- [ ] Prove one seam crossing and nearby query through a complete consumer
      cycle, then unload/reload and save/populate/continue. Include negative
      seams, distant rejection, different load orders and stale identities.
      Preserve exact results/hashes, host phase order and ownership without
      inventing a world snapshot format. Expand only after independent review
      and measured setup/mutation/query/teardown time, allocation and
      retained-memory gates.

## Verification And Closeout

Use existing focused fixtures before broader suites. Baseline/candidate inputs,
source identities and host phase order must match. Compare bounded observable
results and rejection state; passing round trips alone do not prove that a
saturated coordinate map is invertible. Keep disposable probes outside the
maintained suites and preserve portable recipes/results in this plan.

For implementation, validate `netstandard2.1` and `net10.0` in `Release` and
`ReleaseLean`; run both full net10.0 suites and the strict 100% reported line,
branch and executable-method coverage gate using fresh `coverlet.runsettings`
reports and `.assets/scripts/assert-full-coverage.ps1`. Preserve existing CI
policy. Check affected downstream consumer suites, replay/restore and relevant
allocation/performance gates; report local results separately from unchecked
Windows/Linux or released-package lanes.

Use `-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`
for coordinated source restore/build/test and serialize shared outputs. Record
provider revisions and local patches. If a missing reusable operation is proven,
implement/verify FixedMathSharp first, then GridForge and affected consumers.
After upstream changes land, remind maintainers to align affected CI source
pins; pin updates and package releases are not feature-completion gates unless
separately scoped.

Record accepted domains, explicit retain/defer decisions, reopening criteria,
behavior/replay evidence and measured costs here. Update current guides/XML for
adopted behavior and obtain independent final review. Archive the plan only when
selected implementation and verification are complete; leave edits unstaged and
uncommitted for review. No release or distribution work is presumed.
