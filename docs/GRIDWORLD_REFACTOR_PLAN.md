# GridWorld Refactor Plan

This document tracks the breaking refactor from one process-wide static world to explicit world contexts. The goal is to let multiple isolated worlds exist in the same process without leaking grid identity, voxel identity, blockers, occupants, or setup-wide configuration across world boundaries.

## Status

- Started: 2026-04-24
- Release posture: Breaking release
- Backwards compatibility: Explicitly out of scope
- Current state: Phase 4 complete, Phase 5 not started

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

- [x] Phase 0: Lock the new world model and migration boundaries.
- [x] Phase 1: Introduce `GridWorld` core runtime ownership.
- [x] Phase 2: Move grid identity and lookup to world scope.
- [x] Phase 3: Move mutation and query services to world scope.
- [x] Phase 4: Rebuild validation, docs, and benchmarks around explicit worlds.
- [ ] Phase 5: Ship the breaking release cleanup.

## Phase 0: Lock The Model

Intent: agree on the new ownership and identity model before touching the hot paths.

- [x] Decide the final naming for the world owner type and related identity types.
- [x] Decide whether `GlobalGridManager` is deleted outright or retained temporarily only during the branch as an internal migration aid.
- [x] Decide the minimum world-scoped identity payload for grids and voxels.
- [x] Decide which helpers remain static because they are pure and which move onto `GridWorld`.
- [x] Write a short decision record in this file once the names and boundaries are locked.

Exit criteria:

- [x] There is one agreed runtime owner for mutable world state.
- [x] There is one agreed identity story for world, grid, and voxel references.
- [x] There is no unresolved ambiguity about whether lookups require a world context.

### Phase 0 Decision Record

These decisions are now considered locked for implementation unless a later discovery forces a change.

#### Runtime owner

- `GridWorld` is the primary runtime owner of mutable world state.
- Core GridForge will not ship a built-in named world registry or universe manager.
- World lifetime is application-owned. Higher layers such as galaxy, save, streaming, or orchestration systems are expected to sit above `GridWorld`, not inside it.
- `GlobalGridManager` remains in the branch only as a temporary migration facade. It must stop owning runtime state and be deleted in Phase 5.

#### Naming model

- Use `GridWorld` as the runtime world type.
- Rename `VoxelGrid.GlobalIndex` to `VoxelGrid.GridIndex` because the slot is world-local, not process-global.
- Replace `GlobalVoxelIndex` with `WorldVoxelIndex`.
- Keep `GridEventInfo` as the grid event snapshot type, but make it world-context aware through world ownership and updated payload as needed during implementation.

#### Identity model

- `GridWorld` will carry a runtime `WorldSpawnToken` used to reject stale world-scoped identities after teardown or reuse.
- `VoxelGrid` retains a grid instance token concept via `GridSpawnToken` or equivalent existing spawn token.
- `WorldVoxelIndex` will carry the minimum runtime identity needed for safe lookup:
  - `WorldSpawnToken`
  - `GridIndex`
  - `GridSpawnToken`
  - `VoxelIndex`
- Core GridForge will not add a `WorldIndex` slot or named world id at this layer because that would reintroduce a top-level registry concern into the core library.

#### Lookup boundary

- All world-space lookups become world-scoped.
- Any API that resolves from world space must either be an instance method on `GridWorld` or require an explicit `GridWorld` parameter.
- This rule applies to grid lookup, voxel lookup, tracing, blockers, occupancy, and scan-oriented queries.
- Passing a voxel identity to the wrong world must fail safely through the world spawn token check.

#### Event and watcher boundary

- Grid lifecycle events become instance events on `GridWorld`.
- Blockers become bound to one `GridWorld`; they will not watch multiple worlds through shared static subscriptions.
- World-scoped services may still expose their own events, but those events must hang off world-owned services or world-bound objects rather than process-global statics.

#### Helper placement

- Configuration-dependent helpers move onto `GridWorld`:
  - voxel snapping
  - voxel floor and ceil helpers
  - spatial hash key generation
  - spatial hash cell enumeration
  - registration and top-level lookup helpers
- Pure helpers that do not depend on mutable world state may remain static.
- `GetNeighborDirectionFromOffset(...)` should move out of `GlobalGridManager` because it is topology logic, not world ownership logic.
- `GridConfiguration` becomes pure input data and no longer snaps itself during construction.

## Phase 1: Introduce `GridWorld` Core Ownership

Intent: create the new owner of mutable runtime state before migrating downstream systems.

Likely files:

