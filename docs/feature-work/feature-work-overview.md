# Feature Work Overview

## Purpose

This document is an evergreen overview of GridForge feature work. It tracks
active scope, recently completed work, and deferred or evidence-gated designs.
It is a curated view rather than a backlog of every possible feature. Update it
for significant milestones; keep individual issue and benchmark status in their
coordination trackers.

Keep active and deferred plans alongside the coordination trackers in this
directory. Archive completed plans and finished investigation reports under
`done/`, mark their completion status, and update incoming and relative links.
Archived assessments describe the evidence at that time. Use descriptive plan
filenames without dates; place `**Opened:** YYYY-MM-DD` directly below each plan
title. Existing completed records retain their historical filenames.

## Coordination Trackers

Keep these trackers empty when possible, and promote broad work into feature
plans instead of burying it in notes.

1. [Benchmark Signal Hardening Backlog](benchmark-signal-hardening-backlog.md)
   - Reproduce measured allocation or runtime-cost signals, then resolve them or
     close them with an evidence-backed no-change decision.
2. [Issue Tracker](issue-tracker.md)
   - Triage bugs, correctness risks and documentation defects independently from
     feature design. Keep root causes, regressions and resolution evidence with
     the owning issue or completion record.

## Active Coordination

The following plans are selected for design and bounded investigation.
The [issue tracker](issue-tracker.md) lists independently confirmed defects.

- [Partition Diagnostics](partition-diagnostics-plan.md)
  - Add optional renderer-neutral partition facts, starting with an actual
    physics-partition diagnostic consumer. Define bounded copied output and
    attachment/content invalidation separately from navigation proof stamps.
- [Grid Coordinate Ranges And Sectors](grid-coordinate-ranges-and-sectors-plan.md)
  - Establish supported coordinate arithmetic and rejection before selecting a
    sector hierarchy. Gate integer-sector/local frames and any live rebasing on
    a concrete cross-stack consumer and explicit conversion/query contracts.
- [Deterministic Procedural Grid Generation](deterministic-procedural-grid-generation-plan.md)
  - Prove one seeded, work-bounded rectangular layout recipe and sparse-grid
    materialization path. Keep engine placement and physics/navigation policy
    with their existing owners; expand algorithms only from measured need.
- [Named Grids And Priority-Based Position Lookup](named-grids-and-priority-lookup-plan.md)
  - Support optional authored names with deterministic derivation and an
    auxiliary name-to-slot lookup; internal identities and spatial lookup stay
    numeric. Use immutable configurations, higher priority and lower-slot ties,
    with no sparse fallback. Align automatic world coverage with effective
    overlap precedence while preserving explicit resident access and navigation
    binding. Differently named configurations may share geometry; replacement
    and cross-stack query scope are explicit contracts.

Current runtime overlap still uses geometry-key uniqueness and slot-first point
selection. The named-grid plan owns the selected change: add a differently named
replacement before removing the old registration, or remove first to reuse the
same Name. Failed re-add does not restore a removed registration. No names,
priorities or effective-world composition have been implemented by this planning
pass.

- [Cross-Stack Issue Resolution](issue-tracker.md)
  - Resolve defects in dependency order: FixedMathSharp, SwiftCollections, then
    GridForge, followed by affected Trailblazer, Gravitas and adapter consumers.
    Package references remain the default. Use `UseLocalLsfStack=true` and
    global `DisableTransitiveProjectReferences=true` for coordinated validation
    of unreleased sibling changes; serialize shared-output builds and record the
    exact source graph.
- [Benchmark Signal Hardening](benchmark-signal-hardening-backlog.md)
  - Reproduce measured signals at the existing owner and compare the complete
    affected operation. Preserve adverse and inconclusive results; do not turn
    tracker maintenance into speculative optimization work.

## Deferred Design

The
[coordinate-range and sector plan](grid-coordinate-ranges-and-sectors-plan.md)
starts with numeric admission. A sector hierarchy, global coordinate replacement
or live-origin rebasing remains conditional on Phase 0 scenario selection and
cross-stack ownership evidence; no such runtime representation is selected yet.

The [named-grid plan](named-grids-and-priority-lookup-plan.md) uses the
three-grid, shared-cell example as its first proof. Exact partial-cell
composition and the relationship between named navigation and physical query
scope require Phase 0 contracts before broader coverage can be claimed. A
point-only implementation is not completion of its effective-world requirement.

Richer partition geometry/inspection and additional procedural algorithms remain
inside their owning plans' explicit expansion gates. Promote further broad work
into feature plans only for a concrete consumer and acceptance criteria. Keep
completed evaluations archived rather than treating unselected phases as pending
implementation.

## Recently Completed

- [World Observation Lifetimes](done/world-observation-lifetimes-plan.md)
  - Moved exact diagnostic cell notifications onto the owning world and closed
    GF-Issue-018 and GF-Issue-019 for foreign occupant mutations and recycled
    voxel subscribers.

- [Runtime Counter Lifetimes And Exhaustion](done/runtime-counter-lifetimes-plan.md)
  - GridForge admits mutations only when their committed change stamps fit.
    Exhausted worlds can release their grids, and interrupted blockers retain
    ownership of surviving attachments. The audit verified the existing 64-bit
    identity guards and documented the limits of wrapping 32-bit revisions.

