# Named Grids And Priority-Based Position Lookup Plan

**Opened:** 2026-11-26

## Purpose And Status

**Status: planned; user-facing selection rules agreed, implementation not
started.** Add named immutable grid configurations and deterministic overlap
precedence. This includes a coherent effective world view for automatic queries;
a point-only priority sort is an intermediate slice, not completion of the
requested feature. No runtime change, probe, test or measurement is claimed.

The source audit used GridForge `ea52599521f3b86a80d6c3c5d1f2dacb36fc6c82`.
Refresh affected providers and consumers before implementation. Keep design
decisions and phase evidence here; confirmed defects belong in their owning
tracker rather than being inferred from a proposed behavior change.

## Agreed Contract

1. An authored Name is optional. Omitted/null means derive a Name once from the
   normalized definition; an explicitly provided Name must be nonempty and
   non-whitespace. Every active grid has a unique resolved Name within its
   GridWorld, using ordinal case-sensitive comparison without silent trimming.
2. Requested Name and integer Priority belong to an immutable authored
   configuration; the resolved Name is immutable for the registration. There are
   no live setters. Changing those values creates a new configuration and
   registration; the host removes the old registration explicitly.
3. Automatic selection chooses the highest-priority containing grid, then the
   lowest live GridIndex. Priority defaults to zero; compare without subtraction
   so extreme signed priorities remain ordered correctly.
4. Selection happens before checking voxel presence. A sparse hole in the
   winning grid means absence, with **no fallback** to lower grids. Blocking,
   full occupancy or missing navigation data likewise must not trigger fallback.
5. Explicit named-grid access selects that registration directly, without
   retargeting to a higher-priority grid. Exact runtime identities continue to
   validate world and grid generations; reusing a Name cannot revive a handle.
6. Different resolved active Names may share the same normalized geometry. This
   refines the earlier geometry-only duplicate restriction; it does not allow
   the same active Name twice. A changed priority alone does not permit a
   duplicate Name.
7. Automatic multi-cell/world operations must respect overlap precedence rather
   than independently emitting every coincident lower-grid representation.
   Internal candidate inspection is allowed and must be budgeted; it is not the
   same as publishing or processing duplicate effective cells.

Equal-priority results remain deterministic for the same ordered lifecycle. They
are not guaranteed invariant under a different registration/loading order, since
GridIndex slots are reusable. No persistent ordering counter is selected.

## Running Example And Meaning Of Precedence

Three differently named grids A, B and C contain the same three cell locations.
Their priorities are 0, 10 and 20. An unqualified point query at a shared
location selects C. It returns C's voxel if present, or no voxel if C has a
sparse hole. A named lookup of A still reaches A's resident data. Removing C
reveals B according to the same rule; no lower data needs to be deleted to hide
it. Equal priorities use the lower live grid slot.

A world trace covering those three shared locations must not report nine
independently active cells. It observes the effective winners under its stated
coverage contract. A longer query can legitimately cross several differently
owned regions and return many cells: precedence is per location, not a promise
of one voxel for an entire line, radius, box or body.

Keep two concepts explicit:

- **Resident state:** the actual registered grids, physical cells, occupants,
  partitions and obstacles. Hidden state remains addressable for inspection,
  mutation and teardown through explicit ownership.
- **Effective world view:** what an automatic query sees after priority and
  sparse-hole masking. Hidden lower state must not be counted as another active
  representation solely because it remains stored.

This corrects the earlier point-only scope proposal. It does not authorize
silently deleting lower state, moving occupants, or treating every independent
physics body as voxel geometry.

## Existing Owners And Required Changes