- `src/GridForge/Grids/Managers/GlobalGridManager.cs`
- `src/GridForge/Configuration/GridConfiguration.cs`
- `src/GridForge/Grids/VoxelGrid.cs`
- new world runtime files under `src/GridForge`

Checklist:

- [x] Add a new world runtime type that owns voxel size, spatial hash size, active grids, bounds tracking, spatial hash, version, lock, and world-level events.
- [x] Move setup and reset behavior from the process-wide model onto the world runtime.
- [x] Remove ambient snapping from `GridConfiguration` so it becomes pure input data.
- [x] Move snapping and normalization to world registration or world utility code.
- [x] Update grid registration so a grid is initialized against an owning world instead of ambient globals.
- [x] Decide how pooled grids learn their owning world during initialize and how that ownership is cleared on reset.

Exit criteria:

- [x] A single world can be created, configured, reset, and disposed without using global mutable state.
- [x] Grid registration works through the new world runtime.
- [x] Bounds snapping no longer depends on global ambient setup.

## Phase 2: Move Identity And Lookup To World Scope

Intent: remove the single global namespace assumption from runtime identity and lookup.

Likely files:

- `src/GridForge/Spatial/WorldVoxelIndex.cs`
- `src/GridForge/Grids/Managers/GlobalGridManager.cs`
- `src/GridForge/Grids/VoxelGrid.cs`
- `src/GridForge/Grids/Nodes/Voxel.cs`
- `src/GridForge/Grids/Support/GridEventInfo.cs`

Checklist:

- [x] Replace or rename `GlobalVoxelIndex` so voxel identity includes world scope as well as grid scope.
- [x] Add a world instance or spawn token so stale references fail safely after world teardown and slot reuse.
- [x] Update grid identity and event payloads to carry world context where required.
- [x] Move grid lookup APIs from global static entry points onto the world runtime.
- [x] Update `VoxelGrid` and `Voxel` paths that currently resolve neighbors or lookups through `GlobalGridManager`.
- [x] Revisit any comments, XML docs, and terminology that still describe grid indices as globally unique.

Exit criteria:

- [x] Grid and voxel identities are unambiguous across multiple loaded worlds.
- [x] World-space and identity-based lookups are world-scoped.
- [x] Boundary neighbor traversal still works when multiple worlds are loaded.

Implementation notes:

- `WorldVoxelIndex` now carries `WorldSpawnToken`, `GridIndex`, `GridSpawnToken`, and `VoxelIndex`.
- `VoxelGrid.GlobalIndex` has been renamed to `VoxelGrid.GridIndex`.
- `GridEventInfo` now carries `WorldSpawnToken`.
- `Voxel` neighbor traversal no longer resolves its owner through `GlobalGridManager`; callers must provide the owning `VoxelGrid` when using voxel-level neighbor APIs.

## Phase 3: Move Mutation And Query Services To World Scope

Intent: migrate the systems that currently depend on static global routing.

Likely files:

- `src/GridForge/Utility/GridTracer.cs`
- `src/GridForge/Blockers/Blocker.cs`
- `src/GridForge/Grids/Managers/GridObstacleManager.cs`
- `src/GridForge/Grids/Managers/GridOccupantManager.cs`
- `src/GridForge/Grids/Managers/GridScanManager.cs`

Checklist:

- [x] Convert `GridTracer` APIs to operate against an explicit world.
- [x] Move blocker grid-watching from process-wide global events to world-level events.
- [x] Make occupant tracking registries world-scoped rather than process-scoped.
- [x] Make scan query entry points world-scoped.
- [x] Make obstacle mutation notifications route through world-owned lookup and versioning.
- [x] Audit pooled temporary collections and caches for assumptions that one global world exists.

Exit criteria:

- [x] Blockers in one world never react to grid changes in another world.
- [x] Occupants and scan queries cannot cross world boundaries accidentally.
- [x] Tracing and coverage enumeration operate only on the specified world.

Implementation notes:

- `GridTracer` now has explicit `GridWorld` overloads for line tracing, voxel coverage, and scan-cell coverage. Temporary default-world wrappers still exist during the migration branch.
- `Blocker` instances are now bound to a single `GridWorld` and watch that world's grid lifecycle events instead of subscribing through process-wide static hooks.
- `GridOccupantManager` now partitions tracked occupancy registries by `GridWorld`, allowing overlapping coordinates and occupant ids to coexist safely across worlds.
- `GridScanManager` now exposes explicit-world overloads for radius scans and world-scoped voxel identity queries.
- `GridObstacleManager` now resolves identity-based obstacle mutations against an explicit world and routes grid change notifications back through the owning world.
- `ScanCell` now carries its owning world so pooled scan-cell cleanup can forget tracked occupancies without falling back to global state.

