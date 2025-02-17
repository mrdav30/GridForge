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
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(0, 0, 0);

            var occupant1 = new TestOccupant(position);
            var occupant2 = new TestOccupant(position);

            grid.TryAddNodeOccupant(occupant1);
            grid.TryAddNodeOccupant(occupant2);

            List<INodeOccupant> occupants = new List<INodeOccupant>(grid.GetOccupants(occupant1.GridCoordinates.NodeCoordinates));
            Assert.True(occupants.Count > 0);
        }

        [Fact]
        public void GetConditionalOccupants_ShouldFilterCorrectly()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(2, 0, 2);

            var occupant1 = new TestOccupant(position, 1);
            var occupant2 = new TestOccupant(position);

            grid.TryAddNodeOccupant(position, occupant1);
            grid.TryAddNodeOccupant(position, occupant2);

            ;

            List<INodeOccupant> filtered = new List<INodeOccupant>(
                grid.GetConditionalOccupants(occupant1.GridCoordinates.NodeCoordinates, key => key == 1));

            Assert.Single(filtered);
            Assert.Equal(1, filtered[0].OccupantGroupId);
        }

        [Fact]
        public void GetOccupants_ShouldReturnEmptyList_WhenNoOccupantsPresent()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            List<INodeOccupant> occupants = new List<INodeOccupant>(grid.GetOccupants(new Vector3d(-25, 0, -25)));

            Assert.Empty(occupants);
        }

        [Fact]
        public void RemoveOccupant_ShouldReturnFalse_WhenOccupantDoesNotExist()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(-10, 0, -10), 
                new Vector3d(10, 0, 10)), 
                out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(10, 0, 10);

            var occupant = new TestOccupant(position);

            bool removed = grid.TryRemoveNodeOccupant(occupant); // Non-existent occupant

            Assert.False(removed);
        }

        [Fact]
        public void GetConditionalOccupants_ShouldReturnEmptyList_WhenNoMatches()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(2, 0, 2);

            var occupant1 = new TestOccupant(position ,5);
            var occupant2 = new TestOccupant(position, 6);

            grid.TryAddNodeOccupant(occupant1);
            grid.TryAddNodeOccupant(occupant2);

            List<INodeOccupant> filtered = new List<INodeOccupant>(
                grid.GetConditionalOccupants(position, key => key == 99)); // No matches

            Assert.Empty(filtered);
        }

        [Fact]
        public void RemoveAllOccupants_ShouldRemoveOnlyMatchingClusterOccupants()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(3, 0, 3);

            var occupant1 = new TestOccupant(position, 1); // Cluster Key 1
            var occupant2 = new TestOccupant(position, 1); // Cluster Key 1
            var occupant3 = new TestOccupant(position, 2); // Cluster Key 2 (should not be removed)

            grid.TryAddNodeOccupant(position, occupant1);
            grid.TryAddNodeOccupant(occupant2);
            grid.TryAddNodeOccupant(position, occupant3);

            bool removed1 = grid.TryRemoveNodeOccupant(occupant1.GridCoordinates.NodeCoordinates, occupant1);
            bool removed2 = grid.TryRemoveNodeOccupant(occupant2);

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
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(9.5, 9.5, 9.5);

            var occupant1 = new TestOccupant(position, 1);
            var occupant2 = new TestOccupant(position, 2);

            grid.TryAddNodeOccupant(position, occupant1);
            grid.TryAddNodeOccupant(occupant2);

            bool removed1 = grid.TryRemoveNodeOccupant(occupant1);
            bool removed2 = grid.TryRemoveNodeOccupant(occupant2.GridCoordinates.NodeCoordinates, occupant2);

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
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d scanCenter = new Vector3d(0, 0, 0);
            Fixed64 scanRadius = (Fixed64)6; // Searching within a radius of 5 units

            var occupant1 = new TestOccupant(new Vector3d(2, 0, 2), 1);  // Within radius
            var occupant2 = new TestOccupant(new Vector3d(4, 0, 4), 1);  // Within radius
            var occupant3 = new TestOccupant(new Vector3d(10, 0, 10), 1); // Outside radius

            grid.TryAddNodeOccupant(occupant1);
            grid.TryAddNodeOccupant(occupant2);
            grid.TryAddNodeOccupant(occupant3);

            // Act
            var results = new SwiftList<INodeOccupant>(
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
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d scanCenter = new Vector3d(0, 0, 0);
            Fixed64 scanRadius = (Fixed64)5;

            var occupant1 = new TestOccupant(new Vector3d(1, 0, 1), 1); // Group 1
            var occupant2 = new TestOccupant(new Vector3d(2, 0, 2), 2); // Group 2
            var occupant3 = new TestOccupant(new Vector3d(3, 0, 3), 3); // Group 3 (out of filter)

            grid.TryAddNodeOccupant(occupant1);
            grid.TryAddNodeOccupant(occupant2);
            grid.TryAddNodeOccupant(occupant3);

            // Act
            var filteredResults = new SwiftList<INodeOccupant>(ScanManager.ScanRadius(
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
