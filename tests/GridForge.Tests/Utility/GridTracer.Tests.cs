﻿using Xunit;
using FixedMathSharp;
using GridForge.Configuration;
using System.Collections.Generic;
using System.Linq;
using GridForge.Utility;

namespace GridForge.Grids.Tests
{
    [Collection("GridForgeCollection")]
    public class GridTracerTests
    {
        [Fact]
        public void TraceLine_ShouldReturnCorrectNodes()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-50, -1, -50), new Vector3d(50, 1, 50)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d start = new Vector3d(5, 0.5, 5);
            Vector3d end = new Vector3d(45.28, 1, 18.31);

            List<Node> tracedNodes = new List<Node>();

            foreach (var gridNodeSet in GridTracer.TraceLine(start, end, includeEnd: true))
            {
                if (gridNodeSet.Grid.GlobalIndex == gridIndex)
                    tracedNodes.AddRange(gridNodeSet.Nodes);
            }

            Assert.NotEmpty(tracedNodes);

            grid.TryGetNode(start, out Node startNode);
            grid.TryGetNode(end, out Node endNode);

            // Ensure that the first and last node correspond to the start and end positions
            Assert.Equal(startNode.GlobalCoordinates, tracedNodes.First().GlobalCoordinates);
            Assert.Equal(endNode.GlobalCoordinates, tracedNodes.Last().GlobalCoordinates);
        }

        [Fact]
        public void TraceLine_ShouldNotIncludeEndWhenSpecified()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector3d start = new Vector3d(-5, 0, -5);
            Vector3d end = new Vector3d(5, 0, 5);

            List<Node> tracedNodes = new List<Node>();

            foreach (var gridNodeSet in GridTracer.TraceLine(start, end, includeEnd: false))
            {
                if (gridNodeSet.Grid.GlobalIndex == gridIndex)
                    tracedNodes.AddRange(gridNodeSet.Nodes);
            }

            Assert.NotEmpty(tracedNodes);

            grid.TryGetNode(end, out Node endNode);

            // Ensure that the last node is not the end node
            Assert.NotEqual(endNode.GlobalCoordinates, tracedNodes.Last().GlobalCoordinates);
        }

        [Fact]
        public void TraceLine2D_ShouldReturnCorrectNodes()
        {
            GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
            Grid grid = GlobalGridManager.ActiveGrids[gridIndex];

            Vector2d start = new Vector2d(-5, -5);
            Vector2d end = new Vector2d(5, 5);

            List<Node> tracedNodes = new List<Node>();

            foreach (var gridNodeSet in GridTracer.TraceLine(start, end, includeEnd: true))
            {
                if (gridNodeSet.Grid.GlobalIndex == gridIndex)
                    tracedNodes.AddRange(gridNodeSet.Nodes);
            }

            Assert.NotEmpty(tracedNodes);

            grid.TryGetNode(start.ToVector3d(Fixed64.Zero), out Node startNode);
            grid.TryGetNode(end.ToVector3d(Fixed64.Zero), out Node endNode);

            // Ensure the start and end nodes are included
            Assert.Equal(startNode.GlobalCoordinates, tracedNodes.First().GlobalCoordinates);
            Assert.Equal(endNode.GlobalCoordinates, tracedNodes.Last().GlobalCoordinates);
        }
    }
}