- [SwiftCollections v9 Adoption Evaluation](done/2026-11-24-swiftcollections-v9-adoption-plan.md)
  - Retained SlotMap occupancy after DenseTicketMap, TicketStore and
    cached-position comparisons, and existing 3D discovery after the
    projected-index comparison. Ordering repair, mutation costs and retained
    memory defeated the tested alternatives. The subsequent DenseTicketMap
    provider investigation also closed without runtime changes after broader
    miss controls exposed tradeoffs. The record preserves scoped results, limits
    and reopening criteria.

- [Two-Tier Grid Spatial Index](done/2026-08-03-two-tier-grid-spatial-index-plan.md)
  - World-owned indexing keeps ordinary grids in the fixed spatial hash and
    routes oversized grids to a secondary fixed BVH. Lookup, overlap, neighbor
    discovery and traversal preserve exact filtering and deterministic results
    without registering extreme bounds across an unbounded hash-cell volume.
    Downstream validation and release-consumption limits remain in the record.

- [Runtime Identity Hardening](done/2026-07-17-runtime-identity-hardening-plan.md)
  - World, grid-generation, blocker and occupant-registration identities reject
    stale references across pooled reuse, same-slot replacement and multiple
    live worlds. Runtime allocation identities remain separate from geometry,
    deterministic ordering and serialized simulation state.

- [Grid Diagnostics Geometry](done/2026-06-14-grid-diagnostics-geometry-plan.md)
  - Engine-neutral descriptors, topology geometry and dirty-change data support
    dense/sparse rectangular and hex grids. Missing sparse address cells remain
    explicit, bounded and non-materializing; adapters own rendering and caches.

- [Hex Prism Grids](done/2026-06-11-hex-prism-grid-plan.md) and
  [Topology Follow-Up](done/2026-06-13-hex-prism-follow-up-plan.md)
  - Per-grid topology supports flat-top and pointy-top hex prisms alongside
    rectangular grids. Unified contact-neighbor queries handle mixed topology;
    directed lookup retains topology-local directions and deterministic
    ordering.

- [Sparse Voxel Storage And Runtime Mutation](done/2026-06-11-sparse-voxel-grid-plan.md)
  - Bounds define an address space while only configured voxels exist. Explicit
    runtime add/remove preserves occupancy, blocker, partition and neighbor
    lifetimes; sparse holes remain absence rather than empty dense voxels.

- [Vector2d Query APIs](done/2026-06-11-vector2d-query-api-plan.md)
  - XZ inputs and an explicit world-Y layer provide planar query and mutation
    entry points over the same world, grid, voxel and identity contracts.

- [Explicit GridWorld Ownership](done/gridWorldRefactorPlan.md)
  - Replaced process-wide ambient world state with explicit world ownership for
    registration, tracing, blockers, occupants and scans. Multiple live worlds
    remain isolated without compatibility shims or engine dependencies.

## Deferred / Evidence-Gated

The completed
[SwiftCollections evaluation](done/2026-11-24-swiftcollections-v9-adoption-plan.md)
owns the following reopening criteria. These are conditional investigations, not
selected migrations or promises to add another backend.

- **Packed Occupancy And Optional Ticket Stores**
  - Reopen only for a concrete consumer profile and a simpler representation
    that preserves public slot order, full generation safety and lifecycle
    behavior while improving the complete operation. Include mutation/ordering
    repair, hit/miss mixes and actual retained storage. Optional stores require
    a real external issuer and payload owner; do not manufacture an issuer or
    narrow GridForge's public long generations to fit a container.
- **Projected Planar Discovery**
  - Require an existing operation where candidate discovery is a measured cost
    and a projected index pays for registration, removal, reset and retained
    storage across flat, stacked and mixed workloads. Preserve exact vertical,
    topology and sparse-presence filtering, candidate budgets and ordering.
- **Fixed-Root Quadtree Backend**
  - Require a concrete bounded planar consumer with explicit root ownership and
    repeated regional-query benefits over the current discovery and native
    hash/BVH alternatives. Dynamic conjoined worlds alone do not justify a third
    selectable backend or a new world-bounds contract.

## Recommended Execution Order

1. Audit coordinate admission and settle named-grid identity/replacement and
   effective-query scope. Use
   near-limit and small overlap fixtures rather than long soaks or huge grids.
   Record supported behavior, hypotheses and confirmed defects separately.
2. Complete one optional partition producer and actual diagnostic consumer.
   Preserve exact-type lookup, bounded output, pooled lifetimes and independent
   navigation-proof invalidation; review the first slice before expansion.
3. Prove named configuration and one composed overlap scene, then carry the same
   visibility/scope contract through world coverage, navigation, physics and
   tools. Preserve explicit grid access and resident state; do not declare
   completion from only a point-query priority sort.
4. Prove the bounded procedural recipe and existing sparse registration path.
   Follow the accepted named-configuration contract when it lands. Compare exact
   layouts, generation/materialization costs and retained storage before adding
   branching, room chunks or adapter preview.
5. Select large-world sector representation only if the coordinate plan's
   concrete consumer requires it. Prove one seam/query/continuation cycle across
   affected GridForge, Trailblazer, Gravitas and adapter owners before
   broadening.
6. Validate accepted changes for both target frameworks and Standard/Lean
   variants, preserve strict full-coverage and relevant consumer gates, and
   record exact source graphs and results. Coordinate affected CI source pins
   after accepted upstream work lands; preserve existing CI policy.
7. Close and archive completed plans, including explicit no-change/defer
   decisions. Keep trackers for reproduced defects and measured signals, and
   update current guides/XML for adopted behavior. Release validation remains
   separately scoped and uses the released dependency chain before consumers.
