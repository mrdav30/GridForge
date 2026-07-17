# Runtime Identity Hardening Battle Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` or `superpowers:executing-plans` to
> implement this plan phase-by-phase. Use `superpowers:systematic-debugging`
> before changing confirmed defects, `superpowers:test-driven-development` for
> runtime behavior changes, `superpowers:requesting-code-review` for an
> independent final review, and `superpowers:verification-before-completion`
> before claiming a phase is complete. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Status:** In Progress

**Goal:** Make every runtime identity used for stale-reference rejection,
deduplication, blocker ownership, or occupant lookup exact across pooled reuse,
slot reuse, identical geometry, and multiple live worlds.

**Architecture:** Keep geometry and configuration keys as value keys. Replace
hash-code-derived identities with allocation identities owned by the narrowest
correct lifetime: process-unique world identity, world-local grid generations,
blocker registration identity, and generation-aware occupant tickets. Runtime
identities are transient safety data, not serialized simulation state.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`,
`SwiftCollections`, xUnit v3, BenchmarkDotNet, GridForge standard and
`ReleaseLean` variants, and locally linked Gravitas validation.

---

## Status

- Started: 2026-07-17.
- Planning date: 2026-07-17.
- Release posture: intentionally breaking pre-release hardening.
- Current state: Phases 0-2 are complete. Exact voxel identity and local-linked
  Gravitas propagation are verified and independently reviewed. Phase 3 is
  next.
- Working agreement: keep local project references in place and uncommitted;
  commit coherent implementation milestones directly to `develop` while
  preserving unrelated owner changes.
- Execution order: finish one phase and its focused verification before moving
  to the next phase.

## Why This Work Exists

`GridWorld`, `VoxelGrid`, `Voxel`, and `ScanCell` currently assign some
`SpawnToken` values from `GetHashCode()`. A hash code is suitable for locating
a value in a hash table because equality resolves collisions. It is not a
unique allocation identity.

The reported failure is deterministic:

1. A grid occupies a `SwiftBucket` slot.
2. Removal returns the grid to its pool and frees the slot.
3. Adding the same configuration reuses the slot.
4. `VoxelGrid.GetHashCode()` sees the same slot and bounds and recreates the
   same token.
5. A stale `WorldVoxelIndex` is indistinguishable from the replacement grid's
   voxel identity and resolves successfully.

The audit also reproduced two live `GridWorld` instances with the same
hash-derived world token. Identical grid layouts in those worlds then produced
equal `WorldVoxelIndex` values and allowed cross-world resolution.

Two adjacent public-contract defects were confirmed:

- Distinct blockers with identical bounds share the same `BoundsKey` token and
  do not stack independently.
- An occupant bucket ticket is only a recyclable slot. After removal, the stale
  integer can resolve a replacement occupant assigned to the same slot.

## Capacity And Long-Running Worlds

Generation safety does not impose a practical world-size limit.

- `GridWorld.MaxGrids` remains the simultaneous active-grid limit imposed by
  the current `ushort` slot index.
- Grid generations count allocations over a world's lifetime, including churn
  through reused slots.
- World identities count `GridWorld` instances created by the process.
- Runtime identity values use nonzero signed 64-bit integers. Each allocator
  therefore has `9,223,372,036,854,775,807` usable values.
- At one million allocations per second, exhausting one allocator would take
  roughly 292,000 years. At one billion allocations per second, it would still
  take roughly 292 years.

Explicit exhaustion failure exists only to prevent silent wraparound from
making stale identities valid again. It is not expected in a real simulation,
including streamed worlds, huge universes, or long-running servers. Widening
the token is preferable to accepting a reachable wraparound or building a
reclamation registry whose complexity would exceed its value.

## Locked Decisions

- Use a hash only as a hash. Never store `GetHashCode()` as a unique identity.
- Use nonzero signed 64-bit values for world, grid-generation, blocker, and
  occupant-registration identity domains.
- Reserve zero for default, inactive, or unallocated identity.
- Fail before incrementing past `long.MaxValue`; never wrap or reuse an issued
  value within its owning lifetime.
- Keep the world identity allocator process-wide because two simultaneously
  live worlds require collision-free process-local separation. It owns no
  grids, coordinates, simulation state, or world registry.
- Keep grid-generation allocation world-owned and preserve its counter across
  `Reset(deactivate: false)`.
- Keep blocker and occupant registration counters in separate identity domains
  so unrelated operations do not perturb grid-generation values.
- Runtime identities are not save-game IDs, network entity IDs, or durable
  content IDs. Do not serialize them as authoritative state.
- Runtime identities must not become the source of authoritative spatial or
  collision ordering. Existing coordinate and stable entity ordering remains
  authoritative.
- `WorldVoxelIndex` remains the exact cross-system voxel identity and includes
  world identity, grid slot, grid generation, and voxel coordinate.
- Traversal deduplication uses `WorldVoxelIndex`, not an object hash.
- `GridConfigurationKey` remains an exact active spatial-configuration value
  key. Excluding scan-cell size and storage kind remains intentional.
- `BoundsKey` remains an exact geometry value key. It must not double as a
  blocker instance identity.
- `GridIndex` remains a recyclable active-grid slot, not a durable identity.
- `VoxelIndex` remains a coordinate value whose allocation state is checked by
  the owning storage path.
- Remove unused or misleading hash-derived `Voxel.SpawnToken` and
  `ScanCell.SpawnToken` APIs instead of preserving compatibility aliases.
- Preserve engine agnosticism. No Unity, Godot, Unreal, renderer, scene graph,
  or adapter behavior enters GridForge core.

## Non-Goals

- Do not add sectors, universe hierarchy, world streaming orchestration, or a
  scene graph.
- Do not make transient runtime identities stable across processes, saves, or
  replay reconstruction.
- Do not replace `GridConfigurationKey`, `BoundsKey`, `GridIndex`, or
  `VoxelIndex` with generated identities when their current value/slot roles are
  correct.
- Do not add a general identity framework, registry, GUID dependency, or
  compatibility layer.
- Do not change active-grid capacity or widen `GridIndex` in this workstream.
- Do not mix GridForge-Unity migration into core implementation. Record any
  adapter compilation work separately after the core contract is final.

## Identity Model

```text
process
  -> GridWorld.SpawnToken: unique runtime world instance