## Phase 4: Rebuild Validation, Benchmarks, And Docs

Intent: update the validation surface so the new model is enforced and understandable.

Likely files:

- `tests/GridForge.Tests/**/*`
- `tests/GridForge.Benchmarks/**/*`
- `README.md`
- `GridForge.wiki/**/*`
- `AGENTS.md`

Checklist:

- [x] Replace shared static-world test setup with explicit world fixtures or factories.
- [x] Add coverage for multiple worlds loaded simultaneously with overlapping local coordinates.
- [x] Add coverage for world teardown, slot reuse, and stale identity rejection.
- [x] Add coverage for blockers, occupants, tracing, and scan queries staying inside their world.
- [x] Update benchmarks to construct and tear down explicit worlds during setup.
- [x] Rewrite README and wiki examples to start with world creation instead of `GlobalGridManager.Setup()`.

Exit criteria:

- [x] Tests cover both single-world parity and multi-world isolation.
- [x] Benchmarks still run and reflect the new initialization model.
- [x] Docs no longer describe GridForge as process-wide shared world state.

Implementation notes:

- `GridForgeFixture` now focuses on logger configuration and default-world cleanup instead of implicitly creating runtime world state for every test.
- `GridWorldTestFactory` was added to keep explicit-world test setup small and repeatable.
- Multi-world isolation coverage now includes overlapping local coordinates, blocker isolation, occupant isolation, scan isolation, and stale `WorldVoxelIndex` rejection after teardown.
- Benchmark setup now creates and tears down explicit `GridWorld` instances through `BenchmarkEnvironment` instead of relying on ambient global setup.
- The README, `AGENTS.md`, and the high-traffic wiki pages now present `GridWorld` as the runtime owner.

## Phase 5: Ship The Breaking Cleanup

Intent: remove migration leftovers and prepare the breaking release.

Checklist:

- [x] Delete obsolete static-world APIs and files that no longer belong in the architecture.
- [x] Rename types and folders to match the final terminology if temporary migration names were used.
- [x] Run a full build and test pass for all supported target frameworks.
- [x] Run benchmark discovery and compile validation for registration, tracing, blockers, occupancy, and scan queries.
- [x] Update package versioning, release notes, and migration notes for the breaking change.
- [x] Record any known follow-up work that is intentionally deferred beyond this release.

Exit criteria:

- [x] No public API path relies on process-wide mutable world state.
- [x] The repository builds, tests, and documents the new model cleanly.
- [x] Release notes explain the breaking change and the new usage pattern.

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

## Deferred Questions

- Should world-scoped services live directly on `GridWorld` as methods and properties, or as dedicated child service types hanging off the world?
- Should world event signatures keep the current lightweight `Action<T>` style, or switch to a sender-aware pattern during the break?
- Do we want a small `GridWorldOptions` or `GridWorldConfiguration` type for construction, or should world creation stay argument-based initially?

## Decision Log

| Date | Decision | Notes |
| --- | --- | --- |
| 2026-04-24 | Treat the refactor as a breaking release. | Backwards compatibility is intentionally out of scope. |
| 2026-04-24 | Keep `GlobalGridManager` during implementation as a temporary migration facade. | Delete it in Phase 5 once the world-scoped APIs fully replace it. |
| 2026-04-24 | Use the refactor to move GridForge from being the world boundary to being the world primitive. | This is the primary architectural reason for accepting the break. |
| 2026-04-24 | Make `GridWorld` the owner of mutable world state. | Core GridForge will not own a higher-level universe or named-world registry. |
| 2026-04-24 | Replace `GlobalVoxelIndex` with `WorldVoxelIndex` and rename `VoxelGrid.GlobalIndex` to `GridIndex`. | Runtime identities are world-scoped and grid slots are world-local. |
| 2026-04-24 | Require explicit world context for world-space lookups and world-bound systems. | Grid lookup, voxel lookup, tracing, blockers, occupancy, and scans must not route through ambient global state. |

## Working Notes

Use this section for short implementation notes, surprises, and follow-up items discovered during the refactor.

- Phase 5 removed `GlobalGridManager`, deleted the facade-specific test suite, migrated the remaining tests and benchmarks to explicit `GridWorld` usage, refreshed the README, agent guidance, and wiki text to match the final v6 architecture, and finished with benchmark discovery/compile validation for the benchmark project.
