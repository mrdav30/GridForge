using Xunit;
using FixedMathSharp;
using GridForge.Configuration;

namespace GridForge.Grids.Tests
{
    [Collection("GridForgeCollection")]
    public class GridTests
    {
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
            SpatialDirection neighborDirection = VoxelGrid.GetNeighborDirection(grid1, grid2);

            GlobalGridManager.TryRemoveGrid(grid2.GlobalIndex);

            if (grid1.Neighbors != null)
            {
                if (grid1.Neighbors.ContainsKey(neighborDirection))
                    Assert.DoesNotContain(grid2.GlobalIndex, grid1.Neighbors[neighborDirection]);
                else
                    Assert.DoesNotContain(neighborDirection, grid1.Neighbors);
            }
            else
                Assert.False(grid1.IsConjoined);

            Assert.False(grid2.IsActive);
        }
    }
}
