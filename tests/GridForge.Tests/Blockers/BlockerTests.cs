using Xunit;
using GridForge.Grids;
using FixedMathSharp;
using System.Linq;
using GridForge.Configuration;
using GridForge.Utility;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GridForge.Blockers.Tests
{
    [Collection("GridForgeCollection")] // Ensures shared GridForge state is reset per run
    public class BlockerTests
    {
        [Fact]
        public void Blocker_ShouldApplyBlockageToNodes()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(30, 0, 30), new Vector3d(35, 0, 35)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(33, 0, 33);
            Node node = grid.TryGetNode(position, out Node n) ? n : null;
            Assert.NotNull(node);
            Assert.False(node.IsBlocked); // Ensure node is initially unblocked

            BoundingArea boundingArea = new BoundingArea(new Vector3d(32, 0, 32), new Vector3d(34, 0, 34));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();

            Assert.True(node.IsBlocked); // Node should now be blocked
        }

        [Fact]
        public void Blocker_ShouldRemoveBlockageFromNodes()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-35.5, 0, -35.5);
            Node node = grid.TryGetNode(position, out Node n) ? n : null;
            Assert.NotNull(node);

            BoundingArea boundingArea = new BoundingArea(new Vector3d(-36, 0, -36), new Vector3d(-35, 0, -35));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();
            Assert.True(node.IsBlocked);

            blocker.RemoveBlockage();
            Assert.False(node.IsBlocked);
        }

        [Fact]
        public void MultipleBlockers_ShouldStackCorrectly()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-39.5, 0, -39.5);
            Node node = grid.TryGetNode(position, out Node n) ? n : null;
            Assert.NotNull(node);

            BoundingArea boundingArea1 = new BoundingArea(new Vector3d(-40, 0, -40), new Vector3d(-39.5, 0, -39.5));
            var blocker1 = new BoundsBlocker(boundingArea1);
            BoundingArea boundingArea2 = new BoundingArea(new Vector3d(-39.5, 0, -39.5), new Vector3d(-39, 0, -39));
            var blocker2 = new BoundsBlocker(boundingArea2);

            blocker1.ApplyBlockage();
            blocker2.ApplyBlockage();

            Assert.True(node.IsBlocked); // Ensure node is blocked
            Assert.True(node.ObstacleCount >= 2);
        }

        [Fact]
        public void RemovingOneBlocker_ShouldNotAffectOthers()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(-40, 0, -40), 
                new Vector3d(-30, 0, -30)), 
                out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-39.5, 0, -39.5);
            Node node = grid.TryGetNode(position, out Node n) ? n : null;
            Assert.NotNull(node);

            BoundingArea boundingArea1 = new BoundingArea(
                new Vector3d(-40, 0, -40), 
                new Vector3d(-39.5, 0, -39.5));
            BoundingArea boundingArea2 = new BoundingArea(
                new Vector3d(-39.5, 0, -39.5), 
                new Vector3d(-39, 0, -39));

            var blocker1 = new BoundsBlocker(boundingArea1);
            var blocker2 = new BoundsBlocker(boundingArea2);

            blocker1.ApplyBlockage();
            blocker2.ApplyBlockage();

            blocker1.RemoveBlockage();
            Assert.True(node.IsBlocked); // Should still be blocked because of blocker2
            Assert.True(node.ObstacleCount > 0);
        }

        [Fact]
        public void DeactivatingBlocker_ShouldPreventApplication()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(
                new Vector3d(-65, 0, -65), 
                new Vector3d(-60, 0, -60)), 
                out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d position = new Vector3d(-60.5, 0, -60.5);
            Node node = grid.TryGetNode(position, out Node n) ? n : null;
            Assert.NotNull(node);

            BoundingArea boundingArea = new BoundingArea(new Vector3d(-61, 0, -61), new Vector3d(-60, 0, -60));
            var blocker = new BoundsBlocker(boundingArea, false);
            blocker.ApplyBlockage();

            Assert.False(node.IsBlocked); // Should not be blocked due to deactivation
        }

        [Fact]
        public void BoundsBlocker_ShouldAffectCorrectNodes()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            var blockArea = new BoundingArea(new Vector3d(-40, 0, -40), new Vector3d(-39, 0, -39));
            var boundsBlocker = new BoundsBlocker(blockArea);
            boundsBlocker.ApplyBlockage();

            // Get all blocked nodes using GridTracer
            var blockedNodes = GridTracer.GetCoveredNodes(blockArea.Min, blockArea.Max)
                                         .SelectMany(covered => covered.Nodes) // Flatten the grouped nodes
                                         .ToList();

            Assert.NotEmpty(blockedNodes);
            Assert.All(blockedNodes, node => Assert.True(node.IsBlocked));
        }

        [Fact]
        public void Blocker_ShouldCorrectlyAffectEdgeNodes()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            // Blocker placed at the grid's edge
            BoundingArea boundingArea = new BoundingArea(new Vector3d(-30, 0, -30), new Vector3d(-29.5, 0, -29.5));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();

            var blockedNodes = GridTracer.GetCoveredNodes(boundingArea.Min, boundingArea.Max)
                                         .SelectMany(covered => covered.Nodes)
                                         .ToList();

            Assert.NotEmpty(blockedNodes);
            Assert.All(blockedNodes, node => Assert.True(node.IsBlocked));
        }

        [Fact]
        public void Blocker_ShouldApplyAcrossMultipleGrids()
        {
            GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(-40, 0, -40),
                new Vector3d(-30, 0, -30)), 
                out ushort grid1);
            GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(-30, 0, -30), 
                new Vector3d(-20, 0, -20)), 
                out ushort grid2);

            BoundingArea boundingArea = new BoundingArea(new Vector3d(-31, 0, -31), new Vector3d(-29, 0, -29));
            var blocker = new BoundsBlocker(boundingArea);
            blocker.ApplyBlockage();

            var blockedNodes = GridTracer.GetCoveredNodes(boundingArea.Min, boundingArea.Max)
                                         .SelectMany(covered => covered.Nodes)
                                         .ToList();

            Assert.NotEmpty(blockedNodes);
            Assert.All(blockedNodes, node => Assert.True(node.IsBlocked));
        }

        [Fact]
        public void MultipleBlockers_ShouldNotCausePerformanceIssues()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-50, 0, -50), new Vector3d(50, 0, 50)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            List<BoundsBlocker> blockers = new List<BoundsBlocker>();

            for (int i = 0; i < 1000; i++)
            {
                Vector3d min = new Vector3d(-50 + i, 0, -50 + i);
                Vector3d max = new Vector3d(-49 + i, 0, -49 + i);
                var blocker = new BoundsBlocker(new BoundingArea(min, max));
                blockers.Add(blocker);
                blocker.ApplyBlockage();
            }

            // Ensure most blockers applied correctly
            Assert.True(grid.ObstacleCount > 900, $"Because the grid's ObstacleCount {grid.ObstacleCount} is not > 900"); 
        }

        [Fact]
        public void Blockers_ShouldBeThreadSafe()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-50, 0, -50), new Vector3d(50, 0, 50)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Parallel.For(0, 100, i =>
            {
                Vector3d min = new Vector3d(-50 + i, 0, -50 + i);
                Vector3d max = new Vector3d(-49 + i, 0, -49 + i);
                var blocker = new BoundsBlocker(new BoundingArea(min, max));
                blocker.ApplyBlockage();
            });

            Assert.True(grid.ObstacleCount > 90); // Ensure most blockers applied correctly
        }
    }
}
