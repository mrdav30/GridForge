using System;
using System.Threading;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class GridObstacleManagerOwnershipTests
{
    [Theory]
    [InlineData(GridStorageKind.Dense, false, ObstacleMutation.Add)]
    [InlineData(GridStorageKind.Dense, false, ObstacleMutation.Remove)]
    [InlineData(GridStorageKind.Dense, false, ObstacleMutation.Clear)]
    [InlineData(GridStorageKind.Dense, true, ObstacleMutation.Add)]
    [InlineData(GridStorageKind.Dense, true, ObstacleMutation.Remove)]
    [InlineData(GridStorageKind.Dense, true, ObstacleMutation.Clear)]
    [InlineData(GridStorageKind.Sparse, false, ObstacleMutation.Add)]
    [InlineData(GridStorageKind.Sparse, false, ObstacleMutation.Remove)]
    [InlineData(GridStorageKind.Sparse, false, ObstacleMutation.Clear)]
    [InlineData(GridStorageKind.Sparse, true, ObstacleMutation.Add)]
    [InlineData(GridStorageKind.Sparse, true, ObstacleMutation.Remove)]
    [InlineData(GridStorageKind.Sparse, true, ObstacleMutation.Clear)]
    public void DirectMutation_ShouldRejectForeignVoxelWithoutChangingStateOrPublishingEvents(
        GridStorageKind storageKind,
        bool differentWorld,
        ObstacleMutation mutation)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        using GridWorld secondWorld = GridWorldTestFactory.CreateWorld();
        GridWorld otherWorld = differentWorld ? secondWorld : world;
        VoxelGrid owner = AddGrid(world, storageKind, Vector3d.Zero, out Voxel target);
        VoxelGrid other = AddGrid(otherWorld, storageKind, new Vector3d(10, 0, 0), out Voxel otherVoxel);
        ObstacleToken originalToken = world.AllocateObstacleToken();
        ObstacleToken additionalToken = world.AllocateObstacleToken();
        ObstacleToken otherToken = otherWorld.AllocateObstacleToken();
        Assert.True(owner.TryAddObstacle(target, originalToken));
        Assert.True(other.TryAddObstacle(otherVoxel, otherToken));
        uint ownerVersion = owner.Version;
        uint otherVersion = other.Version;
        uint worldVersion = world.Version;
        uint secondWorldVersion = secondWorld.Version;
        uint voxelVersion = target.CachedGridVersion;
        WorldVoxelIndex targetIndex = target.WorldIndex;
        int managerNotifications = 0;
        int voxelNotifications = 0;
        int worldNotifications = 0;
        GridEventInfo committed = default;
        WorldVoxelIndex notifiedIndex = default;
        Action<ObstacleEventInfo> managerHandler = info =>
        {
            managerNotifications++;
            notifiedIndex = info.VoxelIndex;
        };
        Action<ObstacleClearEventInfo> clearManagerHandler = info =>
        {
            managerNotifications++;
            notifiedIndex = info.VoxelIndex;
        };
        Action<ObstacleEventInfo> voxelHandler = _ => voxelNotifications++;
        Action<ObstacleClearEventInfo> clearVoxelHandler = _ => voxelNotifications++;
        Action<GridEventInfo> worldHandler = info =>
        {
            worldNotifications++;
            committed = info;
        };

        GridObstacleManager.OnObstacleAdded += managerHandler;
        GridObstacleManager.OnObstacleRemoved += managerHandler;
        GridObstacleManager.OnObstaclesCleared += clearManagerHandler;
        target.OnObstacleAdded += voxelHandler;
        target.OnObstacleRemoved += voxelHandler;
        target.OnObstaclesCleared += clearVoxelHandler;
        world.OnChangeCommitted += worldHandler;
        secondWorld.OnChangeCommitted += worldHandler;
        try
        {
            Mutate(other, target, mutation, originalToken, additionalToken, expectedSuccess: false);

            Assert.Equal(1, target.ObstacleCount);
            Assert.Equal(1, owner.ObstacleCount);
            Assert.Equal(1, otherVoxel.ObstacleCount);
            Assert.Equal(1, other.ObstacleCount);
            Assert.Equal(ownerVersion, owner.Version);
            Assert.Equal(otherVersion, other.Version);
            Assert.Equal(worldVersion, world.Version);
            Assert.Equal(secondWorldVersion, secondWorld.Version);
            Assert.Equal(voxelVersion, target.CachedGridVersion);
            Assert.Equal(0, managerNotifications);
            Assert.Equal(0, voxelNotifications);
            Assert.Equal(0, worldNotifications);

            Mutate(owner, target, mutation, originalToken, additionalToken, expectedSuccess: true);

            int expectedCount = mutation == ObstacleMutation.Add ? 2 : 0;
            Assert.Equal(expectedCount, target.ObstacleCount);
            Assert.Equal(expectedCount, owner.ObstacleCount);
            Assert.Equal(ownerVersion + 1, owner.Version);
            Assert.Equal(owner.Version, target.CachedGridVersion);
            Assert.Equal(otherVersion, other.Version);
            Assert.Equal(1, otherVoxel.ObstacleCount);
            Assert.Equal(1, other.ObstacleCount);
            Assert.Equal(1, managerNotifications);
            Assert.Equal(1, voxelNotifications);
            Assert.Equal(1, worldNotifications);
            Assert.Equal(targetIndex, notifiedIndex);
            Assert.Equal(targetIndex.WorldSpawnToken, committed.WorldSpawnToken);
            Assert.Equal(targetIndex.GridSpawnToken, committed.GridSpawnToken);
            Assert.Equal(targetIndex.GridIndex, committed.GridIndex);
            Assert.Equal(targetIndex.VoxelIndex, committed.VoxelIndex);
            Assert.Equal(owner.Version, committed.GridVersion);
            Assert.Equal(expectedCount, committed.ObstacleCount);
            Assert.Equal(mutation switch
            {
                ObstacleMutation.Add => GridEventKind.ObstacleAdded,
                ObstacleMutation.Remove => GridEventKind.ObstacleRemoved,
                _ => GridEventKind.ObstaclesCleared,
            }, committed.ChangeKind);
        }
        finally
        {
            GridObstacleManager.OnObstacleAdded -= managerHandler;
            GridObstacleManager.OnObstacleRemoved -= managerHandler;
            GridObstacleManager.OnObstaclesCleared -= clearManagerHandler;
            target.OnObstacleAdded -= voxelHandler;
            target.OnObstacleRemoved -= voxelHandler;
            target.OnObstaclesCleared -= clearVoxelHandler;
            world.OnChangeCommitted -= worldHandler;
            secondWorld.OnChangeCommitted -= worldHandler;
        }
    }

    [Theory]
    [InlineData(GridStorageKind.Dense, IdentityMismatch.World)]
    [InlineData(GridStorageKind.Dense, IdentityMismatch.GridSlot)]
    [InlineData(GridStorageKind.Dense, IdentityMismatch.GridGeneration)]
    [InlineData(GridStorageKind.Dense, IdentityMismatch.MissingCoordinate)]
    [InlineData(GridStorageKind.Dense, IdentityMismatch.ForeignPhysicalVoxel)]
    [InlineData(GridStorageKind.Sparse, IdentityMismatch.World)]
    [InlineData(GridStorageKind.Sparse, IdentityMismatch.GridSlot)]
    [InlineData(GridStorageKind.Sparse, IdentityMismatch.GridGeneration)]
    [InlineData(GridStorageKind.Sparse, IdentityMismatch.MissingCoordinate)]
    [InlineData(GridStorageKind.Sparse, IdentityMismatch.ForeignPhysicalVoxel)]
    public void DirectMutation_ShouldRejectStaleOrCopiedIdentityWithoutChangingObstacleState(
        GridStorageKind storageKind,
        IdentityMismatch mismatch)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        using GridWorld secondWorld = GridWorldTestFactory.CreateWorld();
        VoxelGrid previous = AddGrid(world, storageKind, Vector3d.Zero, out Voxel previousVoxel);
        WorldVoxelIndex previousIndex = previousVoxel.WorldIndex;
        world.Reset();
        VoxelGrid owner = AddGrid(world, storageKind, Vector3d.Zero, out Voxel target);
        VoxelGrid foreign = AddGrid(world, storageKind, new Vector3d(10, 0, 0), out Voxel foreignVoxel);
        ObstacleToken token = world.AllocateObstacleToken();
        ObstacleToken additionalToken = world.AllocateObstacleToken();
        Assert.True(owner.TryAddObstacle(target, token));
        Assert.True(foreign.TryAddObstacle(foreignVoxel, token));
        WorldVoxelIndex originalIndex = target.WorldIndex;
        WorldVoxelIndex originalForeignIndex = foreignVoxel.WorldIndex;
        WorldVoxelIndex invalidIndex = originalIndex;
        Voxel supplied = target;
        switch (mismatch)
        {
            case IdentityMismatch.World:
                invalidIndex.WorldSpawnToken = secondWorld.SpawnToken;
                break;
            case IdentityMismatch.GridSlot:
                invalidIndex.GridIndex = foreign.GridIndex;
                break;
            case IdentityMismatch.GridGeneration:
                invalidIndex.GridSpawnToken = previousIndex.GridSpawnToken;
                break;
            case IdentityMismatch.MissingCoordinate:
                invalidIndex.VoxelIndex = new VoxelIndex(99, 0, 0);
                break;
            case IdentityMismatch.ForeignPhysicalVoxel:
                supplied = foreignVoxel;
                break;
        }

        uint ownerVersion = owner.Version;
        uint foreignVersion = foreign.Version;
        int notifications = 0;
        Action<GridEventInfo> handler = _ => notifications++;
        world.OnChangeCommitted += handler;
        supplied.WorldIndex = invalidIndex;
        try
        {
            Assert.False(owner.TryAddObstacle(supplied, additionalToken));
            Assert.False(owner.TryRemoveObstacle(supplied, token));
            owner.ClearObstacles(supplied);

            Assert.Equal(1, owner.ObstacleCount);
            Assert.Equal(1, target.ObstacleCount);
            Assert.Equal(1, foreign.ObstacleCount);
            Assert.Equal(1, foreignVoxel.ObstacleCount);
            Assert.Equal(ownerVersion, owner.Version);
            Assert.Equal(foreignVersion, foreign.Version);
            Assert.Equal(0, notifications);
        }
        finally
        {
            target.WorldIndex = originalIndex;
            foreignVoxel.WorldIndex = originalForeignIndex;
            world.OnChangeCommitted -= handler;
        }

        Assert.True(owner.TryRemoveObstacle(target, token));
        Assert.True(foreign.TryRemoveObstacle(foreignVoxel, token));
    }

    [Theory]
    [InlineData(GridStorageKind.Dense, GridTeardown.Remove)]
    [InlineData(GridStorageKind.Dense, GridTeardown.Reset)]
    [InlineData(GridStorageKind.Dense, GridTeardown.Deactivate)]
    [InlineData(GridStorageKind.Sparse, GridTeardown.Remove)]
    [InlineData(GridStorageKind.Sparse, GridTeardown.Reset)]
    [InlineData(GridStorageKind.Sparse, GridTeardown.Deactivate)]
    public void Teardown_ShouldClearOwnedObstacleStateAndInvalidateSavedIdentity(
        GridStorageKind storageKind,
        GridTeardown teardown)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            storageKind: storageKind);
        VoxelIndex[] indices = { new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 0), new VoxelIndex(2, 0, 0) };
        Assert.True(world.TryAddGrid(configuration, indices, out ushort gridIndex));
        Assert.True(world.TryGetGrid(gridIndex, out VoxelGrid original));
        Voxel[] voxels = new Voxel[indices.Length];
        WorldVoxelIndex[] savedIndices = new WorldVoxelIndex[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            Assert.True(original.TryGetVoxel(indices[i], out voxels[i]));
            savedIndices[i] = voxels[i].WorldIndex;
            Assert.True(original.TryAddObstacle(voxels[i], world.AllocateObstacleToken()));
            Assert.True(original.TryAddObstacle(voxels[i], world.AllocateObstacleToken()));
        }
        WorldVoxelIndex staleIndex = savedIndices[0];
        SwiftList<ObstacleClearEventInfo> clearNotifications = new SwiftList<ObstacleClearEventInfo>();
        SwiftList<GridEventInfo> committedClearNotifications = new SwiftList<GridEventInfo>();
        Action<ObstacleClearEventInfo> clearHandler = info =>
        {
            clearNotifications.Add(info);
        };
        Action<GridEventInfo> committedHandler = info =>
        {
            if (info.ChangeKind == GridEventKind.ObstaclesCleared)
                committedClearNotifications.Add(info);
        };

        GridObstacleManager.OnObstaclesCleared += clearHandler;
        world.OnChangeCommitted += committedHandler;
        try
        {
            switch (teardown)
            {
                case GridTeardown.Remove:
                    Assert.True(world.TryRemoveGrid(original.GridIndex));
                    break;
                case GridTeardown.Reset:
                    world.Reset();
                    break;
                case GridTeardown.Deactivate:
                    world.Reset(deactivate: true);
                    break;
            }

            Assert.Equal(indices.Length, clearNotifications.Count);
            Assert.Equal(indices.Length, committedClearNotifications.Count);
            for (int i = 0; i < savedIndices.Length; i++)
            {
                WorldVoxelIndex savedIndex = savedIndices[i];
                ObstacleClearEventInfo cleared = Assert.Single(clearNotifications, info => info.VoxelIndex == savedIndex);
                GridEventInfo committed = Assert.Single(committedClearNotifications, info => info.VoxelIndex == savedIndex.VoxelIndex);
                Assert.Equal(2, cleared.ClearedObstacleCount);
                Assert.Equal(savedIndex.WorldSpawnToken, committed.WorldSpawnToken);
                Assert.Equal(savedIndex.GridSpawnToken, committed.GridSpawnToken);
                Assert.Equal(savedIndex.GridIndex, committed.GridIndex);
                Assert.Equal(cleared.ChangeStamp, committed.ChangeStamp);
                Assert.Equal(0, committed.ObstacleCount);
                Assert.Equal(0, voxels[i].ObstacleCount);
                Assert.False(voxels[i].IsAllocated);
            }
            Assert.Equal(0, original.ObstacleCount);
            Assert.False(original.IsActive);
            Assert.False(world.TryGetGridAndVoxel(staleIndex, out _, out _));
        }
        finally
        {
            GridObstacleManager.OnObstaclesCleared -= clearHandler;
            world.OnChangeCommitted -= committedHandler;
        }

        if (teardown == GridTeardown.Deactivate)
            return;

        VoxelGrid replacement = AddGrid(world, storageKind, Vector3d.Zero, out Voxel replacementVoxel);
        Assert.NotEqual(staleIndex.GridSpawnToken, replacementVoxel.WorldIndex.GridSpawnToken);
        Assert.False(world.TryGetGridAndVoxel(staleIndex, out _, out _));
        ObstacleToken token = world.AllocateObstacleToken();
        uint replacementVersion = replacement.Version;
        Assert.False(GridObstacleManager.TryAddObstacle(world, staleIndex, token));
        Assert.False(GridObstacleManager.TryRemoveObstacle(world, staleIndex, token));
        Assert.Equal(replacementVersion, replacement.Version);
        Assert.Equal(0, replacement.ObstacleCount);
        Assert.Equal(0, replacementVoxel.ObstacleCount);
        Assert.True(GridObstacleManager.TryAddObstacle(world, replacementVoxel.WorldIndex, token));
        Assert.True(GridObstacleManager.TryRemoveObstacle(world, replacementVoxel.WorldIndex, token));
    }

    [Fact]
    public void DirectAdd_ShouldRevalidateSparseVoxelAfterWaitingForCommitLock()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = AddGrid(world, GridStorageKind.Sparse, Vector3d.Zero, out Voxel target);
        ObstacleToken token = world.AllocateObstacleToken();
        uint version = grid.Version;
        int obstacleNotifications = 0;
        Action<GridEventInfo> handler = info =>
        {
            if (info.ChangeKind == GridEventKind.ObstacleAdded)
                obstacleNotifications++;
        };
        bool added = true;
        Exception failure = null;
        Thread worker = new Thread(() =>
        {
            try
            {
                added = grid.TryAddObstacle(target, token);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        bool waitingForLock = false;
        bool removed = false;
        world.OnChangeCommitted += handler;
        try
        {
            lock (world.ChangeSyncRoot)
            {
                worker.Start();
                waitingForLock = SpinWait.SpinUntil(
                    () => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5));
                if (waitingForLock)
                    removed = grid.TryRemoveVoxel(target.Index);
            }

            Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
            Assert.True(waitingForLock);
            Assert.True(removed);
            Assert.Null(failure);
            Assert.False(added);
            Assert.False(target.IsAllocated);
            Assert.False(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out _));
            Assert.Equal(0, target.ObstacleCount);
            Assert.Equal(0, grid.ObstacleCount);
            Assert.Equal(version + 1, grid.Version);
            Assert.Equal(0, obstacleNotifications);
        }
        finally
        {
            world.OnChangeCommitted -= handler;
        }
    }

    private static void Mutate(
        VoxelGrid grid,
        Voxel target,
        ObstacleMutation mutation,
        ObstacleToken originalToken,
        ObstacleToken additionalToken,
        bool expectedSuccess)
    {
        switch (mutation)
        {
            case ObstacleMutation.Add:
                Assert.Equal(expectedSuccess, grid.TryAddObstacle(target, additionalToken));
                break;
            case ObstacleMutation.Remove:
                Assert.Equal(expectedSuccess, grid.TryRemoveObstacle(target, originalToken));
                break;
            case ObstacleMutation.Clear:
                grid.ClearObstacles(target);
                break;
        }
    }

    private static VoxelGrid AddGrid(
        GridWorld world,
        GridStorageKind storageKind,
        Vector3d position,
        out Voxel voxel)
    {
        GridConfiguration configuration = new GridConfiguration(position, position, storageKind: storageKind);
        VoxelIndex index = new VoxelIndex(0, 0, 0);
        Assert.True(world.TryAddGrid(configuration, new[] { index }, out ushort gridIndex));
        Assert.True(world.TryGetGrid(gridIndex, out VoxelGrid grid));
        Assert.True(grid.TryGetVoxel(index, out voxel));
        return grid;
    }

    public enum ObstacleMutation
    {
        Add,
        Remove,
        Clear,
    }

    public enum GridTeardown
    {
        Remove,
        Reset,
        Deactivate,
    }

    public enum IdentityMismatch
    {
        World,
        GridSlot,
        GridGeneration,
        MissingCoordinate,
        ForeignPhysicalVoxel,
    }
}
