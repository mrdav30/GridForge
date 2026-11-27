# Runtime Counter Lifetimes And Exhaustion Plan

**Opened:** 2026-11-26

## Purpose And Status

**Status: planned; bounded audit selected, no runtime change selected.** Clarify
and verify identity/revision behavior in long-running simulations. This is the
corrected counter concern from feature planning, not a proposal for a GridForge
simulation clock. Chronicler.Timing solves elapsed-time representation; it does
not define membership identity, cache invalidation or mutation sequencing.

The source audit used GridForge `ea52599521f3b86a80d6c3c5d1f2dacb36fc6c82`. No
near-limit probe, new regression, build or benchmark was executed for this plan.
Source-derived risks below need bounded reproduction before being classified as
implementation defects.

## Existing Policies And Owners

| Counter family                                                       | Current behavior                                                                                                                                                                                      | Safety question                                                                                                                |
| -------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| World and obstacle identities, ScanCell occupant generations         | Process-owned signed 64-bit allocation through [RuntimeIdentityAllocator](../../src/GridForge/Support/RuntimeIdentityAllocator.cs); rejects negative/exhausted state before increment                 | Preserve nonzero uniqueness, thread-safe issuance and failure before externally visible registration. Do not recycle on reset. |
| Grid allocation generation                                           | World-local signed 64-bit allocation through the same helper                                                                                                                                          | Pair with world identity; surviving ordinary reset must not revive an old grid handle.                                         |
| [GridWorld.Version](../../src/GridForge/Grids/Managers/GridWorld.cs) | Public uint revision, incremented with Version++; can repeat, including zero                                                                                                                          | Identify every actual invalidation consumer and its other lifetime/sequence guards.                                            |
| [VoxelGrid.Version](../../src/GridForge/Grids/VoxelGrid.cs)          | Public uint revision deliberately wraps from MaxValue to one                                                                                                                                          | Verify existing wrap semantics and all cache consumers rather than assuming the scalar alone proves freshness.                 |
| World ChangeSequence and GridChangeStamp                             | Checked unsigned 64-bit increment under ChangeSyncRoot; world sequence zero is valid before the first commit, while a zero GridChangeStamp is invalid; ordinary reset does not start a fresh sequence | Define terminal exhaustion and mutation/publication consistency, including callbacks and grid LastChangeSequence.              |
| Provider/private traversal versions and cursors                      | Different owners and invalidation scopes                                                                                                                                                              | Inventory only versions that participate in GridForge or actual downstream safety; do not redesign every collection counter.   |

Existing tests include `RuntimeIdentityAllocator_ShouldThrowBeforeWraparound` in
[GridWorld.Tests](../../tests/GridForge.Tests/Grids/GridWorld.Tests.cs),
`AddOccupant_ShouldFailBeforeStorageMutationWhenGenerationIsExhausted` in
[ScanCell.Tests](../../tests/GridForge.Tests/Grids/ScanCell.Tests.cs), and grid
version-wrap coverage in the VoxelGrid test area. These were inspected, not
rerun by this planning pass. Existing coverage is a starting point, not evidence
that every mutation/counter exhaustion boundary has been verified.

Runtime allocation tokens are transient safety identities. They are not
canonical simulation order, procedural-generation seeds, persistent object IDs
or substitutes for a clock. Retain the established
[runtime identity contract](done/2026-07-17-runtime-identity-hardening-plan.md).

## Decisions And Investigation Boundaries

- **Selected:** a complete consumer ledger for the counter families above and
  bounded near-limit behavior checks. Exercise real mutation/query paths.
- **Retain initially:** guarded 64-bit identity allocation and the currently
  documented distinction between runtime identity and revision numbers.
- **Open:** whether repeating uint revisions are sufficient for every real cache
  consumer, and the explicit terminal policy for exhausted commit order. Decide
  from the complete invalidation chain, not a single equality check.
- **Not selected:** widening every field, resetting counters on overflow,
  saturating revisions at a constant, adding a clock or a hidden global
  registry. Each would need its own compatibility and safety argument.
- **Dependency boundary:** this plan owns counter semantics. The
  [coordinate/sectors plan](grid-coordinate-ranges-and-sectors-plan.md) owns
  position arithmetic. The
  [observation-lifetime plan](world-observation-lifetimes-plan.md) must use the
  accepted counters without silently changing their meanings.

The consumer ledger must include public
`IncrementGridVersion(significant: true)`, which changes uint revisions without
publishing a committed sequence. Conversely, a downstream uint comparison can be
backed by event-driven rebuilds and a wider sequence elsewhere. Follow both
paths before declaring an ABA (repeated-value freshness) defect. There is no
live-world counter restore API selected here; reconstructing configuration is
not permission to reset runtime allocators or invent serialized counter
envelopes.

