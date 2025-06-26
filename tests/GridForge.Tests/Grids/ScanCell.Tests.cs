using Xunit;
using GridForge.Spatial;
using System.Collections.Generic;
using FixedMathSharp;
using GridForge.Configuration;
using SwiftCollections;

namespace GridForge.Grids.Tests
{
    [Collection("GridForgeCollection")]
    public class ScanCellTests
    {
        [Fact]
        public void GetOccupantsFor_ShouldReturnCorrectList()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)), 
                out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(45, 0, 45);

            var occupant1 = new TestOccupant(position);
            var occupant2 = new TestOccupant(position);

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);

            List<IVoxelOccupant> occupants = new List<IVoxelOccupant>(grid.GetOccupants(occupant1.OccupyingIndex.VoxelIndex));
            Assert.True(occupants.Count > 0);
        }

        [Fact]
        public void GetConditionalOccupants_ShouldFilterCorrectly()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
                out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(41, 0, 41);

            var occupant1 = new TestOccupant(position, 1);
            var occupant2 = new TestOccupant(position);

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);

            List<IVoxelOccupant> filtered = new List<IVoxelOccupant>(
                grid.GetConditionalOccupants(occupant1.OccupyingIndex.VoxelIndex, key => key == 1));

            Assert.Single(filtered);
            Assert.Equal(1, filtered[0].OccupantGroupId);
        }

        [Fact]
        public void GetOccupants_ShouldReturnEmptyList_WhenNoOccupantsPresent()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            List<IVoxelOccupant> occupants = new List<IVoxelOccupant>(grid.GetOccupants(new Vector3d(-25, 0, -25)));

            Assert.Empty(occupants);
        }

        [Fact]
        public void RemoveOccupant_ShouldReturnFalse_WhenOccupantDoesNotExist()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(-10, 0, -10), 
                new Vector3d(10, 0, 10)), 
                out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(10, 0, 10);

            var occupant = new TestOccupant(position);

            bool removed = grid.TryRemoveVoxelOccupant(occupant); // Non-existent occupant

            Assert.False(removed);
        }

        [Fact]
        public void GetConditionalOccupants_ShouldReturnEmptyList_WhenNoMatches()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(2, 0, 2);

            var occupant1 = new TestOccupant(position ,5);
            var occupant2 = new TestOccupant(position, 6);

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);

            List<IVoxelOccupant> filtered = new List<IVoxelOccupant>(
                grid.GetConditionalOccupants(position, key => key == 99)); // No matches

            Assert.Empty(filtered);
        }

        [Fact]
        public void RemoveAllOccupants_ShouldRemoveOnlyMatchingClusterOccupants()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
                out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(49, 0, 49);

            var occupant1 = new TestOccupant(position, 1); // Cluster Key 1
            var occupant2 = new TestOccupant(position, 1); // Cluster Key 1
            var occupant3 = new TestOccupant(position, 2); // Cluster Key 2 (should not be removed)

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);
            grid.TryAddVoxelOccupant(occupant3);

            bool removed1 = grid.TryRemoveVoxelOccupant(occupant1.OccupyingIndex.VoxelIndex, occupant1);
            bool removed2 = grid.TryRemoveVoxelOccupant(occupant2);

            Assert.True(removed1);
            Assert.True(removed2);

            // Verify only ClusterKey 1 occupants are removed, but ClusterKey 2 still exists
            bool hasCluster2Occupants = grid.GetConditionalOccupants(
                position, 
                key => key == 2).IsPopulatedSafe();

            Assert.True(hasCluster2Occupants); // ClusterKey 2 should still be occupied
        }

        [Fact]
        public void RemoveAllOccupants_ShouldMarkIndependentGridAsUnoccupied()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(9, 9, 9), new Vector3d(10, 10, 10)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(9.5, 9.5, 9.5);

            var occupant1 = new TestOccupant(position, 1);
            var occupant2 = new TestOccupant(position, 2);

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);

            bool removed1 = grid.TryRemoveVoxelOccupant(occupant1);
            bool removed2 = grid.TryRemoveVoxelOccupant(occupant2.OccupyingIndex.VoxelIndex, occupant2);

            Assert.True(removed1);
            Assert.True(removed2);

            grid.TryGetScanCell(position, out ScanCell cell);

            Assert.False(cell.IsOccupied); // Should be false after last occupant is removed
        }

        [Fact]
        public void ScanRadius_ShouldFindOccupantsWithinRadius()
        {
            // Arrange
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(-20, 0, -20), 
                new Vector3d(20, 0, 20)), 
                out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d scanCenter = new Vector3d(0, 0, 0);
            Fixed64 scanRadius = (Fixed64)6; // Searching within a radius of 5 units

            var occupant1 = new TestOccupant(new Vector3d(2, 0, 2), 1);  // Within radius
            var occupant2 = new TestOccupant(new Vector3d(4, 0, 4), 1);  // Within radius
            var occupant3 = new TestOccupant(new Vector3d(10, 0, 10), 1); // Outside radius

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);
            grid.TryAddVoxelOccupant(occupant3);

            // Act
            var results = new SwiftList<IVoxelOccupant>(
                ScanManager.ScanRadius(scanCenter, scanRadius));

            // Assert
            Assert.Contains(occupant1, results);
            Assert.Contains(occupant2, results);
            Assert.DoesNotContain(occupant3, results);
        }

        [Fact]
        public void ScanRadius_ShouldFilterByGroupCondition()
        {
            // Arrange
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-20, 0, -20), new Vector3d(20, 0, 20)), out ushort gridIndex);
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d scanCenter = new Vector3d(0, 0, 0);
            Fixed64 scanRadius = (Fixed64)5;

            var occupant1 = new TestOccupant(new Vector3d(1, 0, 1), 1); // Group 1
            var occupant2 = new TestOccupant(new Vector3d(2, 0, 2), 2); // Group 2
            var occupant3 = new TestOccupant(new Vector3d(3, 0, 3), 3); // Group 3 (out of filter)

            grid.TryAddVoxelOccupant(occupant1);
            grid.TryAddVoxelOccupant(occupant2);
            grid.TryAddVoxelOccupant(occupant3);

            // Act
            var filteredResults = new SwiftList<IVoxelOccupant>(ScanManager.ScanRadius(
                scanCenter, 
                scanRadius, groupId => groupId == 1 || groupId == 2));

            // Assert
            Assert.Contains(occupant1, filteredResults);
            Assert.Contains(occupant2, filteredResults);
            // Should be excluded based on group condition
            Assert.DoesNotContain(occupant3, filteredResults); 
        }

    }
}
