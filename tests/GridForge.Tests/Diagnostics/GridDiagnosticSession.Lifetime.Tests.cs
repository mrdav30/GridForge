using System;
using System.Runtime.CompilerServices;
using System.Threading;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Tests;
using GridForge.Spatial;
using SwiftCollections;
using Xunit;

namespace GridForge.Diagnostics.Tests;

[Collection("GridForgeCollection")]
public sealed class GridDiagnosticSessionLifetimeTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    public void SessionReachabilityFollowsItsWorld(bool deactivate, bool keepWorld, bool disposeSession)
    {
        using GridWorld held = keepWorld ? new GridWorld() : null;
        (WeakReference world, WeakReference session) = Observe(held, deactivate, disposeSession);
        for (int round = 0; round < 3; round++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        try
        {
            Assert.Equal(keepWorld, world.IsAlive);
            Assert.Equal(keepWorld && !deactivate && !disposeSession, session.IsAlive);
        }
        finally
        {
            (session.Target as GridDiagnosticSession)?.Dispose();
            GC.KeepAlive(held);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, WeakReference) Observe(GridWorld held, bool deactivate, bool disposeSession)
    {
        GridWorld world = held ?? new GridWorld();
        GridDiagnosticSession session = new GridDiagnosticSession(world);
        if (deactivate)
            world.Reset(deactivate: true);
        if (disposeSession)
            session.Dispose();
        return (new WeakReference(world), new WeakReference(session));
    }

    [Fact]
    public void SessionWaitingToSubscribeCannotAttachAfterDeactivation()
    {
        using GridWorld world = new GridWorld();
        Exception failure = null;
        GridDiagnosticSession session = null;
        Thread observer = new Thread(() =>
        {
            try { session = new GridDiagnosticSession(world); }
            catch (Exception ex) { failure = ex; }
        });
        bool waiting;
        lock (world.ChangeSyncRoot)
        {
            observer.Start();
            waiting = SpinWait.SpinUntil(() => (observer.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5));
            world.Reset(deactivate: true);
        }
        Assert.True(observer.Join(TimeSpan.FromSeconds(5)));
        using (session)
        {
            Assert.True(waiting);
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Null(session);
        }
    }

    [Fact]
    public void ExactCellIsVisibleBetweenGlobalAndVoxelCallbacksBeforeTheBroadChange()
    {
        using GridWorld world = new GridWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        using GridDiagnosticSession session = new GridDiagnosticSession(world);
        SwiftList<GridDiagnosticChange> global = new SwiftList<GridDiagnosticChange>();
        SwiftList<GridDiagnosticChange> local = new SwiftList<GridDiagnosticChange>();
        SwiftList<GridDiagnosticChange> after = new SwiftList<GridDiagnosticChange>();
        int globalCount = -1;
        void DuringGlobal(ObstacleEventInfo _) => globalCount = session.GetDirtyChangesInto(global);
        void DuringLocal(ObstacleEventInfo _)
        {
            session.GetDirtyChangesInto(local);
            session.ClearDirtyChanges();
        }
        GridObstacleManager.OnObstacleAdded += DuringGlobal;
        voxel.OnObstacleAdded += DuringLocal;
        try
        {
            Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken()));
        }
        finally
        {
            voxel.OnObstacleAdded -= DuringLocal;
            GridObstacleManager.OnObstacleAdded -= DuringGlobal;
        }

        Assert.Equal(0, globalCount);
        GridDiagnosticChange cell = Assert.Single(local);
        Assert.Equal(GridDiagnosticChangeKind.ObstacleChanged, cell.Kind);
        Assert.Equal(voxel.WorldIndex, cell.WorldIndex);
        session.GetDirtyChangesInto(after);
        Assert.Equal(GridDiagnosticChangeKind.GridChanged, Assert.Single(after).Kind);
    }

    [Fact]
    public void RemovalFromVoxelCallbackRetainsTheObservedCellAndRemovedGrid()
    {
        using GridWorld world = new GridWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        using GridDiagnosticSession session = new GridDiagnosticSession(world);
        WorldVoxelIndex address = voxel.WorldIndex;
        bool removed = false;
        voxel.OnObstacleAdded += _ => removed = world.TryRemoveGrid(address.GridIndex);
        Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken()));
        Assert.True(removed);
        SwiftList<GridDiagnosticChange> changes = new SwiftList<GridDiagnosticChange>();
        Assert.Equal(2, session.GetDirtyChangesInto(changes));
        Assert.Contains(changes, change => change.Kind == GridDiagnosticChangeKind.ObstacleChanged && change.WorldIndex == address);
        Assert.Contains(changes, change => change.Kind == GridDiagnosticChangeKind.GridRemoved && change.GridSpawnToken == address.GridSpawnToken);
    }

    [Fact]
    public void ExactOccupantDeliveryIsWorldLocalAndIsolatesEarlierSubscriberFailure()
    {
        using GridWorld world = new GridWorld();
        using GridWorld other = new GridWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid otherGrid = GridWorldTestFactory.AddGrid(other, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Assert.True(otherGrid.TryGetVoxel(Vector3d.Zero, out Voxel otherVoxel));
        using DiagnosticCaptureScope log = new DiagnosticCaptureScope();
        GridDiagnosticSession disposed = null;
        int callbacks = 0;
        world.DiagnosticCellChanged += (_, _) =>
        {
            callbacks++;
            disposed.Dispose();
            throw new InvalidOperationException("exact subscriber failed");
        };
        using (disposed = new GridDiagnosticSession(world))
        using (GridDiagnosticSession survivor = new GridDiagnosticSession(world))
        {
            Assert.True(otherGrid.TryAddVoxelOccupant(otherVoxel, new TestOccupant(Vector3d.Zero)));
            Assert.Equal(0, callbacks);
            Assert.True(grid.TryAddVoxelOccupant(voxel, new TestOccupant(Vector3d.Zero)));
            Assert.Equal(1, callbacks);
            SwiftList<GridDiagnosticChange> changes = new SwiftList<GridDiagnosticChange>();
            Assert.Equal(0, disposed.GetDirtyChangesInto(changes));
            Assert.Equal(1, survivor.GetDirtyChangesInto(changes));
            Assert.Equal(GridDiagnosticChangeKind.OccupantChanged, changes[0].Kind);
            Assert.Equal(voxel.WorldIndex, changes[0].WorldIndex);
            Assert.Contains(log.Messages, entry => entry.Message.Contains("exact subscriber failed"));
        }
    }

    [Fact]
    public void RejectedResetPreservesDirtAndTerminalDisposalLeavesTheOldSessionItsMarker()
    {
        using GridWorld world = new GridWorld();
        using GridDiagnosticSession session = new GridDiagnosticSession(world);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        GridWorldTestFactory.SeedPublishedSequence(world, ulong.MaxValue);
        Assert.Throws<OverflowException>(() => world.Reset());
        SwiftList<GridDiagnosticChange> changes = new SwiftList<GridDiagnosticChange>();
        session.GetDirtyChangesInto(changes);
        Assert.Equal(grid.SpawnToken, Assert.Single(changes).GridSpawnToken);
        Assert.Equal(GridDiagnosticChangeKind.GridAdded, changes[0].Kind);

        world.Dispose();
        using GridWorld replacement = new GridWorld();
        using GridDiagnosticSession next = new GridDiagnosticSession(replacement);
        GridWorldTestFactory.AddGrid(replacement, Vector3d.Zero, Vector3d.Zero);
        session.GetDirtyChangesInto(changes);
        Assert.Equal(GridDiagnosticChangeKind.WorldReset, Assert.Single(changes).Kind);
        next.GetDirtyChangesInto(changes);
        Assert.Equal(replacement.SpawnToken, Assert.Single(changes).WorldSpawnToken);
        Assert.Equal(GridDiagnosticChangeKind.GridAdded, changes[0].Kind);
    }

    [Fact]
    public void CellsResumeAfterResetAcknowledgementAndGridLifecycleRemainsVisible()
    {
        using GridWorld world = new GridWorld();
        using GridDiagnosticSession session = new GridDiagnosticSession(world);
        world.Reset();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        ObstacleToken obstacle = world.AllocateObstacleToken();
        Assert.True(grid.TryAddObstacle(voxel, obstacle));
        Assert.True(grid.TryRemoveObstacle(voxel, obstacle));
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);
        Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
        SwiftList<GridDiagnosticChange> changes = new SwiftList<GridDiagnosticChange>();
        Assert.Equal(2, session.GetDirtyChangesInto(changes));
        Assert.Contains(changes, change => change.Kind == GridDiagnosticChangeKind.GridAdded);
        Assert.Contains(changes, change => change.Kind == GridDiagnosticChangeKind.WorldReset);
        session.ClearDirtyChanges();

        ulong sequence = world.ChangeSequence;
        Assert.True(grid.TryRemoveVoxelOccupant(voxel, occupant));
        Assert.Equal(sequence, world.ChangeSequence);
        Assert.Equal(1, session.GetDirtyChangesInto(changes));
        Assert.Equal(GridDiagnosticChangeKind.OccupantChanged, changes[0].Kind);
        Assert.Equal(voxel.WorldIndex, changes[0].WorldIndex);
    }
}
