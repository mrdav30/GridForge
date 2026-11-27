# Partition Diagnostics Plan

**Opened:** 2026-11-26

## Purpose And Status

**Status: planned; source review only. No implementation, tests or measurements
have been performed for this plan.**

Allow tools to inspect meaningful partition data through optional,
renderer-neutral diagnostic output. Keep ordinary partition lookup lightweight,
and make attachment/content invalidation explicit so cached overlays do not
silently display stale partition state.

The audit baseline is GridForge `ea52599521f3b86a80d6c3c5d1f2dacb36fc6c82`.
Refresh affected callers and provider contracts before implementation. The
[world observation lifetime plan](world-observation-lifetimes-plan.md) owns the
world-local subscription boundary; complete that boundary first if the selected
partition dirty path depends on it. Neither plan has changed runtime code,
consumer projects or dependency pins.

## Existing Capabilities And Gap

| Owner                                                                                    | Current behavior                                                                                                                                                                                                                     |
| ---------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `src/GridForge/Spatial/IVoxelPartition.cs`                                               | Defines parent identity and attach/remove callbacks, without rendering or diagnostic obligations.                                                                                                                                    |
| `Spatial/PartitionProvider.cs`                                                           | Stores exact concrete type keys, preserves registration order and optimizes the first two types inline. Its enumerator is internal. Generic interface/base lookup does not discover implementations.                                 |
| `Grids/Nodes/Voxel.cs`                                                                   | Retains partitions and runs lifecycle callbacks. Add failure removes the stored entry but cannot undo host object side effects; removal remains successful if its callback throws. No partition diagnostic dirty event is published. |
| `Diagnostics/GridDiagnostics.cs`, `GridDiagnosticCellState.cs`                           | Emit `Partitioned` presence and can resolve a physical descriptor to its live voxel. A caller knowing the concrete type can already inspect it with `TryGetPartition<T>`.                                                            |
| `Diagnostics/GridDiagnosticSession.cs`                                                   | Captures grid, obstacle and occupant changes, not arbitrary partition payload mutation. No existing dirty-session contract promises to observe those fields.                                                                         |
| GridForge-Unity `Build/Base/Runtime/Utility/Debugging/GridDiagnosticUnityVisitor.cs`     | Colors partitioned cells; does not export partition-specific values.                                                                                                                                                                 |
| Gravitas `src/Gravitas/Partitions/3D/PhysicsPartition.cs` and `2D/PhysicsPartition2D.cs` | Real partition consumers expose dynamic, awake-dynamic, kinematic and static counts. They provide a useful first diagnostic payload and dimensional control.                                                                         |

Unprefixed source paths above are under `src/GridForge`. The missing content
surface is a feature opportunity, not a confirmed existing correctness defect.

## Selected Boundary And Non-Goals

Keep `IVoxelPartition` small. Do not add mandatory draw methods, Unity types,
materials, colors, cameras, meshes or reflection-based field dumping to every
partition. GridForge owns identity, bounded inspection and observation
mechanics; the partition owner interprets its payload and the adapter renders
it.

Prefer an optional diagnostic source/writer contract or concrete-type exporter
at the existing diagnostic owner. Choose between these at the first contract
audit using one real producer and consumer. Do not add both registries and
interfaces speculatively. `TryGetPartition<ISomeDiagnosticInterface>()` is not a
valid discovery implementation under the current exact-key provider contract.

The first useful case is a physics-partition count overlay through the existing
Unity diagnostic traversal, retaining 2D/3D distinctions in the producer. A
custom partition without diagnostic support must continue to attach and work
unchanged. Consumer changes are explicitly coordinated follow-up work, not
already supplied by this plan. Keep the core engine-agnostic.

No world clock, generic inspector framework, new partition storage, physics
index, navigation payload or serialized partition schema is selected. A new
public extension contract can serve host-owned partitions, but must be proven
with this concrete diagnostic path before expansion.

## First Slice And Decisions To Settle

1. Capture one currently partitioned physical cell using `GridDiagnostics`,
   resolve its current identity, and export a small copied count payload from
   the real owner into caller-owned storage. Render/inspect it through the
   existing adapter path; do not invent a parallel cell traversal.
2. Select the minimal data schema and extension point. Define stable semantic
   field identity, value representation and ordering separately from display
   labels. Avoid per-query type-name/string allocation and storing live payload
   references in supposedly immutable output.
3. Define a bounded result contract: clear versus append, storage ownership,
   maximum inspected partitions and emitted records, insufficient-capacity
   status, callback failure and whether partial output is valid. A cell-count
   limit alone does not bound per-partition work or arbitrary exporter code.
4. Choose an explicit publication owner for successful attach/detach and
   partition-owned content changes. Ordinary `Voxel` currently has no owning
   world reference; its mutable `WorldIndex` cannot safely route through a new
   hidden global world lookup. Evaluate owner-aware mutation or explicit
   observer invalidation before adding object references to every voxel.