GridWorld
  -> VoxelGrid.SpawnToken: unique allocation generation within that world
  -> blocker token: unique active blocker registration within that world
  -> occupant generation: unique occupant registration within that world

WorldVoxelIndex
  = world token + recyclable grid slot + grid generation + voxel coordinate

occupant ticket
  = recyclable bucket slot + occupant registration generation
```

The existing `SpawnToken` names may remain to minimize mechanical API churn,
but their XML documentation must describe 64-bit runtime allocation identity,
not hash uniqueness. Rename only where a name remains actively misleading after
the type and contract are corrected.

## Historical Evidence

The pre-implementation RCA demonstrated the following remove/re-add sequence:

```text
slot: 0 -> 0
token: unchanged
pooled instance reused: true
stale identity equals replacement identity: true
stale identity resolves replacement grid: true
stale identity resolves replacement voxel: true
```

The same alias occurs through `Reset(deactivate: false)` followed by an
identical grid add. Reusing the same pooled object is not required because the
current grid hash is structural over slot and bounds.

## Phase 0: Baseline And Contract Locks

Intent: preserve evidence and establish focused performance and behavior
baselines before runtime changes.

- [x] Confirm the local FixedMathSharp and SwiftCollections project references
  remain present in the GridForge library, test, and benchmark projects.
- [x] Confirm the local GridForge project reference remains present in the
  Gravitas library, test, and benchmark projects.
- [x] Record current `git status` in both repositories and preserve unrelated
  owner changes.
- [x] Run the GridForge Debug test baseline.
- [x] Capture the closest existing grid lifecycle, traversal, blocker, and
  occupant benchmark baselines. Add a narrowly scoped benchmark only if an
  affected hot path has no meaningful existing signal.
- [x] Add focused failing regressions before changing runtime behavior.

Exit criteria:

- [x] Baseline tests pass before new regressions are introduced.
- [x] The exact stale-grid reproduction fails for the expected reason.
- [x] Benchmark commands and baseline medians are recorded in this plan.

Phase 0 evidence:

- Debug baseline: `429/429` passed, `0` failed.
- RED regressions: `2/2` failed because stale identities resolved replacement
  grids after identical remove/re-add and `Reset(deactivate: false)`.
- Benchmark command:

  ```powershell
  dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- grid-registration grid-tracer blocker-memory occupant-wave --filter '*' --job short --exporters json
  ```

- Default-toolchain baseline means/allocations:

  | Signal | Mean | Allocated |
  | --- | ---: | ---: |
  | Register many adjacent grids | 2,389.4 us | 1,004.58 KB |
  | Remove many adjacent grids | 1,781.7 us | 24.11 KB |
  | Covered voxels, warm pools | 612.07 us | 760 B |
  | Trace line, warm pools | 194.57 us | 704 B |
  | Blocker apply/remove, uncached | 64.40 ms | 28.11 MB |
  | Blocker apply/remove, cached | 46.78 ms | 27.98 MB |
  | Occupant wave, cold pools | 55.43 ms | 32.77 MB |
  | Occupant wave, warm pools | 79.85 ms | 31.95 MB |

Short-run timing variance is high for several rows. Allocation deltas and
large repeatable timing changes are stronger signals than small mean changes.

## Phase 1: World And Grid Allocation Identity

Intent: make `WorldVoxelIndex` reject stale grid generations and foreign worlds
without relying on hash-code luck.

- [x] Add one small internal 64-bit allocation helper that atomically issues a
  nonzero value and throws before overflow. Do not add an interface, registry,
  or pluggable provider.
- [x] Allocate each active `GridWorld` a process-unique runtime token.
- [x] Give each world a private grid-generation counter.
- [x] Issue a new grid generation for every successful grid allocation,
  independent of pool object, slot, bounds, topology, or storage kind.
- [x] Preserve the world's grid-generation counter across
  `Reset(deactivate: false)`.
- [x] Invalidate the public world token when the world is deactivated, while
  never returning its issued process token to an allocator.
- [x] Widen the world and grid token fields carried by `WorldVoxelIndex`, grid
  events, diagnostics, and comparisons to 64-bit values.
- [x] Ensure allocator exhaustion fails before a replacement becomes visible or
  a prior generation can be reused.

Required tests:

- [x] Remove and re-add the identical configuration into the reused slot: the
  stale identity fails grid, voxel, and partition lookup; the replacement
  identity succeeds.
- [x] `Reset(deactivate: false)` plus identical re-add preserves world identity
  and advances grid generation.
- [x] Two simultaneously live worlds with identical layouts have different
  world identities and reject each other's voxel identities.
- [x] Generation values are nonzero and advance even when the pool returns the
  same object.
- [x] The allocator's boundary behavior throws before wraparound.

Exit criteria:

- [x] All focused world/grid identity tests pass.
- [x] No world or grid allocation identity is assigned from `GetHashCode()`;
  the deliberately deferred voxel/scan-cell hash tokens remain Phase 2 scope.
- [x] Grid lookup remains O(1) by slot plus generation validation.

Phase 1 evidence:

- Commit: `0c5420f` (`fix(identity): make world and grid generations
  allocation-safe`).
- Focused world/grid/allocator suite: `5/5` passed.
- Full Debug suite: `433/433` passed, `0` failed.
- `RuntimeIdentityAllocator` uses compare-and-swap allocation, reserves zero,
  and throws without mutating a counter already at `long.MaxValue`.
- Grid generations are allocated before a replacement becomes visible and the
  world-local counter survives non-deactivating reset.
- Independent Phase 1 review: no findings.

## Phase 2: Exact Voxel Deduplication And Gravitas Consumers

Intent: remove object-hash collision risk from traversal and prove the corrected
identity through every downstream physics mode.

- [x] Change `GridTraversal` visited sets from `SwiftHashSet<int>` object hashes
  to `SwiftHashSet<WorldVoxelIndex>` exact identities.
- [x] Update all GridForge traversal callers; no pooled production set exists,
  so the existing caller-owned ownership remains unchanged.
- [x] Update Gravitas 3D query deduplication and every other local-linked
  consumer to use exact identities.
- [x] Remove `Voxel.SpawnToken` after its final consumer is gone.
- [x] Remove unused `ScanCell.SpawnToken` rather than hardening dead identity.
- [x] Audit diagnostic and deterministic sort comparers after token widening;
  keep coordinate/entity ordering authoritative.
- [x] Update GridForge XML documentation and the migration guide for the
  breaking token-width and removed-member changes.

Required tests:

- [x] Grid traversal visits two distinct voxels even when a synthetic comparer
  or hash path produces collisions.
- [x] Existing duplicate-voxel suppression still visits one exact identity only
  once.
- [x] Gravitas 3D same-configuration grid replacement rejects stale collider
  coordinates and returns the replacement partition correctly.
- [x] Repeat the same regression for Gravitas 2D and mixed partition paths.
- [x] Query result ordering and repeated-run determinism remain stable.

Performance evidence:

- [x] Compare traversal/query benchmarks before and after replacing integer
  tokens with `WorldVoxelIndex` values.
- [x] Investigate only a measured regression. Do not restore hash-only identity
  for speed.

Exit criteria:

- [x] No traversal or query path treats an object hash as unique.
- [x] GridForge and Gravitas focused identity/query tests pass.
- [x] Benchmark deltas are recorded and acceptable.

Phase 2 evidence:

- The synthetic hash-collision regression failed on the former integer-token
  path and passes with exact `WorldVoxelIndex`; duplicate exact identities are
  still suppressed.
- Direct traversal benchmark, 4,096 unique plus 4,096 duplicate visits:
  integer-token median `101.008 us`, exact-identity median `133.591 us`, both
  `0 B`. The roughly `4 ns` per-lookup correctness cost is accepted.
- Exact-identity Debug allocation tests cover single and 256-voxel
  unique/duplicate paths at `0 B` after warmup.
- GridForge Debug: `437/437` passed, including the strengthened multi-voxel
  allocation regression.
- Gravitas focused Debug identity/query/order suite: `159/159` passed.
- Gravitas RaycastAll and ConeAll allocation guards: `2/2` passed in Release.
  Debug-only bytes were isolated entirely to pre-existing GridTracer coverage
  tracing; processing all returned voxels through the new exact set is `0 B`.
- The investigation exposed SwiftCollections generic null-guard boxing for
  custom value types. Commit `0baa703` fixes the shared owner with Debug
  `1090/1090` and Release `1093/1093` passing.
- Independent review found no code, determinism, performance, benchmark, or
  test-quality blockers. Its one wording finding was corrected so
  `WorldVoxelIndex.GetHashCode()` explicitly defers authority to equality.

## Phase 3: Independent Same-Bounds Blockers

Intent: let distinct blocker registrations stack even when their geometry is
identical.

- [ ] Replace bounds-derived blocker identity with a world-owned nonzero 64-bit
  registration token.
- [ ] Keep `BoundsKey` solely for geometry equality, bounds lookup, and event
  geometry where appropriate.
- [ ] Update voxel obstacle tracking and obstacle events to carry true obstacle
  registration identity.
- [ ] Preserve direct obstacle APIs with one explicit token contract; do not
  keep a second bounds-as-identity overload.
- [ ] Issue a fresh blocker registration token for each new apply lifetime.
- [ ] Preserve a token while one blocker remains applied across dynamic grid
  add/remove notifications.
- [ ] Keep rollback and partial-coverage cleanup exact for the registration that
  performed the mutation.

Required tests:

- [ ] Two distinct blockers with identical bounds increment obstacle counts
  independently.
- [ ] Removing one identical blocker leaves the other blocker active.
- [ ] Removing the final blocker clears the obstacle.
- [ ] Reapply, rollback, cached coverage, uncached coverage, sparse voxel add,
  and dynamic grid replacement retain correct ownership.

Performance evidence:

- [ ] Compare blocker apply/remove benchmarks and allocations.

Exit criteria:

- [ ] No blocker instance identity is derived from `BoundsKey`.
- [ ] Same-bounds stacking and independent removal pass in dense and sparse
  coverage where applicable.

## Phase 4: Generation-Aware Occupant Tickets

Intent: make an exact occupant ticket identify one registration, not whichever
occupant later occupies the same bucket slot.

- [ ] Replace the raw public integer ticket contract with a small readonly value
  containing the bucket slot and a nonzero world-owned occupant generation.
- [ ] Store the generation beside the occupant in the bucket so lookup and
  removal validate both components in O(1).
- [ ] Update tracked occupancy records, scan-cell APIs, events, and manager
  overloads to use the exact ticket type.
- [ ] Preserve the existing occupant `GlobalId` registry for occupant ownership;
  do not duplicate it with a second global registry.
- [ ] Preserve pooled bucket cleanup and callback-failure recovery.
- [ ] Update XML documentation, wiki examples, and migration guidance for the
  breaking ticket contract.

Required tests:

- [ ] Remove occupant A, add occupant B into the same bucket slot, and prove A's
  stale ticket cannot resolve or remove B.
- [ ] Remove and re-add the same occupant and prove the earlier registration
  ticket remains stale.
- [ ] Exact current tickets still provide O(1) lookup and removal.
- [ ] Scan-cell pooling, world reset, grid replacement, callback failure, and
  tracked-occupancy cleanup do not revive stale tickets.

Performance evidence:

- [ ] Compare occupant add/remove and ticket lookup benchmarks.
- [ ] Confirm the readonly ticket path adds no per-operation managed allocation.

Exit criteria:

- [ ] Public lookup cannot resolve a replacement registration through a stale
  bucket slot.
- [ ] Occupant tracking tests and benchmarks meet the existing correctness and
  allocation bar.

## Phase 5: Cross-Stack Closure

Intent: finish release-ready validation and documentation without removing the
local links needed for the remaining lower-stack hardening work.

- [ ] Update `README.md`, `docs/MIGRATION.md`, and the relevant wiki pages:
  `Core-Concepts.md`, `Determinism-Snapping-and-Pooling.md`,
  `GridTracer-and-Coverage.md`, `Blockers-and-Obstacles.md`,
  `Scan-Cells-and-Query-Flow.md`, and `Occupants-and-Partitions.md`.
- [ ] Update GridForge and Gravitas issue trackers as each defect moves from
  active to resolved.
- [ ] Run GridForge Debug, Release, and ReleaseLean build/test validation.
- [ ] Run the affected GridForge benchmarks and record before/after medians.
- [ ] Run Gravitas Debug, Release, and ReleaseLean validation through the local
  GridForge reference.
- [ ] Run focused Gravitas 2D, 3D, mixed, partition, and query regressions.
- [ ] Run an independent final code review covering correctness, determinism,
  pooling, public API quality, test value, and documentation accuracy.
- [ ] Address review findings and repeat affected verification.
- [ ] Commit coherent implementation and documentation milestones directly to
  `develop`. Keep local project-reference changes uncommitted.

Exit criteria:

- [ ] All four identity defects are resolved with focused regressions.
- [ ] Standard and lean package variants pass.
- [ ] Local-linked Gravitas passes without downstream band-aids.
- [ ] Documentation clearly distinguishes value keys, slots, runtime identities,
  and durable host-owned IDs.
- [ ] The independent reviewer reports no unresolved correctness or release
  blockers.

## Verification Matrix

| Concern | Focused evidence | Full evidence |
| --- | --- | --- |
| Pooled grid generation | identical-config remove/re-add and reset/re-add tests | GridForge Debug/Release/ReleaseLean |
| Cross-world isolation | simultaneous identical-world test | GridForge full suite |
| Voxel deduplication | collision-resistant traversal tests | GridForge traversal benchmarks and full suite |
| Gravitas stale coordinates | 2D, 3D, and mixed replacement regressions | Gravitas Debug/Release/ReleaseLean |
| Same-bounds blockers | independent stack/remove tests | blocker benchmark and GridForge full suite |
| Occupant tickets | stale slot and re-registration tests | occupant benchmark and GridForge full suite |
| Package variants | standard and lean builds/tests | release packaging validation when local links are removed |

## Risks And Controls

| Risk | Control |
| --- | --- |
| Silent generation reuse after numeric overflow | 64-bit nonzero allocation and explicit pre-wrap failure |
| Runtime IDs perturb lockstep order | keep spatial/entity comparers authoritative; test repeated-run ordering |
| Wider identities increase hot-path copy/hash cost | benchmark exact affected paths; retain value types and caller-owned sets |
| Breaking API leaves stale consumers | compile all locally linked GridForge and Gravitas projects together |
| Same-bounds blocker fix removes geometry from events | keep bounds as separate event data; separate geometry from registration identity |
| Ticket hardening adds allocations | readonly value ticket plus bucket entry; benchmark allocations |
| Scope becomes scattered | complete and review one phase before starting the next |

## Progress Log

| Date | Phase | Result |
| --- | --- | --- |
| 2026-07-17 | Planning/RCA | Confirmed pooled-grid alias, cross-world hash collision, voxel hash deduplication risk, same-bounds blocker collapse, and recyclable occupant-ticket alias. Locked phased 64-bit runtime identity approach. |
| 2026-07-17 | Phase 0 | Confirmed local links and owner changes, passed the 429-test Debug baseline, captured lifecycle/traversal/blocker/occupant benchmark baselines, and added two RED stale-grid regressions. |
| 2026-07-17 | Phase 1 | Added process-unique world identity and world-local grid generations, widened identity carriers, passed the focused 5-test suite and full 433-test Debug suite, and committed as `0c5420f`. |
| 2026-07-17 | Phase 2 | Replaced hash-only voxel deduplication with exact identities, removed dead voxel/scan-cell tokens, propagated same-configuration replacement through Gravitas 2D/3D/mixed paths, and fixed a shared SwiftCollections Debug boxing defect exposed by the wider value key. |

## Suggested Commit Sequence

Commit each coherent phase directly to `develop` so release notes and
regressions remain easy to audit:

1. `fix(identity): make world and grid generations allocation-safe`
2. `fix(traversal): deduplicate voxels by exact world identity`
3. `fix(blockers): separate registration identity from bounds`
4. `fix(occupants): make scan-cell tickets generation-aware`
5. `docs(identity): document GridForge runtime identity contracts`
