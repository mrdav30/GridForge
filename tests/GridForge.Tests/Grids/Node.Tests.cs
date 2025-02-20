using Xunit;
using FixedMathSharp;
using GridForge.Configuration;
using System.Linq;
using System;

namespace GridForge.Grids.Tests
{
    [Collection("GridForgeCollection")]
    public class NodeTests
    {
        [Fact]
        public void Node_ShouldInitializeCorrectly()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d testPosition = new Vector3d(10, 0, 10);

            bool found = grid.TryGetNode(testPosition, out Node node);

            Assert.True(found);
            Assert.NotNull(node);
            Assert.Equal(testPosition, node.WorldPosition);
            Assert.False(node.IsOccupied);
            Assert.False(node.IsBlocked);
        }

        [Fact]
        public void Node_ShouldHandleOccupantsCorrectly()
        {
            var config = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(6, 0, 6);
            TestOccupant occupant = new TestOccupant(position);
            grid.TryAddNodeOccupant(occupant);

            grid.TryGetNode(occupant.GridCoordinates.NodeCoordinates, out Node occupantNode);

            Assert.True(occupantNode.IsOccupied);
            Assert.True(grid.TryGetNodeOccupant(occupantNode, occupant.OccupantTicket, out _));

            int previousTicket = occupant.OccupantTicket;
            grid.TryRemoveNodeOccupant(occupantNode, occupant);
            Assert.False(grid.TryGetNodeOccupant(occupantNode, previousTicket, out _));
        }

        [Fact]
        public void Node_ShouldCorrectlyBlockAndUnblock()
        {
            var config = new GridConfiguration(new Vector3d(35, 1, 35), new Vector3d(40, 1, 40));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];
            grid.TryGetNode(new Vector3d(36, 1, 36), out Node node);

            int spawnHash = Guid.NewGuid().GetHashCode();

            grid.TryAddObstacle(node, spawnHash);
            Assert.True(node.IsBlocked);

            grid.TryRemoveObstacle(node, spawnHash);
            Assert.False(node.IsBlocked);
        }

        [Fact]
        public void Node_ShouldCorrectlyHandlePartitions()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];
            grid.TryGetNode(new Vector3d(0, 0, 0), out Node node);

            var partition = new TestPartition();
            node.TryAddPartition(partition);

            Assert.True(node.TryGetPartition<TestPartition>(out _));

            node.TryGetPartition(out TestPartition nodePartition);

            Assert.Equal(partition, nodePartition);

            node.TryRemovePartition<TestPartition>();
            Assert.False(node.TryGetPartition<TestPartition>(out _));
        }

        [Fact]
        public void Node_ShouldRespectBoundaryConditions()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            grid.TryGetNode(new Vector3d(-10, 0, 0), out Node westNode);
            grid.TryGetNode(new Vector3d(10, 0, 0), out Node eastNode);

            Assert.True(grid.IsFacingBoundaryDirection(westNode.LocalCoordinates, LinearDirection.West));
            Assert.True(grid.IsFacingBoundaryDirection(eastNode.LocalCoordinates, LinearDirection.East));
        }

        [Fact]
        public void Node_ShouldNotIncrementObstacleCountBeyondLimit()
        {
            var config = new GridConfiguration(new Vector3d(36, 1, 36), new Vector3d(40, 1, 40));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];
            grid.TryGetNode(new Vector3d(37, 1, 37), out Node node);

            int spawnHash = Guid.NewGuid().GetHashCode();

            grid.TryAddObstacle(node, spawnHash);
            grid.TryAddObstacle(node, spawnHash); // Attempt to add twice

            Assert.True(node.IsBlocked);
            Assert.Equal(1, node.ObstacleCount); // Should not increase beyond 1

            grid.TryRemoveObstacle(node, spawnHash);
            Assert.False(node.IsBlocked);
        }

        [Fact]
        public void Node_ShouldAllowMultipleOccupants()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(10, 0, 10);
            TestOccupant occupant1 = new TestOccupant(position);
            TestOccupant occupant2 = new TestOccupant(position);

            grid.TryAddNodeOccupant(occupant1);
            grid.TryAddNodeOccupant(position, occupant2);

            grid.TryGetNode(position, out Node targetNode);

            Assert.True(targetNode.IsOccupied);
            Assert.True(targetNode.OccupantCount > 0);

            grid.TryRemoveNodeOccupant(targetNode, occupant1);
            Assert.False(grid.TryGetNodeOccupant(targetNode, occupant1.OccupantTicket, out _));
            // Still occupied by occupant2
            Assert.True(grid.TryGetNodeOccupant(targetNode, occupant2.OccupantTicket, out _)); 

            grid.TryRemoveNodeOccupant(targetNode, occupant2);
            // Now fully unoccupied
            Assert.False(grid.TryGetNodeOccupant(targetNode, occupant2.OccupantTicket, out _));
        }

        [Fact]
        public void Node_ShouldNotChangeStateIfRemovingNonExistentOccupant()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];
 
            Vector3d position = new Vector3d(0, 0, 0);
            TestOccupant occupant = new TestOccupant(position);

            grid.TryGetNode(position, out Node node);

            var previousState = node.IsOccupied;

            grid.TryRemoveNodeOccupant(node, occupant); // Removing non-existent occupant

            Assert.True(node.IsOccupied == previousState); // Should remain unchanged
        }

        [Fact]
        public void Node_ShouldRetrieveOccupantsByType()
        {
            var config = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20));
            GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-27, 0, -27);
            TestOccupant occupant1 = new TestOccupant(position);
            TestOccupant occupant2 = new TestOccupant(position);

            grid.TryAddNodeOccupant(position, occupant1);
            grid.TryAddNodeOccupant(occupant2);

            var occupants = grid.GetNodeOccupantsByType<TestOccupant>(position);

            Assert.Equal(2, occupants.Count());
            Assert.Contains(occupant1, occupants);
            Assert.Contains(occupant2, occupants);
        }

    }
}
