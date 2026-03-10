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
        TestOccupant occupant = new TestOccupant(position);
        int obstacleManagerNotifications = 0;
        int obstacleVoxelNotifications = 0;
        int occupantManagerNotifications = 0;
        int occupantVoxelNotifications = 0;
        Action<GridChange, GlobalVoxelIndex> throwingObstacleHandler = (_, _) => throw new InvalidOperationException("obstacle event");
        Action<GridChange, GlobalVoxelIndex> recordingObstacleHandler = (_, _) => obstacleManagerNotifications++;
        Action<GridChange, GlobalVoxelIndex> throwingOccupantHandler = (_, _) => throw new InvalidOperationException("occupant event");
        Action<GridChange, GlobalVoxelIndex> recordingOccupantHandler = (_, _) => occupantManagerNotifications++;
        Action<GridChange, Voxel> throwingVoxelObstacleHandler = (_, _) => throw new InvalidOperationException("voxel obstacle event");
        Action<GridChange, Voxel> recordingVoxelObstacleHandler = (_, _) => obstacleVoxelNotifications++;
        Action<GridChange, Voxel> throwingVoxelOccupantHandler = (_, _) => throw new InvalidOperationException("voxel occupant event");
        Action<GridChange, Voxel> recordingVoxelOccupantHandler = (_, _) => occupantVoxelNotifications++;

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));

        try
        {
            GridObstacleManager.OnObstacleChange += throwingObstacleHandler;
            GridObstacleManager.OnObstacleChange += recordingObstacleHandler;
            voxel.OnObstacleChange += throwingVoxelObstacleHandler;
            voxel.OnObstacleChange += recordingVoxelObstacleHandler;

            Assert.True(grid.TryAddObstacle(voxel, obstacleToken));
            Assert.True(grid.TryRemoveObstacle(voxel, obstacleToken));

            GridOccupantManager.OnOccupantChange += throwingOccupantHandler;
            GridOccupantManager.OnOccupantChange += recordingOccupantHandler;
            voxel.OnOccupantChange += throwingVoxelOccupantHandler;
            voxel.OnOccupantChange += recordingVoxelOccupantHandler;

            Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
            Assert.True(grid.TryRemoveVoxelOccupant(voxel, occupant));
        }
        finally
        {
            GridObstacleManager.OnObstacleChange -= throwingObstacleHandler;
            GridObstacleManager.OnObstacleChange -= recordingObstacleHandler;
            GridOccupantManager.OnOccupantChange -= throwingOccupantHandler;
            GridOccupantManager.OnOccupantChange -= recordingOccupantHandler;
            voxel.OnObstacleChange -= throwingVoxelObstacleHandler;
            voxel.OnObstacleChange -= recordingVoxelObstacleHandler;
            voxel.OnOccupantChange -= throwingVoxelOccupantHandler;
            voxel.OnOccupantChange -= recordingVoxelOccupantHandler;
        }

        Assert.Equal(2, obstacleManagerNotifications);
        Assert.Equal(2, obstacleVoxelNotifications);
        Assert.Equal(2, occupantManagerNotifications);
        Assert.Equal(2, occupantVoxelNotifications);
    }

    [Fact]
    public void GridOccupantManager_ShouldRejectNullOrDuplicateOccupants()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant occupant = new TestOccupant(new Vector3d(1, 0, 1));

        Assert.False(grid.TryAddVoxelOccupant((IVoxelOccupant)null));
        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.False(GridOccupantManager.TryAddVoxelOccupant(occupant.OccupyingIndexMap.Keys.Single(), occupant));
        Assert.False(grid.TryRemoveVoxelOccupant((IVoxelOccupant)null));
        Assert.True(GridOccupantManager.TryDeregister(occupant));
    }
}
