using Xunit;
using FixedMathSharp;
using GridForge.Configuration;
using System.Linq;
using System;

namespace GridForge.Grids.Tests
{
    [Collection("GridForgeCollection")]
    public class VoxelTests
    {
        [Fact]
        public void Voxel_ShouldInitializeCorrectly()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d testPosition = new Vector3d(10, 0, 10);

            bool found = grid.TryGetVoxel(testPosition, out Voxel voxel);

            Assert.True(found);
            Assert.NotNull(voxel);
            Assert.Equal(testPosition, voxel.WorldPosition);
            Assert.False(voxel.IsOccupied);
            Assert.False(voxel.IsBlocked);
        }

        [Fact]
        public void Voxel_ShouldHandleOccupantsCorrectly()
        {
            var config = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(6, 0, 6);
            TestOccupant occupant = new TestOccupant(position);
            grid.TryAddVoxelOccupant(occupant);

            int previousTicket = -1;
            foreach (var kvp in occupant.OccupyingIndexMap)
            {
                grid.TryGetVoxel(kvp.Key.VoxelIndex, out Voxel occupantVoxel);

                Assert.True(occupantVoxel.IsOccupied);
                Assert.True(grid.TryGetVoxelOccupant(occupantVoxel, kvp.Value, out _));
                previousTicket = kvp.Value;
            }

            grid.TryRemoveVoxelOccupant(occupant);
            Assert.False(grid.TryGetVoxelOccupant(position, previousTicket, out _));
        }

        [Fact]
        public void Voxel_ShouldCorrectlyBlockAndUnblock()
        {
            var config = new GridConfiguration(new Vector3d(35, 1, 35), new Vector3d(40, 1, 40));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
            grid.TryGetVoxel(new Vector3d(36, 1, 36), out Voxel voxel);

            int spawnHash = Guid.NewGuid().GetHashCode();

            grid.TryAddObstacle(voxel, spawnHash);
            Assert.True(voxel.IsBlocked);

            grid.TryRemoveObstacle(voxel, spawnHash);
            Assert.False(voxel.IsBlocked);
        }

        [Fact]
        public void Voxel_ShouldCorrectlyHandlePartitions()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
            grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

            var partition = new TestPartition();
            voxel.TryAddPartition(partition);

            Assert.True(voxel.TryGetPartition<TestPartition>(out _));

            voxel.TryGetPartition(out TestPartition voxelPartition);

            Assert.Equal(partition, voxelPartition);

            voxel.TryRemovePartition<TestPartition>();
            Assert.False(voxel.TryGetPartition<TestPartition>(out _));
        }

        [Fact]
        public void Voxel_ShouldRespectBoundaryConditions()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            grid.TryGetVoxel(new Vector3d(-10, 0, 0), out Voxel westVoxel);
            grid.TryGetVoxel(new Vector3d(10, 0, 0), out Voxel eastVoxel);

            Assert.True(grid.IsFacingBoundaryDirection(westVoxel.Index, SpatialDirection.West));
            Assert.True(grid.IsFacingBoundaryDirection(eastVoxel.Index, SpatialDirection.East));
        }

        [Fact]
        public void Voxel_ShouldNotIncrementObstacleCountBeyondLimit()
        {
            var config = new GridConfiguration(new Vector3d(36, 1, 36), new Vector3d(40, 1, 40));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
            grid.TryGetVoxel(new Vector3d(37, 1, 37), out Voxel voxel);

            int spawnHash = Guid.NewGuid().GetHashCode();

            grid.TryAddObstacle(voxel, spawnHash);
            grid.TryAddObstacle(voxel, spawnHash); // Attempt to add twice

            Assert.True(voxel.IsBlocked);
            Assert.Equal(1, voxel.ObstacleCount); // Should not increase beyond 1

            grid.TryRemoveObstacle(voxel, spawnHash);
            Assert.False(voxel.IsBlocked);
        }

        [Fact]
        public void Voxel_ShouldAllowMultipleOccupants()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(10, 0, 10);
            TestOccupant occupant1 = new TestOccupant(position);
            TestOccupant occupant2 = new TestOccupant(position);

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);

            grid.TryGetVoxel(position, out Voxel targetVoxel);

            Assert.True(targetVoxel.IsOccupied);
            Assert.True(targetVoxel.OccupantCount > 0);

            occupant1.OccupyingIndexMap.TryGetValue(targetVoxel.GlobalIndex, out int ticket1);
            occupant2.OccupyingIndexMap.TryGetValue(targetVoxel.GlobalIndex, out int ticket2);

            grid.TryRemoveVoxelOccupant(targetVoxel, occupant1);
            Assert.False(grid.TryGetVoxelOccupant(targetVoxel, ticket1, out _));
            // Still occupied by occupant2
            Assert.True(grid.TryGetVoxelOccupant(targetVoxel, ticket2, out _)); 

            grid.TryRemoveVoxelOccupant(targetVoxel, occupant2);
            // Now fully unoccupied
            Assert.False(grid.TryGetVoxelOccupant(targetVoxel, ticket2, out _));
        }

        [Fact]
        public void Voxel_ShouldNotChangeStateIfRemovingNonExistentOccupant()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
 
            Vector3d position = new Vector3d(0, 0, 0);
            TestOccupant occupant = new TestOccupant(position);

            grid.TryGetVoxel(position, out Voxel voxel);

            var previousState = voxel.IsOccupied;

            grid.TryRemoveVoxelOccupant(voxel, occupant); // Removing non-existent occupant

            Assert.True(voxel.IsOccupied == previousState); // Should remain unchanged
        }

        [Fact]
        public void Voxel_ShouldRetrieveOccupantsByType()
        {
            var config = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-27, 0, -27);
            TestOccupant occupant1 = new TestOccupant(position);
            TestOccupant occupant2 = new TestOccupant(position);

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);

            var occupants = grid.GetVoxelOccupantsByType<TestOccupant>(position);

            Assert.Equal(2, occupants.Count());
            Assert.Contains(occupant1, occupants);
            Assert.Contains(occupant2, occupants);
        }

    }
}
