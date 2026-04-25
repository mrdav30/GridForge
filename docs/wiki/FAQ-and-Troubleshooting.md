# FAQ and Troubleshooting

This page is the fast answer sheet for the most common GridForge questions and failure modes.

When behavior looks surprising, the answer is usually one of these:

- the wrong world instance was used or leaked across runs
- bounds were snapped differently than expected
- pooled state was retained too long
- a blocker, occupant, or partition was used for the wrong job

## Why Does `TryAddGrid(...)` Return `false`?

The most common causes are:

- the target `GridWorld` is inactive
- a grid with the same snapped bounds is already registered in that world
- the bounds were invalid after normalization
- the world has hit capacity

## Why Does A Position That Looks In Bounds Fail To Resolve To A Grid Or Voxel?

Usually one of these is happening:

- the position is outside the snapped bounds, not the original unsnapped bounds
- the grid was never registered in this world
- the world voxel size is not the one you think it is
- the position is just outside an inclusive max boundary due to your own conversion logic

## Why Does The Exact Max Bound Still Resolve To A Voxel?

Because GridForge treats snapped max bounds as inclusive.

## Why Can’t I Register An Occupant?

The add path can fail when:

- the occupant is `null`
- the target voxel is blocked
- the target voxel is already full
- the occupant is already registered to that same voxel identity
- the voxel or scan cell could not be resolved

## Why Did Removing One Blocker Not Unblock The Voxel?

Because obstacle state stacks by token.

## Why Do Tests Or Tools Interfere With Each Other?

Usually because one world instance was reused across scenarios.

Safe pattern:

- create a fresh `GridWorld` for the scenario
- dispose or reset it when the scenario ends
- keep custom voxel-size assumptions local to that world

## Why Did A Stored `WorldVoxelIndex` Stop Resolving Later?

Treat `WorldVoxelIndex` as a runtime identity that should be revalidated, especially after:

- grid removal
- world reset or disposal
- streamed unload and reload cycles

If the referenced world or grid instance is gone or has been replaced by a different runtime allocation, resolution can legitimately fail later.

## Can I Hold Onto Tracer Or Query Results?

Usually, no.

Many query paths use pooled collections internally. Consume results immediately and copy what you need into your own owned data structure if it must survive past the immediate operation.

## When Should I Use `Reset()` Vs `Reset(deactivate: true)`?

Use `Reset()` when:

- you want to clear the current world but keep it active

Use `Reset(deactivate: true)` when:

- you want a full teardown
- you are about to use different voxel or spatial-hash settings
- you want to guarantee the next run starts from an inactive state

## Quick Troubleshooting Checklist

1. Are you querying the world instance you think you are?
2. What are the snapped bounds after world normalization?
3. Is that world's `VoxelSize` the same one the scenario was designed for?
4. Is the voxel blocked, occupied, or both?
5. Are you holding pooled data past the immediate operation?
6. Did a previous test, benchmark, or tool run leave world state behind?
