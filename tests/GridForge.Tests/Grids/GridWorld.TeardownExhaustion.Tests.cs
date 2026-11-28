using System;
using System.Collections.Generic;
using System.Threading;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridWorldTeardownExhaustionTests
{
    [Fact]
    public void ResetWithoutObserversAtExhaustionPreservesTheWorld()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        GridWorldTestFactory.SeedPublishedSequence(world, ulong.MaxValue);

        Assert.Throws<OverflowException>(() => world.Reset());
        Assert.Same(grid, Assert.Single(world.ActiveGrids));
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CheckBoundedVoxelSnapshot(ulong.MaxValue));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Teardown_ShouldRejectBeforeMutationWhenOnlyOneOfTwoStampsFits(bool remove)
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryAddObstacle(Vector3d.Zero, world.AllocateObstacleToken()));
        uint worldVersion = world.Version;
        uint gridVersion = grid.Version;
        ulong lastGridChange = grid.LastChangeSequence;
        int resetCalls = 0;
        List<GridEventInfo> changes = new();
        world.OnReset += () => resetCalls++;
        world.OnChangeCommitted += changes.Add;
        GridWorldTestFactory.SeedPublishedSequence(world, ulong.MaxValue - 1);

        Assert.Throws<OverflowException>(() =>
        {
            if (remove)
                world.TryRemoveGrid(grid.GridIndex);
            else
                world.Reset();
        });

        Assert.Equal(0, resetCalls);
        Assert.Empty(changes);
        Assert.True(world.TryGetGrid(Vector3d.Zero, out VoxelGrid retained));
        Assert.Same(grid, retained);
        Assert.Equal(1, grid.ObstacleCount);
        Assert.Equal(worldVersion, world.Version);
        Assert.Equal(gridVersion, grid.Version);
        Assert.Equal(lastGridChange, grid.LastChangeSequence);
        Assert.Equal(ulong.MaxValue - 1, world.ChangeSequence);
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CheckBoundedVoxelSnapshot(world.ChangeSequence));
        world.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Teardown_ShouldSpendOneStampPerBlockedVoxelEvenWhenTokensStack(bool remove)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryAddObstacle(Vector3d.Zero, world.AllocateObstacleToken()));
        Assert.True(grid.TryAddObstacle(Vector3d.Zero, world.AllocateObstacleToken()));
        List<GridEventInfo> changes = new();
        world.OnChangeCommitted += changes.Add;
        GridWorldTestFactory.SeedPublishedSequence(world, ulong.MaxValue - 2);

        if (remove)
            Assert.True(world.TryRemoveGrid(grid.GridIndex));
        else
            world.Reset();

        Assert.Collection(changes,
            first =>
            {
                Assert.Equal(remove ? GridEventKind.GridRemoved : GridEventKind.ObstaclesCleared, first.ChangeKind);
                Assert.Equal(ulong.MaxValue - 1, first.ChangeSequence);
            },
            last =>
            {
                Assert.Equal(remove ? GridEventKind.ObstaclesCleared : GridEventKind.WorldReset, last.ChangeKind);
                Assert.Equal(ulong.MaxValue, last.ChangeSequence);
            });
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CheckBoundedVoxelSnapshot(ulong.MaxValue));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalDeactivation_ShouldReleaseStateAndNotifyResetWithoutPublishingAPartialTeardown(bool dispose)
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryAddObstacle(Vector3d.Zero, world.AllocateObstacleToken()));
        int resetCalls = 0;
        int committedCalls = 0;
        world.OnReset += () => resetCalls++;
        world.OnChangeCommitted += _ => committedCalls++;
        GridWorldTestFactory.SeedPublishedSequence(world, ulong.MaxValue);

        if (dispose)
            world.Dispose();
        else
            world.Reset(deactivate: true);
        world.Dispose();

        Assert.Equal(1, resetCalls);
        Assert.Equal(0, committedCalls);
        Assert.False(world.IsActive);
        Assert.Equal(0, world.SpawnToken);
        Assert.False(grid.IsActive);
        Assert.Empty(world.ActiveGrids);
        Assert.Equal(ulong.MaxValue, world.ChangeSequence);
    }

    [Fact]
    public void AdmittedReset_ShouldKeepItsMarkerWhenAnObserverSpendsTheRemainingHeadroom()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid first = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid later = GridWorldTestFactory.AddGrid(world, Vector3d.One, Vector3d.One);
        Assert.True(first.TryGetVoxel(Vector3d.Zero, out Voxel firstVoxel));
        Assert.True(later.TryGetVoxel(Vector3d.One, out Voxel laterVoxel));
        Assert.True(later.TryAddObstacle(laterVoxel, world.AllocateObstacleToken()));
        bool clearedLater = true;
        Assert.True(firstVoxel.TryAddPartition(new OnRemovePartition(_ =>
        {
            later.ClearObstacles(laterVoxel);
            clearedLater = !laterVoxel.IsBlocked;
        })));
        GridWorldTestFactory.SeedPublishedSequence(world, ulong.MaxValue - 2);
        List<GridEventKind> events = new();
        world.OnChangeCommitted += change => events.Add(change.ChangeKind);
        bool added = false;
        Action addObstacle = () => added = first.TryAddObstacle(firstVoxel, world.AllocateObstacleToken());
        world.OnReset += addObstacle;

        world.Reset();

        world.OnReset -= addObstacle;
        Assert.True(added);
        Assert.False(clearedLater);
        Assert.Equal(new[] { GridEventKind.ObstacleAdded, GridEventKind.WorldReset }, events);
        Assert.Empty(world.ActiveGrids);
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CheckBoundedVoxelSnapshot(ulong.MaxValue));
    }

    [Fact]
    public void NestedOrdinaryReset_ShouldDeliverEveryPreTeardownNotification()
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        List<string> calls = new();
        bool resetAgain = true;
        world.OnReset += () =>
        {
            calls.Add($"A{world.ActiveGrids.Count}");
            if (resetAgain)
            {
                resetAgain = false;
                world.Reset();
            }
        };
        world.OnReset += () => calls.Add($"B{world.ActiveGrids.Count}");
        world.OnChangeCommitted += change => calls.Add(change.ChangeKind.ToString());
        world.Reset();

        Assert.Equal(new[] { "A1", "A1", "B1", "WorldReset", "B0", "WorldReset" }, calls);
        world.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetObserverCanDisposeWorldBeforeTheOriginalResetResumes(bool terminal)
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        List<string> calls = new();
        bool once = false;
        world.OnReset += () =>
        {
            calls.Add($"A{world.ActiveGrids.Count}");
            if (!once)
            {
                once = true;
                world.Dispose();
            }
        };
        world.OnReset += () => calls.Add($"B{world.ActiveGrids.Count}");
        world.OnChangeCommitted += change => calls.Add(change.ChangeKind.ToString());
        if (terminal)
            GridWorldTestFactory.SeedPublishedSequence(world, ulong.MaxValue - 1);

        world.Reset();

        Assert.Equal(terminal
            ? new[] { "A1", "A1", "B1", "B0" }
            : new[] { "A1", "A1", "B1", "WorldReset", "B0" }, calls);
        Assert.Empty(world.ActiveGrids);
        Assert.False(world.IsActive);
        Assert.Equal(0, world.SpawnToken);
        world.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveDisposeDuringDeactivationReleasesLockAfterPreTeardownObserversReturn(bool dispose)
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        GridConfiguration another = new(Vector3d.One, Vector3d.One);
        int calls = 0;
        bool added = false;
        int gridsSeen = 0;
        world.OnReset += () =>
        {
            calls++;
            world.Reset(deactivate: true);
            world.Dispose();
            added = world.TryAddGrid(another, out _);
        };
        world.OnReset += () => gridsSeen = world.ActiveGrids.Count;

        if (dispose)
            world.Dispose();
        else
            world.Reset(deactivate: true);

        Assert.Equal(1, calls);
        Assert.True(added);
        Assert.Equal(2, gridsSeen);
        Assert.False(world.IsActive);
        Assert.Empty(world.ActiveGrids);
        Assert.Throws<ObjectDisposedException>(world.EnterReadLock);
        Assert.False(world.TryAddGrid(another, out _));
        world.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachedObstacleClearKeepsItsReservedStampAcrossPartitionCallbacks(bool dispose)
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid first = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid live = GridWorldTestFactory.AddGrid(world, Vector3d.One, Vector3d.One);
        Assert.True(first.TryGetVoxel(Vector3d.Zero, out Voxel firstVoxel));
        ObstacleToken token = world.AllocateObstacleToken();
        Assert.True(first.TryAddObstacle(firstVoxel, token));
        WorldVoxelIndex identity = firstVoxel.WorldIndex;
        bool detachedAdd = true;
        bool detachedRemove = true;
        bool liveRejected = false;
        Assert.True(firstVoxel.TryAddPartition(new OnRemovePartition(_ =>
        {
            detachedAdd = first.TryAddObstacle(firstVoxel, world.AllocateObstacleToken());
            detachedRemove = first.TryRemoveObstacle(firstVoxel, token);
            try
            {
                live.TryAddObstacle(Vector3d.One, token);
            }
            catch (OverflowException)
            {
                liveRejected = true;
            }
            if (dispose)
                world.Dispose();
        })));
        GridWorldTestFactory.SeedPublishedSequence(world, ulong.MaxValue - 2);
        List<ObstacleClearEventInfo> clears = new();
        GridObstacleManager.OnObstaclesCleared += clears.Add;
        try
        {
            Assert.True(world.TryRemoveGrid(first.GridIndex));
        }
        finally
        {
            GridObstacleManager.OnObstaclesCleared -= clears.Add;
        }

        Assert.False(detachedAdd);
        Assert.False(detachedRemove);
        Assert.True(liveRejected);
        ObstacleClearEventInfo clear = Assert.Single(clears);
        Assert.Equal(identity, clear.VoxelIndex);
        Assert.Equal(ulong.MaxValue, clear.ChangeSequence);
        Assert.Equal(!dispose, world.IsActive);
        world.Dispose();
    }

    [Fact]
    public void FailedDisposeInPartitionTeardownAllowsIndependentSubsequentDeactivation()
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Exception failure = null;
        Assert.True(voxel.TryAddPartition(new OnRemovePartition(_ =>
            failure = Record.Exception(world.Dispose))));

        world.Reset();

        // This partition runs while ordinary reset owns the world write lock.
        Assert.IsType<LockRecursionException>(failure);
        Assert.True(world.IsActive);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        world.Reset(deactivate: true);
        Assert.False(world.IsActive);
        world.EnterReadLock();
        world.ExitReadLock();
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(world.EnterReadLock);
    }

    private sealed class OnRemovePartition(Action<Voxel> removed) : IVoxelPartition
    {
        public WorldVoxelIndex WorldIndex { get; private set; }
        public void SetParentIndex(WorldVoxelIndex worldIndex) => WorldIndex = worldIndex;
        public void OnAddToVoxel(Voxel voxel) { }
        public void OnRemoveFromVoxel(Voxel voxel) => removed(voxel);
    }
}
