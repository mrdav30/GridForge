# Navigation Body Coverage

GridForge provides two bounded body-coverage contracts. Both publish exact
identities, physical-presence evidence, and revision stamps; neither performs
navigation search or supplies application-specific traversal rules.

| Contract                             | Geometry and selection                                       | Capacity behavior                                        |
| ------------------------------------ | ------------------------------------------------------------ | -------------------------------------------------------- |
| `TraceNavigationBodyInto(...)`       | Upright 3D cylindrical body over candidate grids             | Scratch may grow; caller budgets still apply             |
| `TracePlanarNavigationBodyInto(...)` | Native planar circle/capsule over explicitly selected layers | Existing scratch and output capacities are hard ceilings |

## Swept Navigation Bodies

`TraceNavigationBodyInto(...)` is the non-allocating coverage entry point for a
direct cylindrical body-foot sweep. Callers provide both the result
`SwiftList<GridNavigationBodyTraceCell>` and reusable
`GridNavigationBodyTraceScratch`; there is intentionally no allocating
convenience overload.

The stationary body at `startFoot` must have closed-set contact with the
declared source prism, and the stationary body at `endFoot` must have closed-set
contact with the declared target prism. Exact planar or vertical tangency is
accepted for these endpoint identity checks; a declared endpoint that the body
never contacts fails as `InvalidOrUnrepresentableGeometry` before candidate
work.

The tracer discovers candidate grids from the swept capsule/body bounds, uses
each grid topology's existing covered-address range, creates exact cell prisms,
and retains only positive interior body overlap. FixedMathSharp evaluates the
strict swept-cylinder relation with exact wide intermediates: planar and
vertical interior overlap must coexist at one shared continuous parameter. Mere
tangency or boundary-only coincidence does not claim a neighboring prism. A
direct rectangular diagonal also retains the deterministic four- or eight-cell
corner closure; a hex vertical-planar diagonal retains its source, planar peer,
vertical peer, and target. Results are sorted by durable configuration identity,
grid generation, and topology-local address, so reversing the segment endpoints
does not reverse the published set.

### Union and alternative evidence

The union check starts from the source prism and closes every positive-overlap
neighbor in that topology's exact 26-cell rectangular or 20-cell hex lattice. An
adjacent grid participates only when it issues an exactly coincident, congruent
prism in the same aligned topology lattice, which allows aligned maps to support
one large body across their boundary. A missing outer neighbor rejects a body
that penetrates beyond all returned prisms; exact boundary tangency remains
owned by the interior cell. Unaligned or differently shaped cross-grid coverage
is rejected conservatively. In particular, multiple heterogeneous partial prisms
are not combined as a general CSG replacement for one required source-lattice
prism.

Exact duplicate prisms from overlapping grids are alternatives, not additional
requirements. Distinct source and target identities remain pinned even when
their prisms coincide; for other exact duplicates the certificate prefers a
physically present address and then the stable canonical grid/address order.
When every non-endpoint alternative is missing, one is marked `RequiredCoverage`
and the other missing identities are published as
`PhysicalAlternativeDependency`. Those dependency cells are invalidation-only OR
evidence, not jointly required physical coverage.

Unlike ordinary sparse voxel coverage, this API publishes required missing
addresses. Every result carries canonical world/grid generation identity,
physical presence, role, and the owning grid's last committed change sequence.
The report also carries the world run stamp. `IncompletePhysicalCoverage`
therefore gives callers a reusable negative proof whose complete per-grid
evidence stales when any satisfying alternative changes without staling a trace
that depends only on an unrelated grid.

Pass `excludedGridSpawnTokens` when a caller needs to keep exact grid
generations out of a 3D proof. Supply spawn tokens from the trace's world
and keep the caller-owned set unchanged during the trace. An excluded
generation supplies neither required coverage nor alternative cell evidence.
Its broad-phase grid inspection still counts
against the grid and combined budgets. A replacement grid has a new token,
so a stale exclusion cannot hide it. Source and target grids must remain
eligible.

Pass a reusable `excludedGridCandidates` list to collect the configuration
keys of excluded grids encountered by the same candidate traversal. The trace
clears the list before writing. It retains the list for a coverage miss so the
caller can track its exclusion dependencies. A budget or other early abort
can leave a prefix and carries no complete proof.

### Budgets and terminal outcomes

