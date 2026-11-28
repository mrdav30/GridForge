# World Changes and Composition

Use immutable committed changes and bounded value cursors when integrating an
addressed navigation map, streamed content, or another stateful consumer.
Runtime slots remain recyclable; configuration keys, generations, and revision
fences define what each capture describes.

## Ordered committed changes

`GridWorld.OnChangeCommitted` is the storage-neutral feed for systems that must
reconcile grid state without rereading mutable live objects. The world assigns an increasing
`GridChangeStamp` to each published grid lifecycle, sparse-presence and obstacle
change. The matching exact obstacle event and
generic committed event carry the same cause ID and immutable post-mutation
counts. Handlers run after the mutation locks are released, and reentrant
mutations are queued and delivered in ascending sequence order.

Consumers that install addressed metadata subscribe once to
`GridWorld.OnChangeCommitted`, then capture requested addresses with
`TryCaptureNavigationBaseline(...)` inside their deterministic maintenance
protocol. The request supplies the exact normalized `GridConfigurationKey` and a
strictly ascending, unique span of topology-local `VoxelIndex` values. The
baseline returns only those addresses, the exact active grid generation, sparse
presence, obstacle count, the grid-local `GridLastChangeSequence`, and the world
`CapturedChangeSequence` used by the feed. The grid's last-change sequence
changes only with that grid and lets a chunked consumer detect target-grid
mutation without restarting for unrelated world churn. Apply baseline state
first, then only matching committed events whose sequence is greater than that
captured change sequence. Neither baseline capture nor the committed feed
enumerates unrelated grids or unrequested physical voxels.

`GridIndex` is included for diagnostics; the world token, grid generation, and
normalized configuration key are the identity boundary.

Consumers that already own one committed-change subscription and reconcile at a
deterministic maintenance boundary can use
`ExecuteNavigationMaintenanceSnapshot(...)` to detach their fixed event prefix
and capture all required address baselines against one frozen world state. Keep
the callback short and non-mutating; it runs while GridForge excludes grid and
voxel mutations. `TryCaptureNavigationBaseline(...)` is safe inside that
callback and does not recurse through the world locks.

### When the change sequence is exhausted

The last representable sequence is `ulong.MaxValue`. Grid registration,
removal, sparse presence changes and individual obstacle mutations throw
`OverflowException` before changing live state if they cannot publish their
required stamps. Duplicate additions and missing removals keep their usual
no-op results. A previously captured snapshot remains usable after a rejected
mutation if its other lifetime checks still pass.

The sequence counts committed changes across the world's lifetime, including
ordinary resets. An in-flight removal or reset can reserve the last available
stamps, so a callback can receive this exception while `ChangeSequence` is
still below `ulong.MaxValue`. If the operation releases unused reservations,
a later mutation can use those stamps. The 32-bit `GridWorld.Version` wraps
through zero and an active `VoxelGrid.Version` wraps to one. Pair retained observations with the owning
world and grid identities and the change stamp required by the query.

Removing a grid requires one removal stamp and one clear stamp for each
blocked voxel. Several tokens stacked on one voxel need a single clear.
Once the grid is detached, direct token additions and removals through a
borrowed grid reference return false. Its teardown owns the remaining clears.
Ordinary reset requires the same clear budget across the world plus a reset
marker. If the stamps are already unavailable, ordinary reset rejects the
request before notifying observers. If an `OnReset` observer spends the
remaining clear capacity, the reserved `WorldReset` marker still invalidates
the entire world. Consumers should discard their per-grid state on that marker.

Explicit deactivation and disposal still release grids, registrations and
subscriptions when ordered teardown cannot fit. `OnReset` is the pre-teardown
lifetime notification. A terminal world can deactivate without publishing a
final committed event. To continue at `ChangeSequence == ulong.MaxValue`,
capture the host's authoritative simulation state at a tick boundary. Create
a new `GridWorld` (its sequence starts at zero), rebuild its grids, blockers
and occupants, and rebind observers using fresh runtime identities and
observation baselines. The host can keep its simulation clock across this
handoff. Dispose the old world after the state transfer.

## Bounded boundary contacts

Exact cross-grid composition uses the same gate through
`GridWorld.BeginBoundaryContacts(...)` and
`GridWorld.AdvanceBoundaryContacts(...)`. Grid registration maintains an exact
cell-prism-envelope BVH plus reciprocal sorted incident pair rows. A two-level
source bitset gives the cursor canonical pair discovery without scanning
recyclable grid slots, and target-envelope-derived address ranges avoid whole-
grid source scans. Upper/lower directory visits, pair selection, source
addresses, and exact target probes are all charged to the caller's candidate
budget. The cursor retains only scalar/value state between chunks and rejects
mixed committed generations as `Stale`.

The key-filtered begin path reads only the selected grid's reciprocal incident
rows, merging incoming then outgoing pairs in canonical order; row and pair
fetches remain separately probe-debited, independent of unrelated pair count.
Its value-only `GridBoundaryContact` output captures both canonical
configuration keys, and `GridBoundaryContactRunStamp` identifies the committed
world revision shared by a multi-cursor batch.

## Bounded address-space traversal

Bounded address-space coverage uses `GridWorld.TryBeginCoveredAddresses(...)`
and `GridWorld.AdvanceCoveredAddresses(...)`. The caller supplies a canonical
list of exact grid generations and independent lookup, address, and output
ceilings. The cursor validates and copies that list before producing results,
then enumerates topology-configured addresses in deterministic X/Y/Z order. This
deliberately includes physically absent sparse addresses so higher layers can
apply their own semantic or materialization rules. Any world or grid generation
mismatch makes the entire run `Stale`; the cursor retains only value state and
supports allocation-free reuse after its fixed capacity is constructed.

## Related guides

- [Architecture Overview](Architecture-Overview.md) for owners and mutation
  flow.
- [Cell Geometry and Navigation](Cell-Geometry-and-Navigation.md) for exact seam
  geometry.
- [Navigation Body Coverage](Navigation-Body-Coverage.md) for revision-stamped
  swept evidence.
