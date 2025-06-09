using Xunit;
using FixedMathSharp;
using GridForge.Configuration;
using System.Collections.Generic;
using System.Linq;
using GridForge.Utility;
using GridForge.Blockers;

namespace GridForge.Grids.Tests
{
    [Collection("GridForgeCollection")]
    public class GridTracerTests
    {
        [Fact]
        public void TraceLine_ShouldReturnCorrectVoxels()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-50, -1, -50), new Vector3d(50, 1, 50)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d start = new Vector3d(5, 0.5, 5);
            Vector3d end = new Vector3d(45.28, 1, 18.31);

            List<Voxel> tracedVoxels = new List<Voxel>();

            foreach (var gridVoxelSet in GridTracer.TraceLine(start, end, includeEnd: true))
            {
                if (gridVoxelSet.Grid.GlobalIndex == gridIndex)
                    tracedVoxels.AddRange(gridVoxelSet.Voxels);
            }

            Assert.NotEmpty(tracedVoxels);

            grid.TryGetVoxel(start, out Voxel startVoxel);
            grid.TryGetVoxel(end, out Voxel endVoxel);

            // Ensure that the first and last voxel correspond to the start and end positions
            Assert.Equal(startVoxel.GlobalIndex, tracedVoxels.First().GlobalIndex);
            Assert.Equal(endVoxel.GlobalIndex, tracedVoxels.Last().GlobalIndex);
        }

        [Fact]
        public void TraceLine_ShouldNotIncludeEndWhenSpecified()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d start = new Vector3d(-5, 0, -5);
            Vector3d end = new Vector3d(5, 0, 5);

            List<Voxel> tracedVoxels = new List<Voxel>();

            foreach (var gridVoxelSet in GridTracer.TraceLine(start, end, includeEnd: false))
            {
                if (gridVoxelSet.Grid.GlobalIndex == gridIndex)
                    tracedVoxels.AddRange(gridVoxelSet.Voxels);
            }

            Assert.NotEmpty(tracedVoxels);

            grid.TryGetVoxel(end, out Voxel endVoxel);

            // Ensure that the last voxel is not the end voxel
            Assert.NotEqual(endVoxel.SpawnToken, tracedVoxels.Last().SpawnToken);
        }

        [Fact]
        public void TraceLine2D_ShouldReturnCorrectVoxels()
        {
            GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), 
                out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector2d start = new Vector2d(-5, -5);
            Vector2d end = new Vector2d(5, 5);

            List<Voxel> tracedVoxels = new List<Voxel>();

            foreach (var gridVoxelSet in GridTracer.TraceLine(start, end, includeEnd: true))
            {
                if (gridVoxelSet.Grid.GlobalIndex == gridIndex)
                    tracedVoxels.AddRange(gridVoxelSet.Voxels);
            }

            Assert.NotEmpty(tracedVoxels);

            grid.TryGetVoxel(start.ToVector3d(Fixed64.Zero), out Voxel startVoxel);
            grid.TryGetVoxel(end.ToVector3d(Fixed64.Zero), out Voxel endVoxel);

            // Ensure the start and end voxels are included
            Assert.Equal(startVoxel.GlobalIndex, tracedVoxels.First().GlobalIndex);
            Assert.Equal(endVoxel.GlobalIndex, tracedVoxels.Last().GlobalIndex);
        }

        [Fact]
        public void TraceLine_ShouldFullyCoverAllVoxelsBetweenTwoPoints()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d start = new Vector3d(-5, 0, -5);
            Vector3d end = new Vector3d(5, 0, 5);

            var tracedVoxels = GridTracer.TraceLine(start, end, includeEnd: true)
                .SelectMany(set => set.Voxels).ToList();

            Assert.NotEmpty(tracedVoxels);
            Assert.Contains(tracedVoxels, voxel => voxel.WorldPosition == start);
            Assert.Contains(tracedVoxels, voxel => voxel.WorldPosition == end);
        }

        [Fact]
        public void Blocker_ShouldOnlyAffectSnappedBounds()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-50, 0, -50), new Vector3d(50, 0, 50)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            BoundingArea boundingArea = new BoundingArea(new Vector3d(-5.3, 0, -5.3), new Vector3d(5.8, 0, 5.8));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();

            Vector3d snappedMin = GlobalGridManager.FloorToVoxelSize(boundingArea.Min);
            Vector3d snappedMax = GlobalGridManager.CeilToVoxelSize(boundingArea.Max);

            foreach (var coveredVoxels in GridTracer.GetCoveredVoxels(boundingArea.Min, boundingArea.Max))
            {
                foreach (var voxel in coveredVoxels.Voxels)
                {
                    Assert.True(voxel.WorldPosition.x >= snappedMin.x 
                        && voxel.WorldPosition.x <= snappedMax.x, "Voxel X coordinate is out of bounds");
                    Assert.True(voxel.WorldPosition.y >= snappedMin.y 
                        && voxel.WorldPosition.y <= snappedMax.y, "Voxel Y coordinate is out of bounds");
                    Assert.True(voxel.WorldPosition.z >= snappedMin.z 
                        && voxel.WorldPosition.z <= snappedMax.z, "Voxel Z coordinate is out of bounds");
                }
            }
        }
    }
}