Grid, address, output, and combined candidate-work ceilings are exact and
independent. `GridCandidateLimitExceeded` identifies the grid ceiling;
`CandidateWorkLimitExceeded` identifies the combined ceiling only when it is
strictly tighter. Raw alternatives consume address/work budget. `outputLimit` is
preflighted against the full required plus dependency-evidence result, so a
one-below failure does not publish a partial proof. Checked body-top and query
bounds failures report `ArithmeticOverflow`; invalid or unrepresentable topology
and union geometry remain `InvalidOrUnrepresentableGeometry`. Capacity, budget,
invalid-input, and unrepresentable-geometry failures clear the result; complete
and incomplete-physical results retain their full canonical evidence.

## Native Planar Navigation Bodies

`GridTracer.TracePlanarNavigationBodyInto(...)` proves one same-footprint or
immediate planar neighbor leg for an upright capsule. Pass `axisLength = 0` for
a circle; radius must be positive. The axis follows `Vector2d.Forward`, and
total planar height is `axisLength + 2 * radius`. This is native footprint
geometry: world Y is an embedding coordinate, not gameplay height, and the world
is neither rotated nor treated as a thin 3D navigation body.

The caller supplies a strictly configuration-ordered span of `GridPlanarLayer`
values, each pairing an exact `GridCoveredAddressGeneration` with one
nonnegative topology-local layer index. All selected layers must contain
`embeddingY`. Source and target must belong to those exact generations and
layers; stale revisions, duplicates, unsorted selections, unselected endpoints,
and non-neighbor legs fail closed. Longer movement is decomposed by the caller.
No active-world registry discovery or semantic/map admission is performed.

Closed capsule contact admits each declared endpoint. Coverage of other cells
uses exact strict continuous swept overlap, including odd raw half-axis lengths,
rounded corners, capsule middles, and wide bodies. Rectangular diagonals also
require both side cells. Both hex orientations support their planar neighbors.
Congruent aligned cross-grid footprints can complete a union even when their
selected layer heights differ. Incompatible or unaligned footprints cannot fill
a required source-lattice cell. Virtual positive-overlap neighbors outside all
eligible bounds invalidate the proof instead of silently clipping the body.

Results reuse the navigation-body evidence contract above: canonical identities,
physical presence, committed grid revisions, pinned endpoints, and missing
`PhysicalAlternativeDependency` OR evidence. A complete negative proof returns
`IncompletePhysicalCoverage`; aborted proofs leave no partial output. Other
layers and noneligible grids cannot satisfy missing coverage.

### Pre-size caller storage

Unlike the 3D body's growing scratch path, this entry treats **existing scratch
and result capacities as hard insertion ceilings**, in addition to the caller
budgets. Pre-size `GridNavigationBodyTraceScratch` and the result
`SwiftList<GridNavigationBodyTraceCell>` before entering a steady-state loop.
Grid scratch exhaustion reports `GridCandidateLimitExceeded`, address/union
scratch exhaustion reports `AddressLimitExceeded`, and result exhaustion reports
`OutputLimitExceeded`. Scratch is cleared on each normal return, retaining its
capacity. Storage and scratch must not be shared by concurrent calls.

### Work accounting and complexity

Every selected generation is charged before validation, and every bounded
address candidate before its geometry inspection. `CandidateWorkCount` is
exactly their sum, **not an exact count of all arithmetic operations**.
Candidate index bounds use the actual quantized forward lattice, not an ideal
hex inverse: at most four binary searches of 31 coordinate probes per selected
grid, without physical-cell or storage reads. This remains conservative for very
small representable hex metrics and translated grids. For N retained candidates,
sorting and lattice lookup cost O(N log N); closure has at most eight planar
neighbors per member, and duplicate-alternative evidence scans are bounded by
O(N squared). There are no hidden world scans or storage growth. Checked
body/query expansion overflow returns `ArithmeticOverflow`.

### Snapshot and locking scope

The bounded proof holds the world lifetime read lock, then the committed-change
lock, in the same order as sparse mutations. It validates revisions and
snapshots physical presence under that lock, without invoking host callbacks or
acquiring grid obstacle/occupant locks. The returned evidence is coherent at its
run stamp; later edits can still stale it, so consumers must retain the revision
checks. Large caller budgets also lengthen the period during which edits wait
for the proof. This API does not provide a wall-clock execution-time bound.

## Related guides

- [Cell Geometry and Navigation](Cell-Geometry-and-Navigation.md) for exact
  contact, portals, and corridor validation.
- [Segment Intervals and Slabs](Segment-Intervals-and-Slabs.md) for ray
  certificates.
- [GridTracer and Coverage](GridTracer-and-Coverage.md) for ordinary coverage.