Record each selection with its implementation consequence before expanding.
Until then, callback ownership, concrete public API names and data schema are
open decisions, not promises of a supported API.

## Lifetime And Invalidation Contract

- Resolve physical cells with world/grid generation checks. Sparse holes have no
  partitions and must not materialize storage during inspection.
- Distinguish attachment change from content change. GridForge can observe its
  own successful mutations; arbitrary payload writes require the owner to mark
  diagnostic state dirty or provide a defined diagnostic revision.
- Diagnostic dirty state must not advance obstacle/change stamps used to prove
  navigation validity merely because a visual counter changed. No automatic
  physics/navigation invalidation is implied by the new diagnostic channel.
- Do not publish a successful attachment notification for a failed add callback.
  Preserve existing nontransactional host side effects and detach-before-remove
  callback semantics. Reset/removal can supersede dirty records with their
  existing broader identity invalidation.
- Attachment identity and borrowed provider/voxel storage expire at detach, grid
  removal, reset or pooled reuse. Hosts may retain and reuse partition objects,
  but the reference alone does not prove the original attachment or parent
  binding is still valid. Copied output owns only the facts its schema
  explicitly copies; a shallow reference is not a durable snapshot.
- Never invoke arbitrary exporter/adapter callbacks while holding partition or
  grid mutation locks. Specify a caller-quiescent read boundary or a validated
  capture protocol; do not claim safe concurrent mutation without proving it. Do
  not manufacture per-partition pinning or cloned object graphs by default.
- Define non-reentrant scratch use and exporter read-only obligations. Reject or
  document unsupported reentry; if mutation/reentry is supported, test exact
  reset/removal/exception behavior at that callback boundary.
- Clear reference-bearing scratch/output entries at their documented lifetime
  boundaries, including failure and disposal. Retain capacity deliberately and
  count it; do not retain detached partitions just to simplify an overlay.

## Verification And Cost

Extend existing tests beside their owners:

- `tests/GridForge.Tests/Grids/Voxel.Tests.cs`: exact type/reference lookup,
  boxed-value partitions, duplicate attachment, failing callbacks and detach.
- `tests/GridForge.Tests/Diagnostics/GridDiagnosticsPhysicalQueryTests.cs`:
  physical/sparse cells, `Partitioned` state, ordering, stale descriptors,
  filters and warm allocation.
- `tests/GridForge.Tests/Diagnostics/GridDiagnosticSessionTests.cs`: dirty
  coalescing, world isolation, reset supersession and disposal during earlier
  callbacks.
- Add partition-specific controls for multiple types, unsupported exporters,
  empty/one-below/exact output capacities, per-partition work limits, content
  change without attachment change, stale pooled payloads and callback failure.
  Verify diagnostic-only changes leave unrelated navigation proof stamps alone.
- Run the actual producer and adapter path: 2D/3D physics counts, add/remove and
  sleeping/awake changes, and Unity enable/disable, world replacement and
  destruction. Apply affected repositories' own tests and coverage policies.

Reuse the existing grid-diagnostics and partition lookup/allocation scenarios.
Compare diagnostics disabled, repeated unchanged capture, dirty bursts, many
partition types and reset/teardown. Record copied output bytes, retained
scratch, exporter/dispatch costs and complete adapter refresh costs. Measure
actual runtime layout; do not transfer payload-byte constants from unrelated
stores.

Build core `Release` and `ReleaseLean` for `netstandard2.1` and `net10.0`, then
run focused and complete net10.0 suites. Collect fresh assembly-specific
coverage with existing runsettings and require exact 100% reported line, branch
and executable-method coverage through
`.assets/scripts/assert-full-coverage.ps1`. No exclusions or rounded-percentage
waivers; retain all affected consumer gates.

For coordinated sources use
`-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1` on
restore/build/test and propagate source selection into benchmark children. One
coordinator owns shared outputs and timings. Record exact revisions and loaded
dependencies; report baseline failures separately from introduced failures.

## Phases And Closeout

1. **Choose the contract:** refresh callers, capture baseline behavior/cost and
   settle the first producer/output schema, discovery method and invalidation
   owner. Stop if the proposed generic framework exceeds the concrete need.
2. **Prove one producer and consumer:** implement the smallest coherent core
   surface and actual optional producer/adapter path. Obtain independent
   `ponytail:ponytail-review` review of output ownership, callbacks,
   invalidation, new files, net runtime growth and deletion opportunities before
   expansion.
3. **Verify and document:** complete adverse lifecycle/budget cases, both
   variants and targets, strict coverage, affected consumer checks and matched
   costs. Update XML and the diagnostic/partition guides for adopted behavior;
   record final decisions and limitations here and review the connected source
   again.

Defer richer geometry/text output until an implemented caller needs it. Reopen
mandatory partition-interface changes only if optional extension cannot express
a demonstrated contract. Implementation and verification close this plan;
release readiness is separate. Validate provider before consumer and remind the
maintainer to align CI source pins after upstream changes land. Leave changes
unstaged/uncommitted and keep phase evidence in this plan.