| Owner                                                                                                                                                                                                                                                              | Current behavior                                                                           | Planning consequence                                                                                                                             |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| [GridWorld](../../src/GridForge/Grids/Managers/GridWorld.cs) and [GridSpatialIndex](../../src/GridForge/Grids/Managers/GridSpatialIndex.cs)                                                                                                                        | Point candidates sort by slot; registration binds one normalized geometry key to one grid. | Add an auxiliary name-to-slot index and numeric priority selection; resolve geometry-binding multiplicity without converting hot paths to names. |
| [GridConfiguration](../../src/GridForge/Configuration/GridConfiguration.cs), [GridConfigurationKey](../../src/GridForge/Configuration/GridConfigurationKey.cs) and [NormalizedGridConfiguration](../../src/GridForge/Configuration/NormalizedGridConfiguration.cs) | Value-only configuration and geometry key; normalization reconstructs configuration.       | Preserve all authored fields and separate geometry/congruence from named registration identity.                                                  |
| [GridTracer](../../src/GridForge/Utility/GridTracer.cs), [GridScanManager](../../src/GridForge/Grids/Managers/GridScanManager.cs), [Blocker](../../src/GridForge/Blockers/Blocker.cs)                                                                              | Traverse independent grids/voxel buckets and expose existing multiplicity.                 | Define effective coverage and mutation scope; do not infer that sorting grid candidates composes shapes.                                         |
| [Navigation body coverage](../api/guides/Navigation-Body-Coverage.md)                                                                                                                                                                                              | Exact pinned generations, congruent-prism alternatives, bounded work and reusable proofs.  | A higher sparse hole cannot be filled by a hidden lower OR alternative in an automatic composed proof.                                           |
| Trailblazer Navigator occupancy and map/graph binding                                                                                                                                                                                                              | Position resolution is separately composed; graph keys bind one geometry to one map.       | Preserve caller-specific prism checks, actual registered identities and explicit map scope; migrate binding identity deliberately.               |
| Gravitas partitions and ray/overlap/sweep/contact/CCD paths                                                                                                                                                                                                        | Broad-phase membership covers grids; narrow phase tests complete collider geometry.        | Prove actual consumer behavior; voxel/partition filtering alone is not proof of masked collider geometry.                                        |
| GridForge-Unity diagnostics/picking                                                                                                                                                                                                                                | Raw diagnostic traversal and picking have independent ordering.                            | Keep hidden-state inspection and add clearly specified effective selection rather than accidentally changing one picker.                         |

## Immutable Configuration, Names And Replacement

The public API shape remains to be chosen: Name/Priority can be represented in
an immutable definition extending the present configuration or an explicit
registration definition. Either choice must honor the agreed authored values; do
not hide a mutable metadata layer behind configuration syntax.

### Optional Name Index And Derivation

Use a world-owned `SwiftDictionary<string, ushort>` mapping resolved Name to the
existing grid slot. The grid can expose its resolved Name; no reverse dictionary
or bidirectional framework is required. Resolve a public name lookup once, then
use the existing numeric slot/generation path. Ordinary spatial lookup, voxel
identity, tickets and trace payloads must not become string joins. Keep removal,
failed admission, reset and pooled reuse coherent with the name index.

Generate an omitted Name after normalization, only at registration/definition
creation. Choose the exact unnamed-definition identity fields before
implementation, including Priority. Current GridConfigurationKey includes
normalized bounds/topology/metrics but excludes storage, scan size and sparse
contents; explicitly decide which additional fields distinguish unnamed
registrations. Do not scan large sparse payloads merely to label a grid without
a demonstrated need. Callers can supply a Name when they need distinct
registrations beyond the selected unnamed-definition identity.

Prefer a versioned, unambiguous canonical encoding of exact values: fixed-order
raw fixed-point components and invariant integer fields. Do not derive identity
from `GetHashCode`, default floating/string formatting, runtime allocation
tokens, reusable slots, clock time or order-dependent suffixes. A compact digest
requires comparison against the full definition and explicit collision handling;
a hash alone is not a uniqueness proof. Name generation/storage cost is cold
registration work and belongs in retained-byte measurements, not each query.

The same unnamed normalized definition generates the same Name and is rejected
as a duplicate while active. Changing Priority changes the derived definition
and Name. Explicit/generated Name collisions also reject deterministically;
there is no automatic retry suffix. Define naming-version persistence and
reconstruction before promising that generated Names remain stable across future
format revisions. Reusing any Name never revives an old runtime handle.

This index is a public convenience route, not a replacement for internal world
ownership. Named lookup and direct grid inspection reuse existing grid-local
APIs; avoid redundant convenience wrappers without a real caller.

