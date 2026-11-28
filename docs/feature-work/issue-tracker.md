# Issue Tracker

This tracker keeps active out-of-plan issues and a compact index of verified
resolutions or documented investigation closures.

## Tracker Rules

- Issue IDs use `GF-Issue-NNN`. The next available ID is `GF-Issue-020`.
- Assign an ID at intake and preserve it through closure; never delete or reuse
  IDs. Check Git history before advancing or repairing the counter. Unknown
  historical IDs stay reserved rather than acquiring an inferred cause.
- Add unresolved concerns with concrete evidence to `Active Issues`. Keep each
  entry self-contained: discovery date, status, affected owners, reproducer,
  source revision, impact, priority and next action. Distinguish confirmed
  defects from unverified concerns.
- Keep each item scoped tightly enough to fix and verify independently. Resolve
  related issues together when they share an owner, retaining each ID and its
  regression evidence. Link active plans rather than duplicating their work.
- Record the date on the item, not in this filename.
- After verification, replace the active entry with a compact `Resolved Issues`
  row: the same ID, resolution/closure date, brief outcome and an evidence link.
  Keep detailed root causes, decisions and validation in an existing completion
  record, regression or fix commit rather than duplicating them in this index.
- Distinguish verified fixes, mitigations and investigations closed without a
  reproduced defect. A closed investigation does not establish a runtime fix or
  an attributed cause; preserve its evidence, limits and recurrence-capture
  instructions, and reopen the same ID if it recurs.
- Resolved means verified in source or closed by documented decision, not
  released. Add release/version claims only when separately confirmed. Retain
  prior evidence when new findings change an issue's status.
- Preserve existing issue anchors when compacting entries so inbound links
  continue to reach their issue. New rows may use a short `gf-issue-nnn` anchor.
- Use repository-relative links and portable commands. Ignored artifacts are
  supporting evidence; continuing an issue must not require their survival.
- Do not use this tracker as a substitute for tests, benchmarks, or release
  notes.
- Performance issues should stay in
  [`benchmark-signal-hardening-backlog.md`](benchmark-signal-hardening-backlog.md)
  unless they become a confirmed runtime defect. Do not add performance issues
  here until they have been investigated and confirmed as runtime defects.

## Active Issues

### <a id="gf-issue-018"></a>GF-Issue-018 — Direct occupant registration accepts another world's voxel

**2026-11-28 · Confirmed · High priority.** Create two active worlds, each with
one dense voxel at the origin and the same scan-cell key. Pass world B's voxel
to `gridA.TryAddVoxelOccupant(voxelB, occupant)`. The call returned true.
Only A's registry held a ticket for B's address. A's voxel count remained
zero, B's became one, and the added event identified B. Removal through B
failed. Removal through A with B's voxel succeeded and reported B again.
As a control, after a legitimate registration in B, A rejected the foreign
removal and B removed its own registration.

Harden the direct add/remove overloads at
[GridOccupantManager](../../src/GridForge/Grids/Managers/GridOccupantManager.cs).
Verify receiver world, grid generation and exact physical voxel before mutation
or publication. Regress the two-world case and a foreign voxel with a copied
local identity. Both owners' counts, registry tickets and notifications must
survive rejected input intact. This establishes the publishing owner for the
[world observation plan](world-observation-lifetimes-plan.md).

### <a id="gf-issue-019"></a>GF-Issue-019 — A replacement voxel subscriber receives its predecessor's event

**2026-11-28 · Confirmed · High priority.** In a one-cell dense grid, subscribe
to the static occupant-added event. From the callback, remove and recreate the
grid at the same origin, then attach a new voxel-local subscriber. The voxel
object was rerented and the new subscriber received the original registration's
event once. The same result occurred on occupant removal. In a one-cell sparse
grid, removing and rematerializing just the emptied voxel from the global
removal callback reproduced the delivery even with the same full
`WorldVoxelIndex` and object reference. The equivalent sparse obstacle-removal
and obstacle-clear callbacks also delivered the old event once to the new
listener. In each case the listener saw sequence 4 after the replacement had
committed at sequence 6. The replacement held zero obstacles.

