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
    public void Capture_ShouldReturnOnlyRequestedSparsePresenceAndObstacleStateAtCapturedChangeSequence()
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

        ulong capturedChangeSequence = world.ChangeSequence;
        Assert.True(world.TryCaptureNavigationBaseline(
            grid.Configuration.ToGridKey(),
            new[] { first, absent, last },
            out GridNavigationBaseline baseline));

        Assert.Equal(capturedChangeSequence, baseline.CapturedChangeSequence);
        Assert.Equal(capturedChangeSequence, baseline.GridLastChangeSequence);
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
    public void Capture_ShouldRejectAnInactiveWorldWithoutProducingABaseline()
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        world.Dispose();

        Assert.False(world.TryCaptureNavigationBaseline(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero).ToGridKey(),
            ReadOnlySpan<VoxelIndex>.Empty,
            out GridNavigationBaseline baseline));
        Assert.Null(baseline);
    }

    [Fact]
    public void ChangeStamp_ShouldProvideStableCommittedMutationIdentity()
    {
        var stamp = new GridChangeStamp(sequence: 17, causeId: 9);
        var same = new GridChangeStamp(sequence: 17, causeId: 9);
        var laterSequence = new GridChangeStamp(sequence: 18, causeId: 9);
        var differentCause = new GridChangeStamp(sequence: 17, causeId: 10);
        var committedChanges = new Dictionary<GridChangeStamp, string>
        {
            [stamp] = "sparse voxel added"
        };

        Assert.True(stamp.IsValid);
        Assert.False(new GridChangeStamp(sequence: 0, causeId: 9).IsValid);
        Assert.False(new GridChangeStamp(sequence: 17, causeId: 0).IsValid);
        Assert.True(committedChanges.TryGetValue(same, out string description));
        Assert.Equal("sparse voxel added", description);
        Assert.False(committedChanges.ContainsKey(laterSequence));
        Assert.False(committedChanges.ContainsKey(differentCause));
        Assert.True(stamp.Equals((object)same));
        Assert.False(stamp.Equals(null));
        Assert.False(stamp.Equals("17:9"));
        Assert.True(stamp == same);
        Assert.True(stamp != laterSequence);
        Assert.Equal("17:9", stamp.ToString());
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
        Assert.True(after.CapturedChangeSequence > baseline.CapturedChangeSequence);
        Assert.True(after.GridLastChangeSequence > baseline.GridLastChangeSequence);
    }

    [Fact]
    public void Capture_ShouldKeepGridLastChangeSequenceStableAcrossUnrelatedGridMutation()
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

        Assert.True(after.CapturedChangeSequence > before.CapturedChangeSequence);
        Assert.Equal(before.GridLastChangeSequence, after.GridLastChangeSequence);
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
        Exception snapshotError = null;
        Thread snapshotThread = new(() =>
        {
            try
            {
                world.ExecuteNavigationMaintenanceSnapshot(
                    () => Volatile.Write(ref snapshotEntered, 1));
            }
            catch (Exception exception)
            {
                snapshotError = exception;
            }
        }) { IsBackground = true };

        try
        {
            snapshotThread.Start();
            ThreadState observedState = default;
            Assert.True(SpinWait.SpinUntil(
                () =>
                {
                    observedState = snapshotThread.ThreadState;
                    return (observedState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) != 0;
                },
                TimeSpan.FromSeconds(5)));
            Assert.Equal(
                ThreadState.WaitSleepJoin,
                observedState & ThreadState.WaitSleepJoin);
            Assert.Equal(0, Volatile.Read(ref snapshotEntered));

            releaseCallback.Set();
            Assert.True(snapshotThread.Join(TimeSpan.FromSeconds(5)));
            await mutation.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.Null(snapshotError);
            Assert.Equal(1, Volatile.Read(ref snapshotEntered));
            Assert.Equal(1, Volatile.Read(ref reentrantMutationCompleted));
        }
        finally
        {
            releaseCallback.Set();
            if (snapshotThread.IsAlive)
                snapshotThread.Join(TimeSpan.FromSeconds(5));
            await mutation.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
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
        Assert.Equal(committed.ChangeSequence, exact.ChangeSequence);
        Assert.Equal(committed.CauseId, exact.CauseId);
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
        Assert.Equal(committed[0].ChangeSequence, exactClear.ChangeSequence);
        Assert.Equal(committed[0].CauseId, exactClear.CauseId);

        void HandleExactClear(ObstacleClearEventInfo eventInfo) => exactClear = eventInfo;
    }

    private static GridConfiguration CreateSparseConfiguration(Vector3d min, Vector3d max) =>
        new GridConfiguration(min, max, storageKind: GridStorageKind.Sparse);
}
