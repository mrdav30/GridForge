# GridWorld Refactor Plan

This document tracks the breaking refactor from one process-wide static world to explicit world contexts. The goal is to let multiple isolated worlds exist in the same process without leaking grid identity, voxel identity, blockers, occupants, or setup-wide configuration across world boundaries.

## Status

- Started: 2026-04-24
- Release posture: Breaking release
- Backwards compatibility: Explicitly out of scope
- Current state: Planning

## Scope

- In:
  - Replace ambient global world ownership with an explicit `GridWorld`-style runtime owner.
  - Make grid registration, lookup, tracing, blockers, occupants, and scan queries world-scoped.
  - Make runtime identities world-scoped and safe against slot reuse.
  - Rewrite tests, benchmarks, docs, and examples around explicit world setup.
- Out:
  - Compatibility shims for the existing `GlobalGridManager` API.
  - Universe, galaxy, save-game, or streaming orchestration above the world layer.
  - Engine-specific integration work outside the core library.

## Why This Refactor Exists

Today GridForge assumes one shared runtime world:

- `GlobalGridManager` owns active grids, setup values, spatial hashing, and top-level events.
- `GridConfiguration` snaps bounds against ambient global voxel settings.
- `VoxelGrid`, `GridTracer`, `Blocker`, `GridOccupantManager`, and `GridScanManager` all route through shared state.
- `GlobalVoxelIndex` is grid-scoped, not world-scoped.

That model works for one world composed of many grids or chunks. It does not cleanly support multiple isolated worlds loaded at once.

## Why This Breaking Change Is Worth It

We are doing this as a breaking change because the current architecture makes GridForge itself the world boundary. That is the core constraint we want to remove.

After this refactor, GridForge should become the world primitive instead of the world container. That shift is what makes it practical to build higher-level systems on top without fighting ambient global state.

Concretely, this refactor should make it much easier to build:

- universe or galaxy registries that own multiple `GridWorld` instances
- streamed world loading and unloading without cross-world state leakage
- save and load flows keyed by world identity
- per-world simulation ticks, rulesets, and configuration
- isolated tests and tools that do not interfere through shared runtime state

This is also the right time to take the break because:

- the single-world assumption is still concentrated in the core architecture
- preserving the old API would slow the refactor and keep the wrong boundary alive
- the library will be easier to reason about if world ownership is explicit everywhere

## Target Architecture

- Introduce a first-class `GridWorld` runtime owner for mutable state.
- Retire `GlobalGridManager` as the owner of runtime state.
- Keep `GlobalGridManager` temporarily during the branch as a migration facade so downstream systems can be moved in controlled phases, then remove it before release.
- Keep only pure stateless helpers static, and move them if a better home becomes obvious.
- Make `GridWorld` own voxel size, spatial hash size, active grids, bounds tracking, spatial hash, versioning, locking, and world-level events.
- Make each `VoxelGrid` belong to exactly one `GridWorld`.
- Require a world context for world-space queries unless a future API is intentionally cross-world.
- Make voxel and grid identities world-scoped and resilient to slot reuse through spawn or instance tokens.
- Keep deterministic behavior unchanged for a single world configured the same way.

## Success Criteria

- Two or more worlds can coexist in one process without sharing mutable runtime state.
- The same local positions and grid indices can exist in different worlds without collisions.
- Changing voxel size or spatial hash size in one world does not affect another world.
- Blockers, occupants, tracing, and scan queries operate only on the world they are bound to.
- Existing single-world behavior remains functionally equivalent after API migration.
- The full test suite passes after the refactor and new multi-world coverage exists.

## Progress Tracker

- [ ] Phase 0: Lock the new world model and migration boundaries.
- [ ] Phase 1: Introduce `GridWorld` core runtime ownership.
- [ ] Phase 2: Move grid identity and lookup to world scope.
- [ ] Phase 3: Move mutation and query services to world scope.
- [ ] Phase 4: Rebuild validation, docs, and benchmarks around explicit worlds.
- [ ] Phase 5: Ship the breaking release cleanup.