The following replacement behavior is explicit:

- Add-new/remove-old may coexist when the replacement has a different resolved
  Name, explicit or generated, even when geometry matches. Both publication
  steps are observable; the priority/slot rule applies between them. No atomic
  replacement is implied.
- The same Name cannot coexist. To reuse it, remove the old registration first;
  failure and the observable gap follow ordinary lifecycle rules. An atomic
  same-name replacement API is not selected.
- The new configuration can win immediately, or the old one can keep winning
  until removed, depending on their priorities and slots. There is no automatic
  transfer of old occupants, partitions, blocker tokens or user objects.
- In the differently named add-first workflow, failed admission must leave the
  old registration active and must not publish a partial replacement. In the
  same-name remove-first workflow, failed re-add leaves an observable gap; it
  does not automatically restore the old registration or its runtime identity.
  Define duplicate Name versus invalid configuration outputs before
  implementation; never silently return a different named grid as success.

Geometry equality remains necessary for congruence and exact coverage, even if
it no longer uniquely identifies a world registration. Audit BoundsTracker,
covered-address generation binding, configuration filters and Trailblazer's
one-key/one-map tables. Merely removing the duplicate-key check is insufficient.
Keep hot binding identities numeric; the existing one-geometry-key/one-grid
assumption still needs a deliberate multiplicity/binding change, but that does
not require strings in geometric keys or every trace record. Review equality,
hashing, canonical ordering, reference retention and downstream wire formats.

Current GridConfiguration is a readonly value-only MemoryPackable struct. Adding
a string makes it managed and can change generated MemoryPack behavior; its
current XML distinguishes unmanaged restoration from JSON construction. Specify
JSON, Standard MemoryPack, Lean JSON, default construction, old-record
acceptance or rejection, normalization and event snapshot semantics
deliberately. Configuration serialization is not a live-world snapshot or
permission to restore runtime allocation identities.

## Selection Ownership And Cost

Current position lookup does not iterate every resident grid. GridSpatialIndex
collects nearby ordinary-grid candidates through its fixed spatial hash and
oversized-grid candidates through its fixed BVH. It sorts the combined local
candidate list by grid slot; GridWorld then loops until a candidate contains the
point. Large bounds queries can deliberately use an active-grid scan when
traversing the covered hash-cell volume would cost more; retain that existing
bounded-cost choice unless measured evidence supports a replacement.

Put priority selection at the existing GridWorld/GridSpatialIndex owner. Compare
immutable numeric Priority, then GridIndex, over the k local candidates. A
one-pass winner reduction is O(k) and needs no extra priority sort; the whole
existing collection path is not claimed O(k), since it currently sorts by slot
and the broad-phase cost varies. Check only the selected grid for voxel
presence; a higher sparse hole still masks lower grids.

Keep ActiveGrids as live/resident membership. A lower grid may be visible on one
side and hidden under a smaller overlay on the other; globally deactivating it
would discard its exposed contribution and break explicit access. Selection is
regional, not a single active/inactive flag per grid. Immutable bounds and
priorities let derived owner metadata be maintained at grid add/remove rather
than rebuilt every frame. Sparse presence changes affect query results and
proofs, but do not by themselves change the winning bounds/priority owner under
the no-fallback rule.

Do not add a second active-grid manager, per-priority backend or per-cell winner
cache as a prerequisite. Measure local overlap counts, complete queries,
registration/removal and reveal costs first. If needed, evaluate priority-aware
bucket/node summaries or a regional effective-view index at the current owner;
put a proven reusable capability in SwiftCollections rather than copying an
index. Such pruning or precomputation must account for memory, mask updates,
partial coverage, invalidation and hard work limits. Inspecting several local
candidates is not duplicate effective-world output or proof of a performance
problem, and worst-case overlap can still include every registered grid.

## Effective Query Scope And Geometry Gates

