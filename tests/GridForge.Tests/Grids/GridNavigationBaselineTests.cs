using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridNavigationBaselineTests
{
    [Fact]
    public void Capture_ShouldReturnOnlyRequestedSparsePresenceAndObstacleStateAtHighWater()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = CreateSparseConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0));
        VoxelIndex first = new VoxelIndex(0, 0, 0);
        VoxelIndex absent = new VoxelIndex(1, 0, 0);
        VoxelIndex last = new VoxelIndex(2, 0, 0);

        Assert.True(world.TryAddGrid(configuration, new[] { first, last }, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(last, out Voxel blockedVoxel));
        Assert.True(grid.TryAddObstacle(blockedVoxel, world.AllocateObstacleToken()));

        ulong highWater = world.ChangeSequence;
        Assert.True(world.TryCaptureNavigationBaseline(
            grid.Configuration.ToGridKey(),
            new[] { first, absent, last },
            out GridNavigationBaseline baseline));

        Assert.Equal(highWater, baseline.HighWaterSequence);
        Assert.Equal(highWater, baseline.GridHighWaterSequence);
        Assert.Equal(world.SpawnToken, baseline.WorldSpawnToken);
        Assert.Equal(grid.SpawnToken, baseline.GridSpawnToken);
        Assert.Equal(grid.GridIndex, baseline.GridIndex);
        Assert.Equal(grid.Configuration.ToGridKey(), baseline.ConfigurationKey);

        ReadOnlySpan<NavigationBaselineVoxelState> states = baseline.VoxelStates;
        Assert.Equal(3, states.Length);
        Assert.Equal(first, states[0].VoxelIndex);
        Assert.True(states[0].IsPresent);
        Assert.Equal(0, states[0].ObstacleCount);
        Assert.Equal(absent, states[1].VoxelIndex);
        Assert.False(states[1].IsPresent);
        Assert.Equal(0, states[1].ObstacleCount);
        Assert.Equal(last, states[2].VoxelIndex);
        Assert.True(states[2].IsPresent);
        Assert.Equal(1, states[2].ObstacleCount);
    }

    [Fact]
    public void Capture_ShouldRejectUnsortedDuplicateOutOfBoundsAndWrongConfigurationRequests()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = CreateSparseConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0));
        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        GridConfigurationKey key = grid.Configuration.ToGridKey();

        Assert.False(world.TryCaptureNavigationBaseline(
            key,
            new[] { new VoxelIndex(1, 0, 0), new VoxelIndex(0, 0, 0) },
            out _));
        Assert.False(world.TryCaptureNavigationBaseline(
            key,
            new[] { new VoxelIndex(1, 0, 0), new VoxelIndex(1, 0, 0) },
            out _));
        Assert.False(world.TryCaptureNavigationBaseline(
            key,
            new[] { new VoxelIndex(3, 0, 0) },
            out _));
        Assert.False(world.TryCaptureNavigationBaseline(
            new GridConfiguration(new Vector3d(20, 0, 0), new Vector3d(20, 0, 0)).ToGridKey(),
            Array.Empty<VoxelIndex>(),
            out _));

        Assert.True(world.TryCaptureNavigationBaseline(
            key,
            new[] { default(VoxelIndex) },
            out GridNavigationBaseline originBaseline));
        Assert.Equal(new VoxelIndex(0, 0, 0), originBaseline.VoxelStates[0].VoxelIndex);
    }

    [Fact]
    public void SubscribeAndCapture_ShouldHaveNoMutationGapOrDoubleApplication()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = CreateSparseConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0));
        VoxelIndex address = new VoxelIndex(1, 0, 0);
        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        List<GridEventInfo> events = new List<GridEventInfo>();

        Assert.True(world.TrySubscribeNavigationChanges(
            grid.Configuration.ToGridKey(),
            new[] { address },
            events.Add,
            out GridNavigationChangeSubscription subscription));

        using (subscription)
        {
            Assert.False(subscription.Baseline.VoxelStates[0].IsPresent);
            Assert.True(grid.TryAddVoxel(address, out _));
            Assert.True(grid.TryRemoveVoxel(address));

            Assert.Equal(2, events.Count);
            Assert.All(events, eventInfo =>
                Assert.True(eventInfo.ChangeSequence > subscription.Baseline.HighWaterSequence));
            Assert.Equal(subscription.Baseline.HighWaterSequence + 1, events[0].ChangeSequence);
            Assert.Equal(events[0].ChangeSequence + 1, events[1].ChangeSequence);
            Assert.Equal(GridEventKind.SparseVoxelAdded, events[0].ChangeKind);
            Assert.True(events[0].HasVoxelState);
            Assert.True(events[0].IsVoxelPresent);
            Assert.Equal(GridEventKind.SparseVoxelRemoved, events[1].ChangeKind);
            Assert.True(events[1].HasVoxelState);
            Assert.False(events[1].IsVoxelPresent);
        }

        Assert.True(grid.TryAddVoxel(address, out _));
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task MaintenanceSnapshot_ShouldFreezeMutationsAcrossPrefixDetachAndBaselineCapture()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelIndex address = new VoxelIndex(0, 0, 0);
        Assert.True(grid.TryGetVoxel(address, out Voxel voxel));
        int mutationStarted = 0;
        GridNavigationBaseline baseline = null;
        Task mutation = null;

        world.ExecuteNavigationMaintenanceSnapshot(() =>
        {
            mutation = Task.Run(() =>
            {
                Volatile.Write(ref mutationStarted, 1);
                Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken()));
            });

            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref mutationStarted) != 0,
                TimeSpan.FromSeconds(5)));
            Assert.False(mutation.IsCompleted);
            Assert.True(world.TryCaptureNavigationBaseline(
                grid.Configuration.ToGridKey(),
                new[] { address },
                out baseline));
            Assert.Equal(0, baseline.VoxelStates[0].ObstacleCount);
        });

        await mutation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(world.TryCaptureNavigationBaseline(
            grid.Configuration.ToGridKey(),
            new[] { address },
            out GridNavigationBaseline after));
        Assert.Equal(1, after.VoxelStates[0].ObstacleCount);
        Assert.True(after.HighWaterSequence > baseline.HighWaterSequence);
        Assert.True(after.GridHighWaterSequence > baseline.GridHighWaterSequence);
    }

    [Fact]
    public void Capture_ShouldKeepGridHighWaterStableAcrossUnrelatedGridMutation()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid observed = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid unrelated = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(2, 0, 0),
            new Vector3d(2, 0, 0));
        VoxelIndex address = default;
        Assert.True(world.TryCaptureNavigationBaseline(
            observed.Configuration.ToGridKey(),
            new[] { address },
            out GridNavigationBaseline before));
        Assert.True(unrelated.TryGetVoxel(address, out Voxel voxel));
        Assert.True(unrelated.TryAddObstacle(voxel, world.AllocateObstacleToken()));

        Assert.True(world.TryCaptureNavigationBaseline(
            observed.Configuration.ToGridKey(),
            new[] { address },
            out GridNavigationBaseline after));

        Assert.True(after.HighWaterSequence > before.HighWaterSequence);
        Assert.Equal(before.GridHighWaterSequence, after.GridHighWaterSequence);
    }

    [Fact]
    public async Task MaintenanceSnapshot_ShouldWaitForCommittedCallbackPrefix()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
        GridConfiguration reentrantConfiguration = new(
            new Vector3d(2, 0, 0),
            new Vector3d(2, 0, 0));
        using ManualResetEventSlim callbackEntered = new ManualResetEventSlim();
        using ManualResetEventSlim releaseCallback = new ManualResetEventSlim();
        int snapshotEntered = 0;
        int reentrantMutationCompleted = 0;
        world.OnChangeCommitted += eventInfo =>
        {
            if (eventInfo.ChangeKind != GridEventKind.ObstacleAdded)
                return;

            callbackEntered.Set();
            releaseCallback.Wait(TestContext.Current.CancellationToken);
            Assert.True(world.TryAddGrid(reentrantConfiguration, out _));
            Volatile.Write(ref reentrantMutationCompleted, 1);
        };

        Task mutation = Task.Run(
            () => Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken())),
            TestContext.Current.CancellationToken);
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Task snapshot = Task.Run(
            () => world.ExecuteNavigationMaintenanceSnapshot(
                () => Volatile.Write(ref snapshotEntered, 1)),
            TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref snapshotEntered));

        releaseCallback.Set();
        await Task.WhenAll(mutation, snapshot)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref snapshotEntered));
        Assert.Equal(1, Volatile.Read(ref reentrantMutationCompleted));
    }

    [Fact]
    public void MaintenanceSnapshot_ShouldRejectReentrantCommittedCallback()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
        Exception reentrantFailure = null;
        world.OnChangeCommitted += eventInfo =>
        {
            if (eventInfo.ChangeKind != GridEventKind.ObstacleAdded)
                return;
            reentrantFailure = Record.Exception(
                () => world.ExecuteNavigationMaintenanceSnapshot(() => { }));
        };

        Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken()));

        Assert.IsType<InvalidOperationException>(reentrantFailure);
    }

    [Fact]
    public void ObstacleMutation_ShouldPairExactAndCommittedEventsWithOneCause()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
        GridEventInfo committed = default;
        ObstacleEventInfo exact = default;

        void HandleCommitted(GridEventInfo eventInfo) => committed = eventInfo;
        void HandleExact(ObstacleEventInfo eventInfo) => exact = eventInfo;

        world.OnChangeCommitted += HandleCommitted;
        GridObstacleManager.OnObstacleAdded += HandleExact;
        try
        {
            Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken()));
        }
        finally
        {
            world.OnChangeCommitted -= HandleCommitted;
            GridObstacleManager.OnObstacleAdded -= HandleExact;
        }

        Assert.Equal(GridEventKind.ObstacleAdded, committed.ChangeKind);
        Assert.True(committed.ChangeStamp.IsValid);
        Assert.Equal(committed.ChangeStamp, exact.ChangeStamp);
        Assert.Equal(voxel.Index, committed.VoxelIndex);
        Assert.True(committed.HasVoxelState);
        Assert.True(committed.IsVoxelPresent);
        Assert.Equal(1, committed.ObstacleCount);
        Assert.Equal(1, exact.ObstacleCount);
    }

    [Fact]
    public void ReentrantMutation_ShouldPublishCommittedEventsInSequenceOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
        ObstacleToken first = world.AllocateObstacleToken();
        ObstacleToken second = world.AllocateObstacleToken();
        List<ulong> sequences = new List<ulong>();
        bool reentered = false;

        world.OnChangeCommitted += eventInfo =>
        {
            sequences.Add(eventInfo.ChangeSequence);
            if (!reentered)
            {
                reentered = true;
                Assert.True(grid.TryAddObstacle(voxel, second));
            }
        };

        Assert.True(grid.TryAddObstacle(voxel, first));

        Assert.Equal(2, sequences.Count);
        Assert.Equal(sequences[0] + 1, sequences[1]);
        Assert.Equal(2, voxel.ObstacleCount);
    }

    [Fact]
    public void ResetWithBlockedVoxel_ShouldPublishClearBeforeResetWithoutLockRecursion()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
        Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken()));

        List<GridEventInfo> committed = new List<GridEventInfo>();
        ObstacleClearEventInfo exactClear = default;
        world.OnChangeCommitted += committed.Add;
        GridObstacleManager.OnObstaclesCleared += HandleExactClear;
        try
        {
            world.Reset();
        }
        finally
        {
            GridObstacleManager.OnObstaclesCleared -= HandleExactClear;
        }

        Assert.Equal(2, committed.Count);
        Assert.Equal(GridEventKind.ObstaclesCleared, committed[0].ChangeKind);
        Assert.Equal(GridEventKind.WorldReset, committed[1].ChangeKind);
        Assert.Equal(committed[0].ChangeSequence + 1, committed[1].ChangeSequence);
        Assert.Equal(committed[0].ChangeStamp, exactClear.ChangeStamp);

        void HandleExactClear(ObstacleClearEventInfo eventInfo) => exactClear = eventInfo;
    }

    private static GridConfiguration CreateSparseConfiguration(Vector3d min, Vector3d max) =>
        new GridConfiguration(min, max, storageKind: GridStorageKind.Sparse);
}
