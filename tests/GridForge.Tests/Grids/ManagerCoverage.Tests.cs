using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using System;
using System.Linq;
using System.Reflection;
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
        Vector3d position = new(1, 0, 1);
        BoundsKey firstToken = new(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        BoundsKey secondToken = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));
        Assert.True(grid.TryAddObstacle(position, firstToken));
        Assert.True(GridObstacleManager.TryAddObstacle(voxel.WorldIndex, secondToken));
        Assert.Equal(2, voxel.ObstacleCount);

        Assert.True(grid.TryRemoveObstacle(position, firstToken));
        Assert.True(GridObstacleManager.TryRemoveObstacle(voxel.WorldIndex, secondToken));
        Assert.False(voxel.IsBlocked);
        Assert.False(grid.TryRemoveObstacle(position, firstToken));
    }

    [Fact]
    public void OccupantManagerAndScanManager_ShouldSupportWrapperOverloads()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant occupant = new(new Vector3d(2, 0, 2), 7);

        Assert.True(GridOccupantManager.TryRegister(occupant));
        Assert.True(grid.TryGetVoxel(occupant.Position, out Voxel voxel));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(occupant, voxel.WorldIndex, out int ticket));
        Assert.True(GridScanManager.TryGetVoxelOccupant(voxel.WorldIndex, ticket, out IVoxelOccupant registeredOccupant));
        Assert.Same(occupant, registeredOccupant);
        Assert.Single(GridScanManager.GetOccupants(voxel.WorldIndex));
        Assert.Single(GridScanManager.GetVoxelOccupantsByType<TestOccupant>(voxel.WorldIndex));
        Assert.Single(grid.GetVoxelOccupantsByType<TestOccupant>(voxel.Index));
        Assert.Single(grid.GetConditionalOccupants(voxel.Index, groupCondition: groupId => groupId == 7));
        Assert.True(grid.TryGetScanCell(voxel.Index, out ScanCell scanCell));
        Assert.True(scanCell.IsOccupied);

        TestOccupant secondOccupant = new(occupant.Position, 8);

        Assert.True(grid.TryAddVoxelOccupant(voxel.Index, secondOccupant));
        Assert.True(GridOccupantManager.TryRemoveVoxelOccupant(voxel.WorldIndex, occupant));
        Assert.True(grid.TryRemoveVoxelOccupant(voxel.Index, secondOccupant));
        Assert.False(scanCell.IsOccupied);
    }

    [Fact]
    public void ScanManager_ShouldSupportOccupantConditionWithoutGroupFilter()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new(2, 0, 2);
        TestOccupant targetOccupant = new(position, 1);
        TestOccupant otherOccupant = new(position, 2);

        Assert.True(grid.TryAddVoxelOccupant(targetOccupant));
        Assert.True(grid.TryAddVoxelOccupant(otherOccupant));
        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));

        IVoxelOccupant[] byGlobalIndex = GridScanManager.GetConditionalOccupants(
            voxel.WorldIndex,
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
        Vector3d position = new(2, 0, 2);
        TestOccupant firstOccupant = new(position, 1);
        TestOccupant secondOccupant = new(position, 2);

        Assert.True(grid.TryAddVoxelOccupant(firstOccupant));
        Assert.True(grid.TryAddVoxelOccupant(secondOccupant));
        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));

        IVoxelOccupant[] groupMatches = GridScanManager.GetConditionalOccupants(
            voxel.WorldIndex,
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

        WorldVoxelIndex missingGridIndex = new(int.MaxValue, ushort.MaxValue, 0, new VoxelIndex(0, 0, 0));

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
        Vector3d position = new(1, 0, 1);
        BoundsKey obstacleToken = new(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        BoundsKey secondObstacleToken = new(new Vector3d(2, 0, 2), new Vector3d(3, 0, 3));
        TestOccupant occupant = new(position);
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
        Vector3d position = new(1, 0, 1);
        BoundsKey firstToken = new(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        BoundsKey secondToken = new(new Vector3d(2, 0, 2), new Vector3d(3, 0, 3));
        TestOccupant occupant = new(position, 9);

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

        Assert.Equal(voxel.WorldIndex, addedObstacle.VoxelIndex);
        Assert.Equal(secondToken, addedObstacle.ObstacleToken);
        Assert.Equal(2, addedObstacle.ObstacleCount);
        Assert.True(addedObstacle.GridVersion >= 1);

        Assert.Equal(voxel.WorldIndex, clearedObstacle.VoxelIndex);
        Assert.Equal(2, clearedObstacle.ClearedObstacleCount);
        Assert.True(clearedObstacle.GridVersion >= addedObstacle.GridVersion);

        Assert.Equal(voxel.WorldIndex, addedOccupant.VoxelIndex);
        Assert.Same(occupant, addedOccupant.Occupant);
        Assert.Equal(1, addedOccupant.OccupantCount);
        Assert.True(addedOccupant.Ticket >= 0);

        Assert.Equal(voxel.WorldIndex, removedOccupant.VoxelIndex);
        Assert.Same(occupant, removedOccupant.Occupant);
        Assert.Equal(addedOccupant.Ticket, removedOccupant.Ticket);
        Assert.Equal(0, removedOccupant.OccupantCount);
    }

    [Fact]
    public void GridOccupantManager_ShouldRejectNullOrDuplicateOccupants()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant occupant = new(new Vector3d(1, 0, 1));

        Assert.True(grid.TryGetVoxel(occupant.Position, out Voxel voxel));
        Assert.False(grid.TryAddVoxelOccupant((IVoxelOccupant)null));
        Assert.False(grid.TryAddVoxelOccupant(voxel, (IVoxelOccupant)null));
        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.False(GridOccupantManager.TryAddVoxelOccupant(GridOccupantManager.GetOccupiedIndices(occupant).Single(), occupant));
        Assert.False(grid.TryRemoveVoxelOccupant((IVoxelOccupant)null));
        Assert.False(grid.TryRemoveVoxelOccupant(voxel, (IVoxelOccupant)null));
        Assert.True(GridOccupantManager.TryDeregister(occupant));
    }

    [Fact]
    public void GridOccupantManager_ShouldDeregisterTrackedOccupants_WhenPositionChanges()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant occupant = new(new Vector3d(2, 0, 2), 6);

        Assert.True(GridOccupantManager.TryRegister(occupant));
        Assert.True(grid.TryGetVoxel(new Vector3d(2, 0, 2), out Voxel voxel));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(occupant, voxel.WorldIndex, out int ticket));

        occupant.Position = new Vector3d(99, 0, 99);

        Assert.True(GridOccupantManager.TryDeregister(occupant));
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(occupant));
        Assert.False(grid.TryGetVoxelOccupant(voxel, ticket, out _));
    }

    [Fact]
    public void GridObstacleManager_ShouldReturnFalseForInvalidOrNonBlockableRequests()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new(1, 0, 1);
        BoundsKey validToken = new(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        BoundsKey missingToken = new(new Vector3d(2, 0, 2), new Vector3d(3, 0, 3));
        WorldVoxelIndex missingIndex = new(int.MaxValue, ushort.MaxValue, 0, new VoxelIndex(0, 0, 0));
        TestOccupant occupant = new(position);

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
        Vector3d emptyPosition = new(0, 0, 0);
        TestOccupant outsideOccupant = new(new Vector3d(99, 0, 99), 2);
        TestOccupant localOccupant = new(new Vector3d(1, 0, 1), 3);
        WorldVoxelIndex missingGlobalIndex = new(int.MaxValue, ushort.MaxValue, 0, new VoxelIndex(0, 0, 0));

        Assert.True(grid.TryGetVoxel(emptyPosition, out Voxel emptyVoxel));
        Assert.True(grid.TryAddVoxelOccupant(localOccupant));
        Assert.True(grid.TryGetVoxel(localOccupant.Position, out Voxel occupiedVoxel));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(localOccupant, occupiedVoxel.WorldIndex, out int localTicket));

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
        Vector3d position = new(1, 0, 1);
        BoundsKey obstacleToken = new(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2));
        TestOccupant blockedOccupant = new(position, 4);
        TestOccupant staleOccupant = new(position, 5);

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));
        Assert.True(grid.TryAddObstacle(voxel, obstacleToken));
        Assert.False(grid.TryAddVoxelOccupant(voxel, blockedOccupant));

        Assert.True(grid.TryRemoveObstacle(voxel, obstacleToken));
        Assert.False(grid.TryRemoveVoxelOccupant(voxel, staleOccupant));
    }

    [Fact]
    public void GridOccupantManager_ShouldHandleRegistryCollisionNullQueriesAndDeterministicOrdering()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            out ushort firstIndex));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(12, 0, 12)),
            out ushort secondIndex));

        VoxelGrid firstGrid = GlobalGridManager.ActiveGrids[firstIndex];
        VoxelGrid secondGrid = GlobalGridManager.ActiveGrids[secondIndex];
        Guid sharedId = Guid.NewGuid();
        SharedIdOccupant primaryOccupant = new(sharedId, new Vector3d(0, 0, 0));
        SharedIdOccupant collidingOccupant = new(sharedId, new Vector3d(0, 0, 0));
        TestOccupant missingOccupant = new(new Vector3d(99, 0, 99));

        Assert.False(GridOccupantManager.TryGetOccupancyTicket(null, default, out int nullTicket));
        Assert.Equal(-1, nullTicket);

        Assert.True(firstGrid.TryGetVoxel(new Vector3d(0, 0, 1), out Voxel firstVoxel));
        Assert.True(firstGrid.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel secondVoxel));
        Assert.True(secondGrid.TryGetVoxel(new Vector3d(10, 0, 10), out Voxel thirdVoxel));
        Assert.True(firstGrid.TryAddVoxelOccupant(secondVoxel, primaryOccupant));
        Assert.True(secondGrid.TryAddVoxelOccupant(thirdVoxel, primaryOccupant));
        Assert.True(firstGrid.TryAddVoxelOccupant(firstVoxel, primaryOccupant));

        Assert.False(GridOccupantManager.TryGetOccupancyTicket(
            missingOccupant,
            firstVoxel.WorldIndex,
            out int missingTicket));
        Assert.Equal(-1, missingTicket);
        Assert.False(InvokeTryGetTrackedRecordUnsafe(firstGrid.World!, null));

        Assert.False(firstGrid.TryAddVoxelOccupant(firstVoxel, collidingOccupant));
        Assert.False(GridOccupantManager.TryGetOccupancyTicket(
            collidingOccupant,
            firstVoxel.WorldIndex,
            out int collisionTicket));
        Assert.Equal(-1, collisionTicket);

        WorldVoxelIndex[] trackedIndices = GridOccupantManager.GetOccupiedIndices(primaryOccupant).ToArray();

        Assert.Equal(
            new[]
            {
                firstVoxel.WorldIndex,
                secondVoxel.WorldIndex,
                thirdVoxel.WorldIndex,
            },
            trackedIndices);

        Assert.True(GridOccupantManager.TryGetOccupancyTicket(primaryOccupant, firstVoxel.WorldIndex, out _));
    }

    [Fact]
    public void GridOccupantManager_ShouldFilterSnapshotsAndForgetStaleTrackedEntries()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort firstIndex));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(11, 0, 11)),
            out ushort secondIndex));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 0, 21)),
            out ushort unrelatedIndex));

        VoxelGrid firstGrid = GlobalGridManager.ActiveGrids[firstIndex];
        VoxelGrid secondGrid = GlobalGridManager.ActiveGrids[secondIndex];
        VoxelGrid unrelatedGrid = GlobalGridManager.ActiveGrids[unrelatedIndex];
        TestOccupant occupant = new(new Vector3d(0, 0, 0), 9);

        Assert.True(firstGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel firstVoxel));
        Assert.True(secondGrid.TryGetVoxel(new Vector3d(10, 0, 10), out Voxel secondVoxel));
        Assert.True(firstGrid.TryAddVoxelOccupant(firstVoxel, occupant));
        Assert.True(secondGrid.TryAddVoxelOccupant(secondVoxel, occupant));

        Assert.False(unrelatedGrid.TryRemoveVoxelOccupant(occupant));
        Assert.True(firstGrid.TryRemoveVoxelOccupant(occupant));

        WorldVoxelIndex[] remainingIndices = GridOccupantManager.GetOccupiedIndices(occupant).ToArray();
        Assert.Single(remainingIndices);
        Assert.Equal(secondVoxel.WorldIndex, remainingIndices[0]);

        Assert.True(InvokeTryTrackOccupancy(
            firstGrid.World!,
            occupant,
            new WorldVoxelIndex(int.MaxValue, ushort.MaxValue, 0, new VoxelIndex(0, 0, 0)),
            77));

        Assert.True(GridOccupantManager.TryDeregister(occupant));
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(occupant));
    }

    [Fact]
    public void GridOccupantManager_ShouldHandleTrackedEntriesThatNoLongerMatchVoxelState()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant unavailableVoxelOccupant = new(new Vector3d(0, 0, 0), 1);
        TestOccupant staleGridOccupant = new(new Vector3d(0, 0, 0), 2);
        TestOccupant staleSpawnOccupant = new(new Vector3d(0, 0, 0), 3);

        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));

        Assert.True(InvokeTryTrackOccupancy(grid.World!, unavailableVoxelOccupant, voxel.WorldIndex, 123));
        Assert.False(grid.TryRemoveVoxelOccupant(voxel, unavailableVoxelOccupant));
        Assert.True(InvokeForgetTrackedOccupancy(grid.World!, unavailableVoxelOccupant, voxel.WorldIndex));
        Assert.False(InvokeForgetTrackedOccupancy(grid.World!, unavailableVoxelOccupant, voxel.WorldIndex));
        Assert.False(InvokeForgetTrackedOccupancy(grid.World!, null, voxel.WorldIndex));

        WorldVoxelIndex staleLocalIndex = new(
            grid.World!.SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            new VoxelIndex(99, 0, 99));
        Assert.True(InvokeTryTrackOccupancy(grid.World!, staleGridOccupant, staleLocalIndex, 321));
        Assert.True(InvokeTryTrackOccupancy(
            grid.World!,
            staleSpawnOccupant,
            new WorldVoxelIndex(grid.World!.SpawnToken, grid.GridIndex, grid.SpawnToken + 1, voxel.Index),
            222));

        Assert.True(grid.TryRemoveVoxelOccupant(staleGridOccupant));
        Assert.True(grid.TryRemoveVoxelOccupant(staleSpawnOccupant));
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(staleGridOccupant));
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(staleSpawnOccupant));
    }

    [Fact]
    public void GridOccupantManager_TrackedOccupancyComparer_ShouldSortAcrossEveryKey()
    {
        WorldVoxelIndex origin = new(1, 1, 10, new VoxelIndex(0, 0, 0));

        Assert.True(CompareTrackedOccupancies(
            origin,
            1,
            new WorldVoxelIndex(2, 1, 10, new VoxelIndex(0, 0, 0)),
            1) < 0);
        Assert.True(CompareTrackedOccupancies(
            origin,
            1,
            new WorldVoxelIndex(1, 2, 10, new VoxelIndex(0, 0, 0)),
            1) < 0);
        Assert.True(CompareTrackedOccupancies(
            origin,
            1,
            new WorldVoxelIndex(1, 1, 10, new VoxelIndex(1, 0, 0)),
            1) < 0);
        Assert.True(CompareTrackedOccupancies(
            origin,
            1,
            new WorldVoxelIndex(1, 1, 10, new VoxelIndex(0, 1, 0)),
            1) < 0);
        Assert.True(CompareTrackedOccupancies(
            origin,
            1,
            new WorldVoxelIndex(1, 1, 10, new VoxelIndex(0, 0, 1)),
            1) < 0);
        Assert.True(CompareTrackedOccupancies(
            origin,
            1,
            new WorldVoxelIndex(1, 1, 11, new VoxelIndex(0, 0, 0)),
            1) < 0);
        Assert.True(CompareTrackedOccupancies(
            origin,
            1,
            new WorldVoxelIndex(1, 1, 10, new VoxelIndex(0, 0, 0)),
            2) < 0);
        Assert.Equal(0, CompareTrackedOccupancies(origin, 3, origin, 3));
    }

    private static bool InvokeTryTrackOccupancy(
        GridWorld world,
        IVoxelOccupant occupant,
        WorldVoxelIndex index,
        int ticket)
    {
        MethodInfo method = typeof(GridOccupantManager).GetMethod(
            "TryTrackOccupancy",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find GridOccupantManager.TryTrackOccupancy.");

        return (bool)method.Invoke(null, new object[] { world, occupant, index, ticket });
    }

    private static bool InvokeForgetTrackedOccupancy(
        GridWorld world,
        IVoxelOccupant occupant,
        WorldVoxelIndex index)
    {
        MethodInfo method = typeof(GridOccupantManager).GetMethod(
            "ForgetTrackedOccupancy",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find GridOccupantManager.ForgetTrackedOccupancy.");

        return (bool)method.Invoke(null, new object[] { world, occupant, index });
    }

    private static bool InvokeTryGetTrackedRecordUnsafe(GridWorld world, IVoxelOccupant occupant)
    {
        MethodInfo method = typeof(GridOccupantManager).GetMethod(
            "TryGetTrackedRecordUnsafe",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find GridOccupantManager.TryGetTrackedRecordUnsafe.");
        object[] arguments = { world, occupant, null };

        bool result = (bool)method.Invoke(null, arguments);
        return result;
    }

    private static int CompareTrackedOccupancies(
        WorldVoxelIndex leftIndex,
        int leftTicket,
        WorldVoxelIndex rightIndex,
        int rightTicket)
    {
        Type trackedOccupancyType = typeof(GridOccupantManager).GetNestedType(
            "TrackedOccupancy",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find GridOccupantManager.TrackedOccupancy.");
        MethodInfo compareMethod = typeof(GridOccupantManager).GetMethod(
            "CompareTrackedOccupancies",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find GridOccupantManager.CompareTrackedOccupancies.");

        object left = Activator.CreateInstance(
            trackedOccupancyType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object[] { leftIndex, leftTicket },
            culture: null);
        object right = Activator.CreateInstance(
            trackedOccupancyType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object[] { rightIndex, rightTicket },
            culture: null);

        return (int)compareMethod.Invoke(null, new[] { left, right });
    }

    private sealed class SharedIdOccupant : IVoxelOccupant
    {
        public Guid GlobalId { get; }

        public byte OccupantGroupId { get; }

        public Vector3d Position { get; set; }

        public SharedIdOccupant(Guid globalId, Vector3d position, byte occupantGroupId = byte.MaxValue)
        {
            GlobalId = globalId;
            Position = position;
            OccupantGroupId = occupantGroupId;
        }
    }
}