[GridOccupantManager](../../src/GridForge/Grids/Managers/GridOccupantManager.cs)
currently calls global handlers before looking up the voxel's current
subscribers. [GridObstacleManager](../../src/GridForge/Grids/Managers/GridObstacleManager.cs)
checks the live address after global delivery, which still matches after sparse
reuse. Bind voxel-local delivery to the original subscribers or physical
lifetime before replacement can occur. Keep manager-before-voxel order and
verify dense grid replacement and same-address sparse reuse under the
[world observation plan](world-observation-lifetimes-plan.md). No observer
first attached to a replacement should receive its predecessor's notification.

All cases reproduced in isolated in-memory probes against the local-stack
Standard Release `net10.0` assembly built from committed develop revision
`f9500aa307a38e55156f156ef7488e24002682d6`. The tested DLL's SHA-256 was
`1DB2ACE4686229A479FB7CAE89F4D65FBE76A9449B699CBDCFE42334C8B72849`.

## Performance Investigation Queue

Performance issues should stay in the benchmark plan unless they become a
confirmed runtime defect. Current queue:

- None currently.

## Resolved Issues

Dates reflect recorded resolution/closure, which can precede the commit date.
Evidence links retain the committed root causes, regressions and validation;
they do not claim a fresh rerun or package release. Closed investigations are
identified explicitly below.

