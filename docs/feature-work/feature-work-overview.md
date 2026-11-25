# Feature Work Overview

Feature plans own their design decisions, phases and verification evidence. Bug
investigations belong in the [issue tracker](issue-tracker.md), and measured
performance signals belong in the
[benchmark backlog](benchmark-signal-hardening-backlog.md).

| Feature                                                                              | Status                                                           | Scope                                                                                                                                                                                                                                                                                                    |
| ------------------------------------------------------------------------------------ | ---------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [SwiftCollections v9 adoption](done/2026-11-24-swiftcollections-v9-adoption-plan.md) | Completed Phase 0 and bucket follow-ups; current owners retained | Retained SlotMap after DenseTicketMap, TicketStore and cached-position comparisons, and existing 3D discovery after the projected-index comparison. A bounded DenseTicketMap lookup improvement is recorded for separate provider work; results, limits and reopening criteria remain in the evaluation. |

Completed feature plans remain under [`done/`](done/), including the
[original feature roadmap](done/2026-06-11-feature-roadmap-overview.md).
