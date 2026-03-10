using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using System;
using System.Linq;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class VoxelGridTests : IDisposable
{
    public static TheoryData<SpatialDirection, VoxelIndex> BoundaryDirectionCases => CreateBoundaryDirectionCases();

    public VoxelGridTests()
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
    public void Initialize_ShouldSetCorrectDimensions()
    {
        var start = new Vector3d(-10, 0, -10);
        var end = new Vector3d(10, 0, 10);
        var config = new GridConfiguration(start, end);
        GlobalGridManager.TryAddGrid(config, out ushort index);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[index];

        int width = ((end.x - start.x) / GlobalGridManager.VoxelSize).FloorToInt() + 1;
        int height = ((end.y - start.y) / GlobalGridManager.VoxelSize).FloorToInt() + 1;
        int length = ((end.z - start.z) / GlobalGridManager.VoxelSize).FloorToInt() + 1;

        Assert.Equal(width, grid.Width);
        Assert.Equal(height, grid.Height);
        Assert.Equal(length, grid.Length);
        Assert.True(grid.IsActive);
    }

    [Fact]
    public void GetVoxel_ShouldReturnCorrectVoxel()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort index);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[index];

        bool found = grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        Assert.True(found);
        Assert.NotNull(voxel);
    }

    [Fact]
    public void IsVoxelAllocated_ShouldReturnCorrectState()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort index);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[index];

        Assert.True(grid.IsVoxelAllocated(10, 0, 10));
        Assert.True(grid.IsVoxelAllocated(20, 0, 20));
    }

    [Fact]
    public void GetScanCell_ShouldReturnValidCell()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort index);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[index];

        bool found = grid.TryGetScanCell(new Vector3d(0, 0, 0), out ScanCell scanCell);

        Assert.True(found);
        Assert.NotNull(scanCell);
    }

    [Fact]
    public void GetActiveScanCells_ShouldReturnExpectedCount()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort index);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[index];

        int count = 0;
        foreach (var cell in grid.GetActiveScanCells())
            count++;

        Assert.Equal(grid.ActiveScanCells?.Count ?? 0, count);
    }

    [Fact]
    public void Grid_ShouldCorrectlyManageNeighbors()
    {
        var config1 = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        var config2 = new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(30, 0, 30));

        GlobalGridManager.TryAddGrid(config1, out ushort index1);
        GlobalGridManager.TryAddGrid(config2, out ushort index2);

        VoxelGrid grid1 = GlobalGridManager.ActiveGrids[index1];
        VoxelGrid grid2 = GlobalGridManager.ActiveGrids[index2];

        Assert.True(grid1.IsConjoined);
        Assert.True(grid2.IsConjoined);

        // since tests run in parallel...there maybe other grids in play
        Assert.True(grid1.NeighborCount >= 1);
        Assert.True(grid1.NeighborCount >= 1);

        // get the direction before removal
        int neighborIndex = (int)VoxelGrid.GetNeighborDirection(grid1, grid2);

        GlobalGridManager.TryRemoveGrid(grid2.GlobalIndex);

        if (grid1.Neighbors != null)
        {
            if (grid1.Neighbors.ContainsKey(neighborIndex))
                Assert.DoesNotContain(grid2.GlobalIndex, grid1.Neighbors[neighborIndex]);
        }
        else
            Assert.False(grid1.IsConjoined);

        Assert.False(grid2.IsActive);
    }

    [Fact]
    public void TryGetVoxelIndex_ShouldHandleNegativePositionsAndFractionalVoxelSize()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup((Fixed64)0.5);

        try
        {
            var config = new GridConfiguration(new Vector3d(-1.5, 0, -1.5), new Vector3d(1.5, 0, 1.5));

            Assert.True(GlobalGridManager.TryAddGrid(config, out ushort index));

            VoxelGrid grid = GlobalGridManager.ActiveGrids[index];

            Assert.True(grid.TryGetVoxelIndex(new Vector3d(-1.25, 0, -0.75), out VoxelIndex negativeIndex));
            Assert.Equal(new VoxelIndex(0, 0, 1), negativeIndex);

            Assert.Equal(
                new Vector3d(-1.5, 0, -1.0),
                grid.FloorToGrid(new Vector3d(-1.26, 0, -0.74)));
            Assert.Equal(
                new Vector3d(-1.0, 0, -0.5),
                grid.CeilToGrid(new Vector3d(-1.26, 0, -0.74)));
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void GetScanCellKey_ShouldTransitionAcrossScanCellBoundaries()
    {
        var config = new GridConfiguration(
            new Vector3d(0, 0, 0),
            new Vector3d(15, 0, 15),
            scanCellSize: 4);

        Assert.True(GlobalGridManager.TryAddGrid(config, out ushort index));

        VoxelGrid grid = GlobalGridManager.ActiveGrids[index];
        Vector3d beforeBoundary = new Vector3d(3.9, 0, 3.9);
        Vector3d atBoundary = new Vector3d(4, 0, 4);

        int firstCellKey = grid.GetScanCellKey(beforeBoundary);
        int secondCellKey = grid.GetScanCellKey(atBoundary);

        Assert.NotEqual(firstCellKey, secondCellKey);
        Assert.Equal((0, 0, 0), grid.SnapToScanCell(beforeBoundary));
        Assert.Equal((1, 0, 1), grid.SnapToScanCell(atBoundary));
    }

    [Fact]
    public void GetActiveScanCells_ShouldReturnEmptyWhenGridHasNoOccupants()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(5, 0, 5)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Assert.Empty(grid.GetActiveScanCells());
    }

    [Theory]
    [MemberData(nameof(BoundaryDirectionCases))]
    public void IsFacingBoundaryDirection_ShouldMatchSpatialDirectionOffsets(
        SpatialDirection direction,
        VoxelIndex boundaryIndex)
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Assert.True(grid.IsFacingBoundaryDirection(boundaryIndex, direction));
    }

    [Fact]
    public void IsFacingBoundaryDirection_ShouldReturnFalseForCenterVoxel()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        VoxelIndex centerIndex = new VoxelIndex(1, 1, 1);

        foreach (SpatialDirection direction in SpatialAwareness.AllDirections)
            Assert.False(grid.IsFacingBoundaryDirection(centerIndex, direction));
    }

    [Fact]
    public void Grid_ShouldHandleComplexConnectionsDuringDynamicLoadAndUnload()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10)),
            out ushort centerIndex));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(20, 0, 10)),
            out _));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 10), new Vector3d(10, 0, 20)),
            out _));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(20, 0, 20)),
            out ushort northEastIndex));

        VoxelGrid centerGrid = GlobalGridManager.ActiveGrids[centerIndex];

        Assert.Equal(3, centerGrid.NeighborCount);
        Assert.Equal(3, centerGrid.GetAllGridNeighbors().Select(grid => grid.GlobalIndex).Distinct().Count());

        Assert.True(GlobalGridManager.TryRemoveGrid(northEastIndex));

        Assert.Equal(2, centerGrid.NeighborCount);
        Assert.Equal(2, centerGrid.GetAllGridNeighbors().Select(grid => grid.GlobalIndex).Distinct().Count());
    }

    [Fact]
    public void GetNeighborDirection_ShouldFollowCardinalAxisOffsets()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort centerIndex));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(2, 0, 1)),
            out ushort eastIndex));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 1), new Vector3d(1, 0, 2)),
            out ushort northIndex));

        VoxelGrid centerGrid = GlobalGridManager.ActiveGrids[centerIndex];
        VoxelGrid eastGrid = GlobalGridManager.ActiveGrids[eastIndex];
        VoxelGrid northGrid = GlobalGridManager.ActiveGrids[northIndex];

        Assert.Equal(SpatialDirection.East, VoxelGrid.GetNeighborDirection(centerGrid, eastGrid));
        Assert.Equal(SpatialDirection.North, VoxelGrid.GetNeighborDirection(centerGrid, northGrid));
    }

    private static TheoryData<SpatialDirection, VoxelIndex> CreateBoundaryDirectionCases()
    {
        TheoryData<SpatialDirection, VoxelIndex> cases = new TheoryData<SpatialDirection, VoxelIndex>();

        for (int i = 0; i < SpatialAwareness.DirectionOffsets.Length; i++)
        {
            (int x, int y, int z) offset = SpatialAwareness.DirectionOffsets[i];
            VoxelIndex boundaryIndex = new VoxelIndex(
                offset.x < 0 ? 0 : offset.x > 0 ? 2 : 1,
                offset.y < 0 ? 0 : offset.y > 0 ? 2 : 1,
                offset.z < 0 ? 0 : offset.z > 0 ? 2 : 1);

            cases.Add((SpatialDirection)i, boundaryIndex);
        }

        return cases;
    }
}