## Source-Derived Question: Mutation Before Stamp Exhaustion

`GridWorld.AllocateChangeStamp` computes checked `_changeSequence + 1`. Overflow
leaves that counter unchanged. Several current mutation paths allocate the stamp
after changing their primary storage, counts or versions. At terminal sequence,
this could produce changed state without the matching committed event or
LastChangeSequence update. A retained sequence-based proof might then appear
current despite a mutation. This is an **unexecuted failure-boundary
hypothesis**, not a confirmed defect or a deadlock claim.

Phase 0 must seed the existing private state in a disposable fixture, perform
one real operation and inspect storage, versions, event count/order and proof
validity. Use an ordinary control and a subsequent-operation/teardown check. Do
not run billions of mutations, change production APIs for test access, or infer
an observed failure from source ordering alone. If confirmed outside the
accepted contract, record it in the issue tracker with revision, reproducer,
expected/observed state and a scoped next action, preserving ID history.

## Implementation Phases

### Phase 0 — Classify Actual Lifetime Guarantees

- [ ] Refresh source identities and enumerate increment/reset/restore sites and
      public/private consumers. Record width, reserved values, owner, wrap or
      throw policy, synchronization and whether the value is durable.
- [ ] Follow full consumer invalidation chains in GridForge, Trailblazer,
      Gravitas and relevant adapters. A uint comparison may be backed by world
      tokens, committed sequence, topology rebuilding or lifetime teardown.
      Compare actual published proofs and caches, not isolated fields.
- [ ] Check max-minus-one, terminal value, invalid/default values and deliberate
      uint wrap with tiny fixtures. Include add/remove grid, reset/deactivation,
      sparse add/remove, obstacles, occupants and explicit version increments as
      applicable. Separate normal argument rejection from exceptional
      exhaustion.
- [ ] Check atomicity around exhaustion: slot membership, generation allocation,
      counts, blocker/occupant registries, grid/world versions, event
      publication and retained cursor/proof validity. Preserve identity safety
      even when an allocation attempt consumes an identity without publishing a
      registration.
- [ ] Verify ordinary reset versus deactivation/new world and restore/populate
      boundaries. An old or abandoned-future handle must not become valid from a
      numerically repeated revision or reset allocator.
- [ ] Record a disposition per family: retain and document, scoped confirmed
      fix, or explicit API decision. Estimate practical exhaustion only from
      actual mutation rates; do not equate one version increment with one frame.

### Phase 1 — Harden Only Proven Gaps

- [ ] Add a failing regression through the actual production operation before
      any confirmed fix. Prefer the existing allocator and narrow state owner;
      no broad epoch framework or new provider capability without a real need.
- [ ] Define the observable terminal policy before implementation: rejection,
      exception, read-only terminal state or explicit new lifetime. Ensure no
      unsafe alias, silent saturation or partially advertised successful change.
      Audit allocation, callback and publication ordering together.
- [ ] If revising public counter types or semantics, identify source/binary,
      serialization, event, cache and downstream implications. Do not widen an
      API and merely update tests to accept changed behavior.
- [ ] Preserve deterministic order, lock ordering and warm allocation. Obtain
      independent first-slice review before propagating to related mutations.
      Remove superseded paths if a shared owner is adopted.

### Phase 2 — Validate And Close

- [ ] Run focused exhaustion/wrap/reset/proof-continuation checks, then full
      Release and ReleaseLean net10.0 suites. Build netstandard2.1/net10.0 and
      require strict fresh 100% reported line, branch and executable-method
      coverage with the existing runsettings and assertion script.
- [ ] Verify affected downstream invalidation, replay/populate and allocation
      contracts. Coordinate sources using `UseLocalLsfStack=true`, global
      `DisableTransitiveProjectReferences=true` and serial shared-output builds;
      record exact provider/consumer revisions and local patches.
- [ ] Measure affected mutation/query operations if implementation changes hot
      paths. Use bounded comparable runs, inspect actual child/case completion
      and preserve adverse/inconclusive outcomes. A no-change audit does not
      justify broad benchmarking or a new maintained harness.
- [ ] Update public XML, the determinism/lifecycle guides and relevant
      resolution records. Preserve existing CI policy and remind maintainers to
      align CI source pins after any accepted upstream change lands. Package
      release readiness is outside this plan unless separately requested.
- [ ] Record each counter's final disposition, exact verification and remaining
      limits here. Archive only after the selected work is complete; retain is a
      valid outcome. Leave edits unstaged/uncommitted for review.

## Completion Evidence

A completed audit accounts for each safety-relevant counter and actual consumer,
reports bounded edge results and selects explicit exhaustion/wrap behavior. Any
accepted fix requires its reproducing regression, applicable source/consumer
checks, strict coverage and independent review. No counter has been changed or
new exhaustion defect confirmed by this planning pass.
