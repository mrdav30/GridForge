# Deterministic Procedural Grid Generation Plan

**Opened:** 2026-11-26

## Purpose And Status

**Status: planned; design and first bounded recipe selected for investigation.**
No generator, public API, package, benchmark or runtime behavior is implemented
by this record. The source audit used GridForge
`ea52599521f3b86a80d6c3c5d1f2dacb36fc6c82`; refresh relevant contracts before
implementation. No build, test or performance result is claimed.

Provide a reproducible way to create useful layout data and materialize it
through existing GridForge ownership. The first recipe is a bounded rectangular
planar walker with connected floor output. Engine placement, visual dressing,
physics and navigation policy remain with their existing owners.

The [referenced Unity generator](https://nappin.dev/details/gridForge.html)
inspires seeded generation, branching walkers, room chunks and data-only output.
Its documented workflow is inspiration, not evidence for deterministic
cross-runtime behavior or permission to copy its implementation.

## Existing Owners

| Owner                                                                                                                                                                           | Existing responsibility                                                      | Consequence for this feature                                                                                                |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| [GridConfiguration](../../src/GridForge/Configuration/GridConfiguration.cs) and [NormalizedGridConfiguration](../../src/GridForge/Configuration/NormalizedGridConfiguration.cs) | Offline normalization, local address validation and cell-prism queries       | Validate the generation domain before materializing a world.                                                                |
| [GridWorld](../../src/GridForge/Grids/Managers/GridWorld.cs)                                                                                                                    | Registration, unique normalized configuration keys, identity and publication | Publish through registration APIs and the accepted named-configuration/priority contract; do not bypass identity admission. |
| [Sparse storage](../api/guides/Sparse-Grid-Storage.md)                                                                                                                          | Bounds as address space, explicitly configured physical cells                | Missing cells are absent, not implicit blocked walls or empty dense cells.                                                  |
| FixedMathSharp `Random/DeterministicRandom.cs`                                                                                                                                  | Explicitly seeded deterministic random streams                               | Reuse the existing PRNG; do not add a local random implementation.                                                          |
| [Partition contract](../../src/GridForge/Spatial/IVoxelPartition.cs)                                                                                                            | Host-defined voxel-local data and lifecycle                                  | Generation tags may drive host partition creation, without imposing a universal floor/wall partition.                       |
| GridForge-Unity, Gravitas and Trailblazer                                                                                                                                       | Presentation, collision and navigation policy                                | Keep prefab/Tilemap placement, collider construction and traversal authoring outside the core generator.                    |

Use the existing [world/grid model](../api/guides/Core-Concepts.md) and
[determinism rules](../api/guides/Determinism-Snapping-and-Pooling.md). The
[coordinate-range plan](grid-coordinate-ranges-and-sectors-plan.md) owns broader
admission and sector design. This first recipe stays within an already
representable bounded domain; it does not wait for a sector system.

## Decisions And Scope

- **Selected:** one data-only rectangular planar recipe, followed by one sparse
  registration consumer and an engine-neutral usage example. A 32x1x32 region is
  the initial representative fixture, not a universal supported-size limit.
- **Selected:** explicit seed, algorithm/version identity and validated
  settings. A seed is not a complete persistence format for edited terrain or
  future algorithm versions.
- **Selected:** one seeded walker first. Branching walkers and small room chunks
  are conditional extensions after that complete slice is measured.
- **Open before API implementation:** namespace/project/package placement,
  settings defaults, output ownership and exact result/status names. Use one
  production path; do not create a general plug-in generation framework or
  separate package solely for this experiment.
- **Deferred:** hex, stacked 3D rooms, noise/biomes, cross-sector generation,
  progressive regeneration and runtime scheduling. Reopen each for a concrete
  consumer after the first recipe passes behavior and complete-cost gates.
- **Excluded:** Unity engine APIs, rendering, arbitrary host callbacks while
  generating, simulation clocks, alternate occupancy indexes and duplicate
  active grid Names.

## Caller Contract To Establish

### Settings, random state and coordinates

Define the local rectangular domain, world placement, cell metrics, starting
cell, target fill, boundary policy, maximum live walkers if enabled and hard
work/output budgets. Define whether target fill counts unique carved cells and
how it rounds to an integer target. Validate checked address-count arithmetic,
representable geometry, impossible targets and capacities before random draws or
world mutation. Specify zero-sized, zero-fill and one-cell behavior.

Construct `DeterministicRandom` explicitly from the supplied seed; a zero seed
through its constructor is valid, while a default all-zero PRNG struct is not a
seeded stream. Copying the struct copies stream state. Keep one clear owner and
pass by reference where draws must advance that owner.

Use stable authored keys for feature streams if the recipe grows. Do not seed
from process-unique world/grid/occupant tokens, wall time, string hash codes,
thread scheduling or iteration order from an unordered collection. The same
algorithm version, settings, seed and ordered inputs must reproduce the same
layout on both supported target frameworks and package variants.

### Work, results and storage

Count explicit deterministic work units, including revisits, rejected boundary
steps and failed room placements. Target fill never grants permission for an
unbounded retry loop. Initial fixture budgets, such as at most 1,024 output
cells and 4,096 walker steps, are provisional and must be recorded before
candidate tuning; they do not establish latency limits.

Define distinct complete, invalid-settings and work-limit outcomes. Only a
complete accepted layout may be published by the first materialization path. If
partial data is exposed for preview, mark it explicitly and specify its validity
and ownership; never silently treat a budget-limited prefix as a complete
dungeon. Invalid input must not alter an existing world.

Specify whether each output call clears or appends, who owns backing storage,
and what invalidates any borrowed span. Prefer canonical local-index order and
explicit cell facts over live Voxel references. Preserve the distinction between
walkable cells, walls, decoration and absence. The host chooses whether a layout
becomes sparse floor cells, dense cells with blockers, or another supported
materialization; implement only the first selected policy initially.

### Publication, lifecycle and continuation

Generate and validate the complete layout before tearing down an existing grid.
Publication then follows the named-grid replacement contract: different Names
allow add-first replacement, while same-Name reuse requires remove-first. A
failed re-add after removal can leave a gap and does not restore the old
registration or runtime identity. Current registration still rejects duplicate
normalized geometry keys. The
[named-grid plan](named-grids-and-priority-lookup-plan.md) owns the selected
future Name/priority and shared-geometry policy. A generated layout must use
that accepted registration definition when implemented; it must not create
aliases to evade identity checks. The host may supply a Name or use the accepted
canonical derivation; Priority and name resolution remain separate from the
layout's random stream. Record observed registration failure behavior; do not
claim a transaction across callbacks, multiple grids or external engine objects
without implementing and testing it.

A generated layout may be persisted with its algorithm version/settings and
actual edited data. Do not serialize transient world identities as generation
seeds or promise that an unversioned seed regenerates identically after an
algorithm change. Teardown follows existing world/partition/pool lifetimes.

## Implementation Phases

### Phase 0 — Prove The Recipe And Materialization Policy

- Refresh the source graph and lower-stack PRNG signatures. Choose output
  ownership, concrete status semantics and the sparse-floor materialization
  policy before publishing an API.
- Define the 32x1x32 fixture, fixed seeds, work budgets, target-fill meaning and
  exact boundary/connectivity rules. Include 1x1, narrow strips, no progress,
  impossible target and exhausted-capacity controls.
- Produce data and materialize one accepted layout through normal GridWorld
  registration. Resolve cells, trace/scan it and remove/recreate it; use one
  coherent caller rather than an isolated random-walk kernel.
- Record output hashes/addresses, allocation, retained storage and full
  generation/registration/teardown cost. Set acceptance thresholds before
  tuning. Stop or simplify if the first useful case needs disproportionate
  machinery.

### Phase 1 — Implement One Deterministic Production Path

- Implement the smallest selected recipe and its public contracts. Add focused
  tests around actual runtime output, not tests of a benchmark or duplicated
  generator implementation.
- Check unique addresses, in-bounds output, connected floor cells under the
  selected adjacency, canonical ordering, exact statuses and unchanged world
  state on invalid or incomplete generation. Prove fixed-seed results across
  repeated executions and supported targets.
- Obtain independent review before expansion. Audit PRNG ownership, copy
  semantics, budget accounting, integer limits, pool cleanup and publication.
- Add one engine-neutral example; adapter preview is conditional on a real
  consumer and requires its own disposal/refresh verification.

### Phase 2 — Expand Only From Measured Need

- Evaluate bounded branching or room chunks using the same consumer. Preserve
  connectivity, deterministic ordering and hard work limits with all rejection
  work counted. Include their full generation and materialization costs.
- Record retain/defer decisions for other algorithms and representations. Remove
  superseded paths; do not keep comparison backends as permanent APIs.

### Phase 3 — Verify And Close

- Build GridForge for netstandard2.1/net10.0 in Release and ReleaseLean. Run the
  focused and complete net10.0 suites, then fresh strict line/branch/executable-
  method coverage using the existing runsettings and coverage assertion script.
  Require 100% reported coverage without exclusions or rounded-percentage
  waivers.
- Validate any accepted provider/adapter change in dependency order. Source-mode
  commands carry `UseLocalLsfStack=true`, global
  `DisableTransitiveProjectReferences=true` and serial shared-output builds;
  record exact revisions and actual benchmark payloads.
- Reuse existing benchmark machinery where possible. Freeze comparable hosts,
  inspect complete case/child results, preserve adverse samples, and use bounded
  runs without competing builds or profilers. Measure complete generation,
  registration, use and teardown, not only PRNG/iteration throughput.
- Update applicable guides/XML, examples and this plan. Record exact results,
  limits and deferred decisions here; leave changes unstaged/uncommitted. After
  accepted upstream work lands, coordinate CI dependency pins. Release readiness
  is separate unless explicitly requested.

## Completion Evidence

Completion requires the selected recipe, useful materialization consumer,
explicit contracts, bounded failure behavior, exact deterministic output,
reviewed first slice, applicable build/test/coverage results and measured
complete costs. Keep this record planned until those results exist. No result
has been produced by this planning pass.