## Phase 0: Lock The Model

Intent: agree on the new ownership and identity model before touching the hot paths.

- [ ] Decide the final naming for the world owner type and related identity types.
- [x] Decide whether `GlobalGridManager` is deleted outright or retained temporarily only during the branch as an internal migration aid.
- [ ] Decide the minimum world-scoped identity payload for grids and voxels.
- [ ] Decide which helpers remain static because they are pure and which move onto `GridWorld`.
- [ ] Write a short decision record in this file once the names and boundaries are locked.

Exit criteria:

- [ ] There is one agreed runtime owner for mutable world state.
- [ ] There is one agreed identity story for world, grid, and voxel references.
- [ ] There is no unresolved ambiguity about whether lookups require a world context.

## Phase 1: Introduce `GridWorld` Core Ownership

Intent: create the new owner of mutable runtime state before migrating downstream systems.

Likely files:

- `src/GridForge/Grids/Managers/GlobalGridManager.cs`
- `src/GridForge/Configuration/GridConfiguration.cs`
- `src/GridForge/Grids/VoxelGrid.cs`
- new world runtime files under `src/GridForge`

Checklist:

- [ ] Add a new world runtime type that owns voxel size, spatial hash size, active grids, bounds tracking, spatial hash, version, lock, and world-level events.
- [ ] Move setup and reset behavior from the process-wide model onto the world runtime.
- [ ] Remove ambient snapping from `GridConfiguration` so it becomes pure input data.
- [ ] Move snapping and normalization to world registration or world utility code.
- [ ] Update grid registration so a grid is initialized against an owning world instead of ambient globals.
- [ ] Decide how pooled grids learn their owning world during initialize and how that ownership is cleared on reset.

Exit criteria:

- [ ] A single world can be created, configured, reset, and disposed without using global mutable state.
- [ ] Grid registration works through the new world runtime.
- [ ] Bounds snapping no longer depends on global ambient setup.

## Phase 2: Move Identity And Lookup To World Scope

Intent: remove the single global namespace assumption from runtime identity and lookup.

Likely files:

- `src/GridForge/Spatial/GlobalVoxelIndex.cs`
- `src/GridForge/Grids/Managers/GlobalGridManager.cs`
- `src/GridForge/Grids/VoxelGrid.cs`
- `src/GridForge/Grids/Nodes/Voxel.cs`
- `src/GridForge/Grids/Support/GridEventInfo.cs`

Checklist:

- [ ] Replace or rename `GlobalVoxelIndex` so voxel identity includes world scope as well as grid scope.
- [ ] Add a world instance or spawn token so stale references fail safely after world teardown and slot reuse.
- [ ] Update grid identity and event payloads to carry world context where required.
- [ ] Move grid lookup APIs from global static entry points onto the world runtime.
- [ ] Update `VoxelGrid` and `Voxel` paths that currently resolve neighbors or lookups through `GlobalGridManager`.
- [ ] Revisit any comments, XML docs, and terminology that still describe grid indices as globally unique.

Exit criteria:

- [ ] Grid and voxel identities are unambiguous across multiple loaded worlds.
- [ ] World-space and identity-based lookups are world-scoped.
- [ ] Boundary neighbor traversal still works when multiple worlds are loaded.

## Phase 3: Move Mutation And Query Services To World Scope

Intent: migrate the systems that currently depend on static global routing.

Likely files:

- `src/GridForge/Utility/GridTracer.cs`
- `src/GridForge/Blockers/Blocker.cs`
- `src/GridForge/Grids/Managers/GridObstacleManager.cs`
- `src/GridForge/Grids/Managers/GridOccupantManager.cs`
- `src/GridForge/Grids/Managers/GridScanManager.cs`

Checklist:

