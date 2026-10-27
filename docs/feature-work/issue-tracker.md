# Issue Tracker

This tracker keeps active out-of-plan issues and a compact index of verified
resolutions or documented investigation closures.

## Tracker Rules

- Issue IDs use `GF-Issue-NNN`. The next available ID is `GF-Issue-012`.
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

- None currently.

## Performance Investigation Queue

Performance issues should stay in the benchmark plan unless they become a
confirmed runtime defect. Current queue:

- None currently.

## Resolved Issues

Dates reflect recorded resolution/closure, which can precede the commit date.
Evidence links retain the committed root causes, regressions and validation;
they do not claim a fresh rerun or package release. Closed investigations are
identified explicitly below.

| Issue                                                                                                                                                                                                                                                | Resolved/closed | Outcome                                                                                                                                                   | Evidence                                                                                                                                                                                                                    |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| <a id="gf-issue-011"></a>GF-Issue-011 | 2026-10-27 | Corrected a pool-history-dependent test assumption: independent worlds need distinct registration generations, not equal physical bucket slots. Retained world-isolation checks and explicitly reject a foreign generation at the local slot. | [Regression](../../tests/GridForge.Tests/Grids/GridWorld.Tests.cs) — `OccupantTrackingAndScan_ShouldStayInsideExplicitWorld`; controlled fresh/recycled buckets reproduced slots 0/1 before the fix. |
| <a id="gf-issue-010---ideal-hex-inverse-can-omit-representable-small-metric-lattice-addresses"></a>GF-Issue-010                                                                                                                                      | 2026-09-21      | Aligned hex inverse/dimensions with the quantized lattice, preserving small-metric addresses within documented arithmetic limits.                         | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-010---ideal-hex-inverse-can-omit-representable-small-metric-lattice-addresses)    |
| <a id="gf-issue-009---local-stack-executable-hosts-selected-published-math-binaries"></a>GF-Issue-009                                                                                                                                                | 2026-09-15      | Corrected sibling math selection in direct local-stack test/benchmark hosts; generated benchmark children remain a separate boundary.                     | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-009---local-stack-executable-hosts-selected-published-math-binaries)              |
| <a id="gf-issue-008---generated-local-stack-benchmark-builds-rediscover-unversioned-dependencies"></a>GF-Issue-008                                                                                                                                   | 2026-09-15      | Documented inherited local-stack/transitive-reference settings that prevent unversioned generated-child dependencies; no geometry fix.                    | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-008---generated-local-stack-benchmark-builds-rediscover-unversioned-dependencies) |
| <a id="gf-issue-007---benchmark-launcher-returns-success-after-a-failed-child"></a>GF-Issue-007                                                                                                                                                      | 2026-09-11      | Propagated benchmark validation, build and child-exit failures to launcher status; zero exit alone does not certify a complete capture.                   | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-007---benchmark-launcher-returns-success-after-a-failed-child)                    |
| <a id="gf-issue-006---planar-strict-miss-benchmark-launch-reported-a-null-reference-exception"></a><a id="closed-issues-unreproduced"></a><a id="focused-investigation---2026-09-11"></a><a id="bounded-closure-check---2026-09-22"></a>GF-Issue-006 | 2026-09-22      | Closed as unreproduced, with no cause or runtime fix established; the original failed baseline remains excluded from comparisons.                         | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-006---planar-strict-miss-benchmark-launch-reported-a-null-reference-exception)    |
| <a id="gf-issue-005--recycled-occupant-tickets-could-resolve-replacement-occupants"></a>GF-Issue-005                                                                                                                                                 | 2026-07-17      | Made occupant tickets and scan-cell entries generation-aware, rejecting recycled registrations while preserving direct lookup and cleanup.                | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-005--recycled-occupant-tickets-could-resolve-replacement-occupants)               |
| <a id="gf-issue-004--identical-bounds-blockers-shared-one-registration-identity"></a>GF-Issue-004                                                                                                                                                    | 2026-07-17      | Separated blocker registration identity from bounds so identical geometry stacks and removes independently; retained exact rollback and capacity checks.  | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-004--identical-bounds-blockers-shared-one-registration-identity)                  |
| <a id="gf-issue-003--gridforge-reused-grid-spawn-tokens-across-pooled-generations"></a>GF-Issue-003                                                                                                                                                  | 2026-07-17      | Replaced hash-derived world/grid tokens with nonrepeating generation identities, preventing stale pooled-grid and voxel keys from resolving replacements. | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-003--gridforge-reused-grid-spawn-tokens-across-pooled-generations)                |
| <a id="gf-issue-002--coverlet-branch-instrumentation-guard-target-misses"></a>GF-Issue-002                                                                                                                                                           | 2026-06-14      | Simplified dead/short-circuit guard paths and covered diagnostics, topology, tracing and storage edge cases; branch coverage reached 100%.                | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-002--coverlet-branch-instrumentation-guard-target-misses)                         |
| <a id="gf-issue-001--direction-utility-arrays-are-public-and-mutable"></a>GF-Issue-001                                                                                                                                                               | 2026-06-14      | Replaced mutable public direction arrays with privately backed ReadOnlySpan properties and added a public-array exposure regression.                      | [Closeout](https://github.com/mrdav30/GridForge/blob/e7223aef007a9c74e830b1e820c59fc01e49b7d8/docs/feature-work/issue-tracker.md#gf-issue-001--direction-utility-arrays-are-public-and-mutable)                             |

**GF-Issue-006 recurrence:** Stop proactive replays; reopen the same ID only if
the failure recurs. Capture exception state, exact executable identities and
full logs using the linked validated first-chance procedure before changing
code. Successful replays are not a fix; do not conceal a failure with null
guards, retries or optimization changes. The linked closure also records that
its historical artifact directory was absent at the last check.