| Issue                                                                                                                                                                                                                                                | Resolved/closed | Outcome                                                                                                                                                                                                                                        | Evidence                                                                                                                                                                                                                    |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| <a id="gf-issue-017"></a>GF-Issue-017 | 2026-11-28 | Grid registration during blocker application now requests a fresh coverage pass with the same token. Explicit removal cancels the pending pass. Four regressions verify all three physical voxels and cleanup in cached and uncached modes. | [Blocker regressions](../../tests/GridForge.Tests/Blockers/BlockerTests.cs) and [reproduction and verification](done/runtime-counter-lifetimes-plan.md#gf-issue-017) |
| <a id="gf-issue-016"></a>GF-Issue-016 | 2026-11-28 | Explicit removal during the first attachment cancels the application and rolls back its token. Cached and uncached regressions verify the exact voxel events, empty coverage, and successful reapplication with a fresh token. | [Blocker regressions](../../tests/GridForge.Tests/Blockers/BlockerTests.cs) and [v10.2.0 reproduction](done/runtime-counter-lifetimes-plan.md#gf-issue-016) |
| <a id="gf-issue-015"></a>GF-Issue-015 | 2026-11-27 | Verified admission before primitive mutation, terminal reset/disposal and ownership of interrupted blocker attachments. Both Standard and Lean passed 1,284 tests and strict full coverage. | [Reproduction, contracts and validation](done/runtime-counter-lifetimes-plan.md) |
| <a id="gf-issue-014--direct-obstacle-mutation-accepts-a-foreign-voxel"></a><a id="gf-issue-014"></a>GF-Issue-014                                                                                                                                     | 2026-11-24      | Validated world/grid generation and exact physical ownership under the shared mutation locks for add/remove/clear. All 29 focused cases and both 1,248-test source-mode suites pass strict full coverage.                                      | [Resolution, reproduction and contract limits](done/2026-11-24-gf-issue-014-obstacle-ownership.md)                                                                                                                          |
| <a id="gf-issue-013--voxel-occupant-enumeration-includes-neighboring-buckets"></a><a id="gf-issue-013"></a>GF-Issue-013                                                                                                                              | 2026-11-24      | Routed unfiltered, typed and conditional voxel queries to the exact registration bucket; retained whole-cell queries and lowered measured warm allocation. Ten focused cases and both 1,219-test source-mode suites pass strict full coverage. | [Resolution and preserved reproducer](done/2026-11-24-gf-issue-013-voxel-occupant-lookup.md)                                                                                                                                |
| <a id="gf-issue-011"></a>GF-Issue-011                                                                                                                                                                                                                | 2026-10-27      | Corrected a pool-history-dependent test assumption: independent worlds need distinct registration generations, not equal physical bucket slots. Retained world-isolation checks and explicitly reject a foreign generation at the local slot.  | [Regression](../../tests/GridForge.Tests/Grids/GridWorld.Tests.cs) â€” `OccupantTrackingAndScan_ShouldStayInsideExplicitWorld`; controlled fresh/recycled buckets reproduced slots 0/1 before the fix.                      |
| <a id="gf-issue-010---ideal-hex-inverse-can-omit-representable-small-metric-lattice-addresses"></a>GF-Issue-010                                                                                                                                      | 2026-09-21      | Aligned hex inverse/dimensions with the quantized lattice, preserving small-metric addresses within documented arithmetic limits.                                                                                                              | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-010---ideal-hex-inverse-can-omit-representable-small-metric-lattice-addresses)    |
| <a id="gf-issue-009---local-stack-executable-hosts-selected-published-math-binaries"></a>GF-Issue-009                                                                                                                                                | 2026-09-15      | Corrected sibling math selection in direct local-stack test/benchmark hosts; generated benchmark children remain a separate boundary.                                                                                                          | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-009---local-stack-executable-hosts-selected-published-math-binaries)              |
| <a id="gf-issue-008---generated-local-stack-benchmark-builds-rediscover-unversioned-dependencies"></a>GF-Issue-008                                                                                                                                   | 2026-09-15      | Documented inherited local-stack/transitive-reference settings that prevent unversioned generated-child dependencies; no geometry fix.                                                                                                         | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-008---generated-local-stack-benchmark-builds-rediscover-unversioned-dependencies) |
| <a id="gf-issue-007---benchmark-launcher-returns-success-after-a-failed-child"></a>GF-Issue-007                                                                                                                                                      | 2026-09-11      | Propagated benchmark validation, build and child-exit failures to launcher status; zero exit alone does not certify a complete capture.                                                                                                        | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-007---benchmark-launcher-returns-success-after-a-failed-child)                    |
| <a id="gf-issue-006---planar-strict-miss-benchmark-launch-reported-a-null-reference-exception"></a><a id="closed-issues-unreproduced"></a><a id="focused-investigation---2026-09-11"></a><a id="bounded-closure-check---2026-09-22"></a>GF-Issue-006 | 2026-09-22      | Closed as unreproduced, with no cause or runtime fix established; the original failed baseline remains excluded from comparisons.                                                                                                              | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-006---planar-strict-miss-benchmark-launch-reported-a-null-reference-exception)    |
| <a id="gf-issue-005--recycled-occupant-tickets-could-resolve-replacement-occupants"></a>GF-Issue-005                                                                                                                                                 | 2026-07-17      | Made occupant tickets and scan-cell entries generation-aware, rejecting recycled registrations while preserving direct lookup and cleanup.                                                                                                     | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-005--recycled-occupant-tickets-could-resolve-replacement-occupants)               |
| <a id="gf-issue-004--identical-bounds-blockers-shared-one-registration-identity"></a>GF-Issue-004                                                                                                                                                    | 2026-07-17      | Separated blocker registration identity from bounds so identical geometry stacks and removes independently; retained exact rollback and capacity checks.                                                                                       | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-004--identical-bounds-blockers-shared-one-registration-identity)                  |
| <a id="gf-issue-003--gridforge-reused-grid-spawn-tokens-across-pooled-generations"></a>GF-Issue-003                                                                                                                                                  | 2026-07-17      | Replaced hash-derived world/grid tokens with nonrepeating generation identities, preventing stale pooled-grid and voxel keys from resolving replacements.                                                                                      | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-003--gridforge-reused-grid-spawn-tokens-across-pooled-generations)                |
| <a id="gf-issue-002--coverlet-branch-instrumentation-guard-target-misses"></a>GF-Issue-002                                                                                                                                                           | 2026-06-14      | Simplified dead/short-circuit guard paths and covered diagnostics, topology, tracing and storage edge cases; branch coverage reached 100%.                                                                                                     | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-002--coverlet-branch-instrumentation-guard-target-misses)                         |
| <a id="gf-issue-001--direction-utility-arrays-are-public-and-mutable"></a>GF-Issue-001                                                                                                                                                               | 2026-06-14      | Replaced mutable public direction arrays with privately backed ReadOnlySpan properties and added a public-array exposure regression.                                                                                                           | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-001--direction-utility-arrays-are-public-and-mutable)                             |

**GF-Issue-006 recurrence:** Stop proactive replays; reopen the same ID only if
the failure recurs. Capture exception state, exact executable identities and
full logs using the linked validated first-chance procedure before changing
code. Successful replays are not a fix; do not conceal a failure with null
guards, retries or optimization changes. The linked closure also records that
its historical artifact directory was absent at the last check.