- [ ] Convert `GridTracer` APIs to operate against an explicit world.
- [ ] Move blocker grid-watching from process-wide global events to world-level events.
- [ ] Make occupant tracking registries world-scoped rather than process-scoped.
- [ ] Make scan query entry points world-scoped.
- [ ] Make obstacle mutation notifications route through world-owned lookup and versioning.
- [ ] Audit pooled temporary collections and caches for assumptions that one global world exists.

Exit criteria:

- [ ] Blockers in one world never react to grid changes in another world.
- [ ] Occupants and scan queries cannot cross world boundaries accidentally.
- [ ] Tracing and coverage enumeration operate only on the specified world.

## Phase 4: Rebuild Validation, Benchmarks, And Docs

Intent: update the validation surface so the new model is enforced and understandable.

Likely files:

- `tests/GridForge.Tests/**/*`
- `tests/GridForge.Benchmarks/**/*`
- `README.md`
- `GridForge.wiki/**/*`
- `AGENTS.md`

Checklist:

- [ ] Replace shared static-world test setup with explicit world fixtures or factories.
- [ ] Add coverage for multiple worlds loaded simultaneously with overlapping local coordinates.
- [ ] Add coverage for world teardown, slot reuse, and stale identity rejection.
- [ ] Add coverage for blockers, occupants, tracing, and scan queries staying inside their world.
- [ ] Update benchmarks to construct and tear down explicit worlds during setup.
- [ ] Rewrite README and wiki examples to start with world creation instead of `GlobalGridManager.Setup()`.

Exit criteria:

- [ ] Tests cover both single-world parity and multi-world isolation.
- [ ] Benchmarks still run and reflect the new initialization model.
- [ ] Docs no longer describe GridForge as process-wide shared world state.

## Phase 5: Ship The Breaking Cleanup

Intent: remove migration leftovers and prepare the breaking release.

Checklist:

- [ ] Delete obsolete static-world APIs and files that no longer belong in the architecture.
- [ ] Rename types and folders to match the final terminology if temporary migration names were used.
- [ ] Run a full build and test pass for all supported target frameworks.
- [ ] Run targeted benchmarks for registration, tracing, blockers, occupancy, and scan queries.
- [ ] Update package versioning, release notes, and migration notes for the breaking change.
- [ ] Record any known follow-up work that is intentionally deferred beyond this release.

Exit criteria:

- [ ] No public API path relies on process-wide mutable world state.
- [ ] The repository builds, tests, and documents the new model cleanly.
- [ ] Release notes explain the breaking change and the new usage pattern.

## Validation Gates

Use these commands as the default validation gates during implementation:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
```

Add targeted test or benchmark commands here if the refactor introduces new hot spots that need tighter loops.

## Risks To Watch

- Determinism drift if snapping or world-owned configuration changes semantics.
- Hidden ambient-global assumptions in pooled objects, caches, or static registries.
- Event leakage where listeners accidentally subscribe across worlds.
- Identity bugs caused by world or grid slot reuse.
- Test fragility while the old and new models coexist during the migration branch.
- Allocation regressions if world-scoped ownership causes duplicate temporary structures.

## Open Questions

- Should pure snapping helpers live on `GridWorld`, a separate static utility, or the configuration layer?
- Does the final API need a lightweight top-level registry for named worlds, or should world lifetime stay fully application-owned?
- Do we want one world-specific blocker base type, or should blockers receive world context only at construction or apply time?

## Decision Log

| Date | Decision | Notes |
| --- | --- | --- |
| 2026-04-24 | Treat the refactor as a breaking release. | Backwards compatibility is intentionally out of scope. |
| 2026-04-24 | Keep `GlobalGridManager` during implementation as a temporary migration facade. | Delete it in Phase 5 once the world-scoped APIs fully replace it. |
| 2026-04-24 | Use the refactor to move GridForge from being the world boundary to being the world primitive. | This is the primary architectural reason for accepting the break. |

## Working Notes

Use this section for short implementation notes, surprises, and follow-up items discovered during the refactor.