| Surface                                                       | Required contract                                                                                                                                                                                                                          |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Unqualified point grid/voxel lookup                           | Highest priority, then lowest slot; selected-grid holes remain absence. Paired 2D/3D APIs agree; failure may retain the selected outGrid and null voxel.                                                                                   |
| Named-grid/direct identity lookup                             | Resolve only the requested resident registration; stale identity never retargets by Name. Explicit access does not by itself decide automatic collision participation.                                                                     |
| World lines, bounds, contacts, radius scans and body coverage | Respect effective priority regions, exact boundaries, documented ordering and bounded work; do not expose a second lower representation in a masked region.                                                                                |
| Explicitly bound navigation/grid queries                      | Preserve binding and never silently switch the Navigator or map to another grid. Settle whether the operation uses a pinned-grid view or validates against the effective world; physics and navigation must use a coherent declared scope. |
| Diagnostics                                                   | Distinguish raw/all-grid inspection from effective-world picking/output. Hidden data must remain debuggable.                                                                                                                               |
| Persistent world blockers and occupancy mutations             | Define target scope and reconciliation explicitly; query visibility must not silently delete or transfer resident state.                                                                                                                   |

Start the first proof with the user's three-grid/three-shared-cell example and
aligned rectangular cell layouts. This is a representative test scene, not an
unannounced rejection of all existing mixed topology or a claim that arbitrary
partial overlaps are solved.

Before broader coverage implementation, define the ownership domain and seam
convention. Current registered bounds describe centers, while exact cell prisms
extend half a cell around centers and tracers expand candidate bounds. Bounds,
cell centers and complete cell geometry are not interchangeable masks.

If a higher grid covers only part of a lower cell, dropping the lower cell by
its center can lose exposed geometry; retaining the full cell can keep hidden
geometry. Different metrics or rectangular/hex overlap may require clipped
intervals/regions or explicit support limits. Do not implement global precedence
with a whole-cell or whole-scan-cell center filter. Select the representation,
admission or incomplete-result policy before claiming support for those cases.
Keep repeated fragments/output and all mask discovery within hard work limits.

## State, Publication And Consumer Safety

Adding/removing a higher grid changes effective visibility even if the lower
grid's version and last sequence remain unchanged, or the upper grid is entirely
sparse. Reuse the existing world mutation/publication owner where possible.
Define effective-view invalidation for retained results; a proof stamped only
against unchanged lower B cannot ignore a new masking A. Do not invent another
counter framework before auditing existing run/change stamps.

Preserve existing synchronization and borrowed-storage contracts. A new
selection loop does not itself permit concurrent mutation, and blindly adding
nested world locks can violate existing lock ordering or recursion rules. A
captured operation must finish against its declared frozen view or report
stale/incomplete. Budget mask discovery even when a higher mask has no eligible
navigation map. Filtering to selected semantic maps before finding mask owners
must not accidentally restore lower geometry in an automatic world view.

Retain actual registration identities for removal and reconciliation. A
stationary Navigator or collider can be affected by overlay add/remove; skipping
all work because its position is unchanged is not a complete update policy.
Re-resolving an old coordinate does not reliably identify its former owner.
Named explicit navigation remains pinned unless the host selects a transition;
phase 0 must settle how its physical query scope matches that binding.

For scans, decide whether effective visibility follows the registered voxel,
current occupant position or visible registration portions. Preserve documented
registration multiplicity; do not deduplicate every occupant by Guid to conceal
incorrect ownership. For persistent blockers, choose all-resident maintenance
versus effective-only application plus reveal reconciliation. Hiding a voxel
must not silently clear its obstacle state or orphan a token.

Gravitas owns collider behavior. A collider crossing a visibility seam can be
found through an exposed partition and then tested as a whole shape in a masked
region. Changing partition membership or filtering collider centers is not a
complete physics composition policy. Define actual grid-authored geometry
ownership versus independent dynamic bodies, then verify rays, overlaps, sweeps,
contacts and CCD in 2D, 3D and mixed paths. Do not delete a host-owned body
merely because one partition becomes hidden, or claim geometric masking from
broad-phase candidate changes alone.

## Implementation Phases

### Phase 0 — Settle And Prove The Contract

- [ ] Refresh the source/consumer ledger and freeze ordinary and overlapping
      baselines. Define optional Name/default construction and canonical
      derivation, priority defaults, replacement failure outputs, binding
      identity and configuration wire behavior.
