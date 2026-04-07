using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using System;
using System.Linq;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class ManagerCoverageTests : IDisposable
{
    public ManagerCoverageTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        GlobalGridManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ObstacleManager_ShouldSupportPositionAndGlobalIndexOverloads()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(1, 0, 1);
        BoundsKey firstToken = new BoundsKey(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        BoundsKey secondToken = new BoundsKey(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));
        Assert.True(grid.TryAddObstacle(position, firstToken));
        Assert.True(GridObstacleManager.TryAddObstacle(voxel.GlobalIndex, secondToken));
        Assert.Equal(2, voxel.ObstacleCount);

        Assert.True(grid.TryRemoveObstacle(position, firstToken));
        Assert.True(GridObstacleManager.TryRemoveObstacle(voxel.GlobalIndex, secondToken));
        Assert.False(voxel.IsBlocked);
        Assert.False(grid.TryRemoveObstacle(position, firstToken));
    }

    [Fact]
    public void OccupantManagerAndScanManager_ShouldSupportWrapperOverloads()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant occupant = new TestOccupant(new Vector3d(2, 0, 2), 7);

        Assert.True(GridOccupantManager.TryRegister(occupant));
        Assert.True(grid.TryGetVoxel(occupant.Position, out Voxel voxel));
        Assert.True(occupant.OccupyingIndexMap.TryGetValue(voxel.GlobalIndex, out int ticket));
        Assert.True(GridScanManager.TryGetVoxelOccupant(voxel.GlobalIndex, ticket, out IVoxelOccupant registeredOccupant));
        Assert.Same(occupant, registeredOccupant);
        Assert.Single(GridScanManager.GetOccupants(voxel.GlobalIndex));
        Assert.Single(GridScanManager.GetVoxelOccupantsByType<TestOccupant>(voxel.GlobalIndex));
        Assert.Single(grid.GetVoxelOccupantsByType<TestOccupant>(voxel.Index));
        Assert.Single(grid.GetConditionalOccupants(voxel.Index, groupCondition: groupId => groupId == 7));
        Assert.True(grid.TryGetScanCell(voxel.Index, out ScanCell scanCell));
        Assert.True(scanCell.IsOccupied);

        TestOccupant secondOccupant = new TestOccupant(occupant.Position, 8);

        Assert.True(grid.TryAddVoxelOccupant(voxel.Index, secondOccupant));
        Assert.True(GridOccupantManager.TryRemoveVoxelOccupant(voxel.GlobalIndex, occupant));
        Assert.True(grid.TryRemoveVoxelOccupant(voxel.Index, secondOccupant));
        Assert.False(scanCell.IsOccupied);
    }

    [Fact]
    public void ScanManager_ShouldSupportOccupantConditionWithoutGroupFilter()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(2, 0, 2);
        TestOccupant targetOccupant = new TestOccupant(position, 1);
        TestOccupant otherOccupant = new TestOccupant(position, 2);

        Assert.True(grid.TryAddVoxelOccupant(targetOccupant));
        Assert.True(grid.TryAddVoxelOccupant(otherOccupant));
        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));

        IVoxelOccupant[] byGlobalIndex = GridScanManager.GetConditionalOccupants(
            voxel.GlobalIndex,
            occupantCondition: occupant => ReferenceEquals(occupant, targetOccupant))
            .ToArray();
        IVoxelOccupant[] byVoxelIndex = grid.GetConditionalOccupants(
            voxel.Index,
            occupantCondition: occupant => ReferenceEquals(occupant, targetOccupant))
            .ToArray();
        IVoxelOccupant[] byVoxel = grid.GetConditionalOccupants(
            voxel,
            occupantCondition: occupant => ReferenceEquals(occupant, targetOccupant))
            .ToArray();

        Assert.Single(byGlobalIndex);
        Assert.Single(byVoxelIndex);
        Assert.Single(byVoxel);
        Assert.Same(targetOccupant, byGlobalIndex[0]);
        Assert.Same(targetOccupant, byVoxelIndex[0]);
        Assert.Same(targetOccupant, byVoxel[0]);
    }

    [Fact]
    public void ScanManager_ShouldSupportGroupConditionWithoutOccupantFilter()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(2, 0, 2);
        TestOccupant firstOccupant = new TestOccupant(position, 1);
        TestOccupant secondOccupant = new TestOccupant(position, 2);

        Assert.True(grid.TryAddVoxelOccupant(firstOccupant));
        Assert.True(grid.TryAddVoxelOccupant(secondOccupant));
        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));

        IVoxelOccupant[] groupMatches = GridScanManager.GetConditionalOccupants(
            voxel.GlobalIndex,
            groupCondition: groupId => groupId == 2)
            .ToArray();

        Assert.Single(groupMatches);
        Assert.Same(secondOccupant, groupMatches[0]);
    }

    [Fact]
    public void ScanManager_TryGetVoxelOccupant_ShouldReturnFalseForInvalidIndices()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        GlobalVoxelIndex missingGridIndex = new GlobalVoxelIndex(ushort.MaxValue, new VoxelIndex(0, 0, 0), 0);

        Assert.False(GridScanManager.TryGetVoxelOccupant(missingGridIndex, 0, out IVoxelOccupant missingGlobalOccupant));
        Assert.Null(missingGlobalOccupant);

        Assert.False(grid.TryGetVoxelOccupant(new VoxelIndex(-1, 0, 0), 0, out IVoxelOccupant missingLocalOccupant));
        Assert.Null(missingLocalOccupant);
    }

    [Fact]
    public void ManagerNotifications_ShouldSwallowSubscriberExceptions()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(1, 0, 1);
        BoundsKey obstacleToken = new BoundsKey(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        BoundsKey secondObstacleToken = new BoundsKey(new Vector3d(2, 0, 2), new Vector3d(3, 0, 3));
        TestOccupant occupant = new TestOccupant(position);
        int obstacleManagerAddedNotifications = 0;
        int obstacleManagerRemovedNotifications = 0;
        int obstacleManagerClearedNotifications = 0;
        int obstacleVoxelAddedNotifications = 0;
        int obstacleVoxelRemovedNotifications = 0;
        int obstacleVoxelClearedNotifications = 0;
        int occupantManagerAddedNotifications = 0;
        int occupantManagerRemovedNotifications = 0;
        int occupantVoxelAddedNotifications = 0;
        int occupantVoxelRemovedNotifications = 0;
        Action<ObstacleEventInfo> throwingObstacleAddedHandler = (_) => throw new InvalidOperationException("obstacle add event");
        Action<ObstacleEventInfo> recordingObstacleAddedHandler = (_) => obstacleManagerAddedNotifications++;
        Action<ObstacleEventInfo> throwingObstacleRemovedHandler = (_) => throw new InvalidOperationException("obstacle remove event");
        Action<ObstacleEventInfo> recordingObstacleRemovedHandler = (_) => obstacleManagerRemovedNotifications++;
        Action<ObstacleClearEventInfo> throwingObstacleClearHandler = (_) => throw new InvalidOperationException("obstacle clear event");
        Action<ObstacleClearEventInfo> recordingObstacleClearHandler = (_) => obstacleManagerClearedNotifications++;
        Action<OccupantEventInfo> throwingOccupantAddedHandler = (_) => throw new InvalidOperationException("occupant add event");
        Action<OccupantEventInfo> recordingOccupantAddedHandler = (_) => occupantManagerAddedNotifications++;
        Action<OccupantEventInfo> throwingOccupantRemovedHandler = (_) => throw new InvalidOperationException("occupant remove event");
        Action<OccupantEventInfo> recordingOccupantRemovedHandler = (_) => occupantManagerRemovedNotifications++;
        Action<ObstacleEventInfo> throwingVoxelObstacleAddedHandler = (_) => throw new InvalidOperationException("voxel obstacle add event");
        Action<ObstacleEventInfo> recordingVoxelObstacleAddedHandler = (_) => obstacleVoxelAddedNotifications++;
        Action<ObstacleEventInfo> throwingVoxelObstacleRemovedHandler = (_) => throw new InvalidOperationException("voxel obstacle remove event");
        Action<ObstacleEventInfo> recordingVoxelObstacleRemovedHandler = (_) => obstacleVoxelRemovedNotifications++;
        Action<ObstacleClearEventInfo> throwingVoxelObstacleClearHandler = (_) => throw new InvalidOperationException("voxel obstacle clear event");
        Action<ObstacleClearEventInfo> recordingVoxelObstacleClearHandler = (_) => obstacleVoxelClearedNotifications++;
        Action<OccupantEventInfo> throwingVoxelOccupantAddedHandler = (_) => throw new InvalidOperationException("voxel occupant add event");
        Action<OccupantEventInfo> recordingVoxelOccupantAddedHandler = (_) => occupantVoxelAddedNotifications++;
        Action<OccupantEventInfo> throwingVoxelOccupantRemovedHandler = (_) => throw new InvalidOperationException("voxel occupant remove event");
        Action<OccupantEventInfo> recordingVoxelOccupantRemovedHandler = (_) => occupantVoxelRemovedNotifications++;

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));

        try
        {
            GridObstacleManager.OnObstacleAdded += throwingObstacleAddedHandler;
            GridObstacleManager.OnObstacleAdded += recordingObstacleAddedHandler;
            GridObstacleManager.OnObstacleRemoved += throwingObstacleRemovedHandler;
            GridObstacleManager.OnObstacleRemoved += recordingObstacleRemovedHandler;
            GridObstacleManager.OnObstaclesCleared += throwingObstacleClearHandler;
            GridObstacleManager.OnObstaclesCleared += recordingObstacleClearHandler;
            voxel.OnObstacleAdded += throwingVoxelObstacleAddedHandler;
            voxel.OnObstacleAdded += recordingVoxelObstacleAddedHandler;
            voxel.OnObstacleRemoved += throwingVoxelObstacleRemovedHandler;
            voxel.OnObstacleRemoved += recordingVoxelObstacleRemovedHandler;
            voxel.OnObstaclesCleared += throwingVoxelObstacleClearHandler;
            voxel.OnObstaclesCleared += recordingVoxelObstacleClearHandler;

            Assert.True(grid.TryAddObstacle(voxel, obstacleToken));
            grid.ClearObstacles(voxel);
            Assert.True(grid.TryAddObstacle(voxel, secondObstacleToken));
            Assert.True(grid.TryRemoveObstacle(voxel, secondObstacleToken));

            GridOccupantManager.OnOccupantAdded += throwingOccupantAddedHandler;
            GridOccupantManager.OnOccupantAdded += recordingOccupantAddedHandler;
            GridOccupantManager.OnOccupantRemoved += throwingOccupantRemovedHandler;
            GridOccupantManager.OnOccupantRemoved += recordingOccupantRemovedHandler;
            voxel.OnOccupantAdded += throwingVoxelOccupantAddedHandler;
            voxel.OnOccupantAdded += recordingVoxelOccupantAddedHandler;
            voxel.OnOccupantRemoved += throwingVoxelOccupantRemovedHandler;
            voxel.OnOccupantRemoved += recordingVoxelOccupantRemovedHandler;

            Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
            Assert.True(grid.TryRemoveVoxelOccupant(voxel, occupant));
        }
        finally
        {
            GridObstacleManager.OnObstacleAdded -= throwingObstacleAddedHandler;
            GridObstacleManager.OnObstacleAdded -= recordingObstacleAddedHandler;
            GridObstacleManager.OnObstacleRemoved -= throwingObstacleRemovedHandler;
            GridObstacleManager.OnObstacleRemoved -= recordingObstacleRemovedHandler;
            GridObstacleManager.OnObstaclesCleared -= throwingObstacleClearHandler;
            GridObstacleManager.OnObstaclesCleared -= recordingObstacleClearHandler;
            GridOccupantManager.OnOccupantAdded -= throwingOccupantAddedHandler;
            GridOccupantManager.OnOccupantAdded -= recordingOccupantAddedHandler;
            GridOccupantManager.OnOccupantRemoved -= throwingOccupantRemovedHandler;
            GridOccupantManager.OnOccupantRemoved -= recordingOccupantRemovedHandler;
            voxel.OnObstacleAdded -= throwingVoxelObstacleAddedHandler;
            voxel.OnObstacleAdded -= recordingVoxelObstacleAddedHandler;
            voxel.OnObstacleRemoved -= throwingVoxelObstacleRemovedHandler;
            voxel.OnObstacleRemoved -= recordingVoxelObstacleRemovedHandler;
            voxel.OnObstaclesCleared -= throwingVoxelObstacleClearHandler;
            voxel.OnObstaclesCleared -= recordingVoxelObstacleClearHandler;
            voxel.OnOccupantAdded -= throwingVoxelOccupantAddedHandler;
            voxel.OnOccupantAdded -= recordingVoxelOccupantAddedHandler;
            voxel.OnOccupantRemoved -= throwingVoxelOccupantRemovedHandler;
            voxel.OnOccupantRemoved -= recordingVoxelOccupantRemovedHandler;
        }

        Assert.Equal(2, obstacleManagerAddedNotifications);
        Assert.Equal(1, obstacleManagerRemovedNotifications);
        Assert.Equal(1, obstacleManagerClearedNotifications);
        Assert.Equal(2, obstacleVoxelAddedNotifications);
        Assert.Equal(1, obstacleVoxelRemovedNotifications);
        Assert.Equal(1, obstacleVoxelClearedNotifications);
        Assert.Equal(1, occupantManagerAddedNotifications);
        Assert.Equal(1, occupantManagerRemovedNotifications);
        Assert.Equal(1, occupantVoxelAddedNotifications);
        Assert.Equal(1, occupantVoxelRemovedNotifications);
    }

    [Fact]
    public void SplitManagerNotifications_ShouldExposeStrongPayloads()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(1, 0, 1);
        BoundsKey firstToken = new BoundsKey(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        BoundsKey secondToken = new BoundsKey(new Vector3d(2, 0, 2), new Vector3d(3, 0, 3));
        TestOccupant occupant = new TestOccupant(position, 9);

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));

        ObstacleEventInfo addedObstacle = default;
        ObstacleClearEventInfo clearedObstacle = default;
        OccupantEventInfo addedOccupant = default;
        OccupantEventInfo removedOccupant = default;

        void HandleObstacleAdded(ObstacleEventInfo eventInfo) => addedObstacle = eventInfo;
        void HandleObstaclesCleared(ObstacleClearEventInfo eventInfo) => clearedObstacle = eventInfo;
        void HandleOccupantAdded(OccupantEventInfo eventInfo) => addedOccupant = eventInfo;
        void HandleOccupantRemoved(OccupantEventInfo eventInfo) => removedOccupant = eventInfo;

        GridObstacleManager.OnObstacleAdded += HandleObstacleAdded;
        GridObstacleManager.OnObstaclesCleared += HandleObstaclesCleared;
        GridOccupantManager.OnOccupantAdded += HandleOccupantAdded;
        GridOccupantManager.OnOccupantRemoved += HandleOccupantRemoved;

        try
        {
            Assert.True(grid.TryAddObstacle(voxel, firstToken));
            Assert.True(grid.TryAddObstacle(voxel, secondToken));
            grid.ClearObstacles(voxel);

            Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
            Assert.True(grid.TryRemoveVoxelOccupant(voxel, occupant));
        }
        finally
        {
            GridObstacleManager.OnObstacleAdded -= HandleObstacleAdded;
            GridObstacleManager.OnObstaclesCleared -= HandleObstaclesCleared;
            GridOccupantManager.OnOccupantAdded -= HandleOccupantAdded;
            GridOccupantManager.OnOccupantRemoved -= HandleOccupantRemoved;
        }

        Assert.Equal(voxel.GlobalIndex, addedObstacle.VoxelIndex);
        Assert.Equal(secondToken, addedObstacle.ObstacleToken);
        Assert.Equal(2, addedObstacle.ObstacleCount);
        Assert.True(addedObstacle.GridVersion >= 1);

        Assert.Equal(voxel.GlobalIndex, clearedObstacle.VoxelIndex);
        Assert.Equal(2, clearedObstacle.ClearedObstacleCount);
        Assert.True(clearedObstacle.GridVersion >= addedObstacle.GridVersion);

        Assert.Equal(voxel.GlobalIndex, addedOccupant.VoxelIndex);
        Assert.Same(occupant, addedOccupant.Occupant);
        Assert.Equal(1, addedOccupant.OccupantCount);
        Assert.True(addedOccupant.Ticket >= 0);

        Assert.Equal(voxel.GlobalIndex, removedOccupant.VoxelIndex);
        Assert.Same(occupant, removedOccupant.Occupant);
        Assert.Equal(addedOccupant.Ticket, removedOccupant.Ticket);
        Assert.Equal(0, removedOccupant.OccupantCount);
    }

    [Fact]
    public void GridOccupantManager_ShouldRejectNullOrDuplicateOccupants()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant occupant = new TestOccupant(new Vector3d(1, 0, 1));

        Assert.True(grid.TryGetVoxel(occupant.Position, out Voxel voxel));
        Assert.False(grid.TryAddVoxelOccupant((IVoxelOccupant)null));
        Assert.False(grid.TryAddVoxelOccupant(voxel, (IVoxelOccupant)null));
        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.False(GridOccupantManager.TryAddVoxelOccupant(occupant.OccupyingIndexMap.Keys.Single(), occupant));
        Assert.False(grid.TryRemoveVoxelOccupant((IVoxelOccupant)null));
        Assert.False(grid.TryRemoveVoxelOccupant(voxel, (IVoxelOccupant)null));
        Assert.True(GridOccupantManager.TryDeregister(occupant));
    }

    [Fact]
    public void GridObstacleManager_ShouldReturnFalseForInvalidOrNonBlockableRequests()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(1, 0, 1);
        BoundsKey validToken = new BoundsKey(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        BoundsKey missingToken = new BoundsKey(new Vector3d(2, 0, 2), new Vector3d(3, 0, 3));
        GlobalVoxelIndex missingIndex = new GlobalVoxelIndex(ushort.MaxValue, new VoxelIndex(0, 0, 0), 0);
        TestOccupant occupant = new TestOccupant(position);

        Assert.True(grid.TryGetVoxel(position, out Voxel occupiedVoxel));
        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel emptyVoxel));

        Assert.False(GridObstacleManager.TryAddObstacle(missingIndex, validToken));
        Assert.False(grid.TryAddObstacle(new Vector3d(99, 0, 99), validToken));
        Assert.False(grid.TryRemoveObstacle(new Vector3d(99, 0, 99), validToken));

        Assert.True(grid.TryAddVoxelOccupant(occupiedVoxel, occupant));
        Assert.False(grid.TryAddObstacle(occupiedVoxel, validToken));
        Assert.True(grid.TryRemoveVoxelOccupant(occupiedVoxel, occupant));

        Assert.True(grid.TryAddObstacle(occupiedVoxel, validToken));
        Assert.False(grid.TryRemoveObstacle(occupiedVoxel, missingToken));

        int obstacleCountBeforeNoOpClear = grid.ObstacleCount;
        grid.ClearObstacles(emptyVoxel);

        Assert.Equal(obstacleCountBeforeNoOpClear, grid.ObstacleCount);
        Assert.True(grid.TryRemoveObstacle(occupiedVoxel, validToken));
    }

    [Fact]
    public void GridOccupantManagerAndScanManager_ShouldHandleUnavailableOrEmptyInputs()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d emptyPosition = new Vector3d(0, 0, 0);
        TestOccupant outsideOccupant = new TestOccupant(new Vector3d(99, 0, 99), 2);
        TestOccupant localOccupant = new TestOccupant(new Vector3d(1, 0, 1), 3);
        GlobalVoxelIndex missingGlobalIndex = new GlobalVoxelIndex(ushort.MaxValue, new VoxelIndex(0, 0, 0), 0);

        Assert.True(grid.TryGetVoxel(emptyPosition, out Voxel emptyVoxel));
        Assert.True(grid.TryAddVoxelOccupant(localOccupant));
        Assert.True(grid.TryGetVoxel(localOccupant.Position, out Voxel occupiedVoxel));
        Assert.True(localOccupant.OccupyingIndexMap.TryGetValue(occupiedVoxel.GlobalIndex, out int localTicket));

        Assert.False(GridOccupantManager.TryRegister(outsideOccupant));
        Assert.False(GridOccupantManager.TryDeregister(outsideOccupant));
        Assert.False(grid.TryAddVoxelOccupant(new VoxelIndex(-1, 0, 0), new TestOccupant(emptyPosition)));
        Assert.False(GridOccupantManager.TryAddVoxelOccupant(missingGlobalIndex, new TestOccupant(emptyPosition)));
        Assert.False(GridOccupantManager.TryRemoveVoxelOccupant(missingGlobalIndex, new TestOccupant(emptyPosition)));
        Assert.False(grid.TryRemoveVoxelOccupant(new VoxelIndex(-1, 0, 0), new TestOccupant(emptyPosition)));

        Assert.Empty(GridScanManager.ScanRadius<TestOccupant>(new Vector3d(99, 0, 99), (Fixed64)1).ToArray());
        Assert.Empty(GridScanManager.GetVoxelOccupantsByType<TestOccupant>(missingGlobalIndex));
        Assert.Empty(grid.GetVoxelOccupantsByType<TestOccupant>(new Vector3d(99, 0, 99)));
        Assert.Empty(grid.GetVoxelOccupantsByType<TestOccupant>(new VoxelIndex(-1, 0, 0)));
        Assert.Empty(grid.GetVoxelOccupantsByType<TestOccupant>((Voxel)null));

        Assert.False(grid.TryGetVoxelOccupant(emptyVoxel, 0, out IVoxelOccupant missingOccupant));
        Assert.Null(missingOccupant);
        Assert.False(grid.TryGetVoxelOccupant(new Vector3d(99, 0, 99), 0, out IVoxelOccupant missingPositionOccupant));
        Assert.Null(missingPositionOccupant);
        Assert.True(grid.TryGetVoxelOccupant(localOccupant.Position, localTicket, out IVoxelOccupant byPositionOccupant));
        Assert.Same(localOccupant, byPositionOccupant);
        Assert.True(grid.TryGetVoxelOccupant(occupiedVoxel.Index, localTicket, out IVoxelOccupant byIndexOccupant));
        Assert.Same(localOccupant, byIndexOccupant);
        Assert.Empty(GridScanManager.GetOccupants(missingGlobalIndex));
        Assert.Empty(grid.GetOccupants(new Vector3d(99, 0, 99)));
        Assert.Empty(grid.GetOccupants(new VoxelIndex(-1, 0, 0)));

        Assert.Empty(GridScanManager.GetConditionalOccupants(missingGlobalIndex));
        Assert.Empty(grid.GetConditionalOccupants(new Vector3d(99, 0, 99)));
        Assert.Empty(grid.GetConditionalOccupants(new VoxelIndex(-1, 0, 0)));
        Assert.Empty(grid.GetConditionalOccupants((Voxel)null));
    }

    [Fact]
    public void GridOccupantManager_ShouldRejectUnavailableVoxelState()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(1, 0, 1);
        BoundsKey obstacleToken = new BoundsKey(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        TestOccupant blockedOccupant = new TestOccupant(position, 4);
        TestOccupant staleOccupant = new TestOccupant(position, 5);

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));
        Assert.True(grid.TryAddObstacle(voxel, obstacleToken));
        Assert.False(grid.TryAddVoxelOccupant(voxel, blockedOccupant));

        Assert.True(grid.TryRemoveObstacle(voxel, obstacleToken));

        staleOccupant.SetOccupancy(voxel.GlobalIndex, 123);
        Assert.False(grid.TryRemoveVoxelOccupant(voxel, staleOccupant));
    }
}
