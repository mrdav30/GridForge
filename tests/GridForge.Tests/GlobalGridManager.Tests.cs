using Xunit;
using GridForge.Grids;
using FixedMathSharp;
using System;
using System.Collections.Generic;
using GridForge.Configuration;

namespace GridForge.Tests
{
    public class GlobalGridManagerTests
    {
        public GlobalGridManagerTests()
        {
            if (!GlobalGridManager.IsActive)
                GlobalGridManager.Setup();
            else
                GlobalGridManager.Reset(); // Ensure fresh state before each test
        }

        [Fact]
        public void Setup_ShouldInitializeCollections()
        {
            Assert.NotNull(GlobalGridManager.ActiveGrids);
            Assert.NotNull(GlobalGridManager.SpatialHash);
        }

        [Fact]
        public void Reset_ShouldClearAllGrids()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.AddGrid(config);

            GlobalGridManager.Reset();

            Assert.Empty(GlobalGridManager.ActiveGrids);
            Assert.Empty(GlobalGridManager.SpatialHash);
        }

        [Fact]
        public void AddGrid_ShouldSucceedWithValidConfiguration()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            bool result = GlobalGridManager.AddGrid(config);

            Assert.True(result);
            Assert.Single(GlobalGridManager.ActiveGrids);
        }

        [Fact]
        public void GridConfiguration_ShouldCorrectInvalidBounds()
        {
            var invalidConfig = new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(-10, 0, -10));
            bool result = GlobalGridManager.AddGrid(invalidConfig);

            Assert.True(result);
        }

        [Fact]
        public void GetGrid_ShouldReturnCorrectGrid()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.AddGrid(config);

            bool found = GlobalGridManager.GetGrid(new Vector3d(0, 0, 0), out Grid grid);

            Assert.True(found);
            Assert.NotNull(grid);
        }

        [Fact]
        public void GetGridAndNode_ShouldReturnCorrectNode()
        {
            var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            GlobalGridManager.AddGrid(config);

            bool found = GlobalGridManager.GetGridAndNode(new Vector3d(0, 0, 0), out Grid grid, out Node node);

            Assert.True(found);
            Assert.NotNull(grid);
            Assert.NotNull(node);
        }

        [Fact]
        public void FindOverlappingGrids_ShouldReturnCorrectResults()
        {
            var config1 = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
            var config2 = new GridConfiguration(new Vector3d(5, 0, 5), new Vector3d(15, 0, 15));

            GlobalGridManager.AddGrid(config1);
            GlobalGridManager.AddGrid(config2);

            Grid targetGrid = GlobalGridManager.ActiveGrids[0];
            IEnumerable<Grid> overlaps = GlobalGridManager.FindOverlappingGrids(targetGrid);

            Assert.Single(overlaps);
        }
    }
}