- [ ] Document automatic versus explicitly named/map-bound query scopes,
      physical/navigation coherence, ownership domain/seams and supported
      initial geometry. Treat these as preimplementation decisions, not hidden
      behavior chosen by traversal order.
- [ ] Prove expected outcomes for three aligned grids and three shared cells:
      priorities, equal-priority slots, sparse holes, blocked/full winners,
      named access, shared-geometry replacement and overlay removal/reveal.
- [ ] Add independent partial-cell, differing-metric, hex and seam-spanning
      collider counterexamples. Select exact support or explicit admission/
      incomplete behavior; do not promise general clipping without an owner and
      useful consumer. Record dispositions and reopening criteria.
- [ ] Measure candidate discovery, complete queries and containing consumer
      frames before tuning. Set budgets/thresholds from those baselines; do not
      assume duplicate suppression makes the complete workload faster.

### Phase 1 — One Named Configuration And Composed Core Slice

- [ ] Implement immutable definitions, unique Name admission/routing and
      unambiguous registration/binding identity. Verify add/remove/reset/pool
      reuse and failed replacement without publishing stale Names or priorities.
- [ ] Prove one shared winner authority through point aliases and the selected
      aligned line/bounds/scan paths. Preserve explicit resident access and
      distinct closest-query semantics unless deliberately revised.
- [ ] Add focused production regressions, then obtain independent first-slice
      review of geometry, ordering, callbacks, memory, runtime growth and
      superseded paths. Point-only success does not complete this feature.

### Phase 2 — Integrate Effective Coverage And Real Consumers

- [ ] Carry the selected scope through bounded contact/body proofs and both
      dimensional counterparts. Automatic composed proofs must honor opaque
      sparse holes and masking grids that lack semantic metadata. Preserve
      budgets and exact endpoint rules.
- [ ] Coordinate Trailblazer binding, occupancy/committed-cell/profile checks,
      immediate/scheduled acquisition and retained-proof continuation. No silent
      grid switch; use actual old identities for cleanup.
- [ ] Coordinate Gravitas coverage/membership and its selected geometry policy;
      prove seam-spanning bodies and query/contact/CCD consistency rather than
      changing only partition collection. Match host phase barriers.
- [ ] Prove diagnostics/picking, world blockers and occupancy reveal behavior.
      Follow add-new/remove-old through both observable states, failure,
      callback reentry and reset/deactivation without losing resident data.

### Phase 3 — Validate And Close

- [ ] Build GridForge netstandard2.1/net10.0 in Release/ReleaseLean, run focused
      and full net10.0 suites and the existing fresh strict 100% reported line,
      branch and executable-method coverage gate. Preserve CI policy and all
      applicable consumer tests, replay/serialization and allocation gates.
- [ ] Use `UseLocalLsfStack=true`, global
      `DisableTransitiveProjectReferences=true` and serial shared-output builds
      for coordinated sources. Record exact revisions, patches and loaded
      benchmark payloads. Validate providers before affected consumers.
- [ ] Measure setup/replacement, mask queries, sparse mutation, reveal,
      repartitioning and teardown, including retained/peak storage and actual
      hidden-candidate work. Use bounded comparable runs; inspect complete
      case/child results and preserve adverse/inconclusive outcomes.
- [ ] Update XML, named/overlap/sparse/coverage guides, configuration migration
      and adapters for adopted behavior. Record exact evidence, explicit limits
      and rejected/deferred designs here; independently review final source.
      Remind maintainers to align CI source pins after upstream lands. Release
      readiness remains separate unless explicitly requested.

## Completion And Reopening

Complete only when accepted named configuration, default selection and the
selected effective-world/explicit-scope contracts agree through real consumers.
Report unsupported geometry or physical scope explicitly; do not close a broad
composition claim using only point tests. Expansion to arbitrary partial-cell
composition or another query scope requires a concrete consumer and exact
representation/verification evidence. No broad layer-mask framework, mutable
priority/rename API or atomic replacement is selected by this plan. Leave all
changes unstaged/uncommitted and keep phase evidence in this record.
