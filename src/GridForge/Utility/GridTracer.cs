using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using SwiftCollections.Pool;
using System.Collections.Generic;

namespace GridForge.Utility
{
    /// <summary>
    /// Used to group query results on a grid to node level
    /// </summary>
    public struct GridNodeSet
    {
        /// <summary>
        /// The grid containing the nodes from the resulting query
        /// </summary>
        public Grid Grid;

        /// <summary>
        /// A list of nodes that match the provided query
        /// </summary>
        public SwiftList<Node> Nodes;
    }

    /// <summary>
    /// Provides utilities for tracing lines or bounding areas in a grid, aligning them to grid nodes.
    /// Uses fixed-point calculations to ensure deterministic and accurate grid traversal.
    /// </summary>
    public static class GridTracer
    {
        /// <summary>
        /// Traces a 3D line between two points in the grid.
        /// The traced points are returned as grid nodes.
        /// </summary>
        /// <remarks>
        /// Uses a fractional step algorithm inspired by Bresenham’s line algorithm.
        /// This implementation leverages fixed-point math to maintain precision across a deterministic grid.
        /// </remarks>
        /// <param name="start">Starting position in world space.</param>
        /// <param name="end">Ending position in world space.</param>
        /// <param name="padding">Value applied to the start/end positions before snapping.</param>
        /// <param name="includeEnd">Whether to include the end node in the traced line.</param>
        /// <returns>A collection of <see cref="GridNodeSet"/> objects representing the traced path.</returns>
        public static IEnumerable<GridNodeSet> TraceLine(
            Vector3d start,
            Vector3d end,
            double padding = 0d,
            bool includeEnd = true)
        {
            SwiftDictionary<Grid, SwiftList<Node>> gridNodeMapping = new SwiftDictionary<Grid, SwiftList<Node>>();
            SwiftHashSet<int> nodeRedundancyCheck = SwiftHashSetPool<int>.Shared.Rent();

            (Vector3d snappedStart, Vector3d snappedEnd) = 
                GlobalGridManager.SnapBoundsToNodeSize(start, end, padding);

            Vector3d diff = snappedEnd - snappedStart;
            Vector3d delta = Vector3d.Abs(diff);

            // Determine the total number of points to trace along the longest axis
            Fixed64 steps = FixedMath.Ceiling(FixedMath.Max(FixedMath.Max(delta.x, delta.y), delta.z));

            // Calculate the interval (step size) for each axis
            Fixed64 stepX = diff.x / (steps + Fixed64.One);
            Fixed64 stepY = diff.y / (steps + Fixed64.One);
            Fixed64 stepZ = diff.z / (steps + Fixed64.One);

            // Get affected spatial cells along the traced line
            foreach (int cellIndex in GlobalGridManager.GetSpatialGridCells(snappedStart, snappedEnd))
            {
                if (!GlobalGridManager.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                    continue;

                foreach (ushort gridIndex in gridList)
                {
                    if (!GlobalGridManager.ActiveGrids.IsAllocated(gridIndex))
                        continue;

                    Grid currentGrid = GlobalGridManager.ActiveGrids[gridIndex];
                    // If grid has already been processed, skip it
                    if (gridNodeMapping.ContainsKey(currentGrid))
                        continue;

                    SwiftList<Node> nodeList = SwiftListPool<Node>.Shared.Rent();
                    gridNodeMapping.Add(currentGrid, nodeList);

                    // Traverse the grid along the computed line
                    for (Fixed64 i = Fixed64.Zero; i <= steps; i += Fixed64.One)
                    {
                        Vector3d tracePos = GlobalGridManager.FloorToNodeSize(
                            new Vector3d(start.x + stepX * i, start.y + stepY * i, start.z + stepZ * i));

                        if (!currentGrid.TryGetNode(tracePos, out Node node) || !nodeRedundancyCheck.Add(node.SpawnToken))
                            continue;

                        nodeList.Add(node);
                    }
                }
            }

            // Include end node if needed
            if (includeEnd && GlobalGridManager.TryGetGridAndNode(end, out Grid endGrid, out Node endNode))
            {
                if (!gridNodeMapping.TryGetValue(endGrid, out SwiftList<Node> nodeList))
                {
                    nodeList = SwiftListPool<Node>.Shared.Rent();
                    gridNodeMapping.Add(endGrid, nodeList);
                }

                if (nodeRedundancyCheck.Add(endNode.SpawnToken))
                    nodeList.Add(endNode);
            }

            // Yield grouped results
            foreach (KeyValuePair<Grid, SwiftList<Node>> kvp in gridNodeMapping)
            {
                yield return new GridNodeSet
                {
                    Grid = kvp.Key,
                    Nodes = kvp.Value
                };

                SwiftListPool<Node>.Shared.Release(kvp.Value);
            }

            SwiftHashSetPool<int>.Shared.Release(nodeRedundancyCheck);
        }

        /// <summary>
        /// Traces a 2D line between two points, snapping them to grid coordinates.
        /// </summary>
        /// <remarks>
        /// This method projects the 2D line onto the X-Y plane and follows the closest grid-aligned path.
        /// </remarks>
        /// <param name="start">Starting 2D position.</param>
        /// <param name="end">Ending 2D position.</param>
        /// <param name="padding">Value applied to the start/end positions before snapping.</param>
        /// <param name="includeEnd">Whether to include the end node.</param>
        /// <returns>A collection of <see cref="GridNodeSet"/> objects representing the traced path.</returns>
        public static IEnumerable<GridNodeSet> TraceLine(
            Vector2d start,
            Vector2d end,
            double padding = 0d,
            bool includeEnd = true)
        {
            // Convert 2D positions to 3D (assuming Y = 0 for a flat projection)
            Vector3d start3D = start.ToVector3d(Fixed64.Zero);
            Vector3d end3D = end.ToVector3d(Fixed64.Zero);

            return TraceLine(start3D, end3D, padding, includeEnd);
        }

        /// <summary>
        /// Retrieves all grid nodes covered by the given bounding area.
        /// </summary>
        /// <param name="boundsMin">The minimum corner of the bounding area.</param>
        /// <param name="boundsMax">The maximum corner of the bounding area.</param>
        /// <param name="padding">Value applied to the min/max bounds before snapping.</param>
        public static IEnumerable<GridNodeSet> GetCoveredNodes(
            Vector3d boundsMin, 
            Vector3d boundsMax,
            double padding = 0d)
        {
            SwiftDictionary<Grid, SwiftList<Node>> gridNodeMapping = new SwiftDictionary<Grid, SwiftList<Node>>();
            SwiftHashSet<int> nodeRedundancyCheck = SwiftHashSetPool<int>.Shared.Rent();

            (Vector3d snappedMin, Vector3d snappedMax) = 
                GlobalGridManager.SnapBoundsToNodeSize(boundsMin, boundsMax, padding);

            foreach (int cellIndex in GlobalGridManager.GetSpatialGridCells(snappedMin, snappedMax))
            {
                if (!GlobalGridManager.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                    continue;

                foreach (ushort gridIndex in gridList)
                {
                    if (!GlobalGridManager.ActiveGrids.IsAllocated(gridIndex))
                        continue;

                    Grid currentGrid = GlobalGridManager.ActiveGrids[gridIndex];

                    // If grid has already been processed, skip it
                    if (gridNodeMapping.ContainsKey(currentGrid))
                        continue;

                    SwiftList<Node> nodeList = SwiftListPool<Node>.Shared.Rent();
                    gridNodeMapping.Add(currentGrid, nodeList);

                    Fixed64 resolution = GlobalGridManager.NodeSize;
                    for (Fixed64 x = snappedMin.x; x <= snappedMax.x; x += resolution)
                    {
                        for (Fixed64 y = snappedMin.y; y <= snappedMax.y; y += resolution)
                        {
                            for (Fixed64 z = snappedMin.z; z <= snappedMax.z; z += resolution)
                            {
                                Vector3d position = new Vector3d(x, y, z);
                                if (!currentGrid.TryGetNode(position, out Node node) || !nodeRedundancyCheck.Add(node.SpawnToken))
                                    continue;

                                nodeList.Add(node);
                            }
                        }
                    }
                }
            }

            foreach (var kvp in gridNodeMapping)
            {
                yield return new GridNodeSet
                {
                    Grid = kvp.Key,
                    Nodes = kvp.Value
                };

                SwiftListPool<Node>.Shared.Release(kvp.Value);
            }

            SwiftHashSetPool<int>.Shared.Release(nodeRedundancyCheck);
        }

        /// <summary>
        /// Retrieves all scan cells within the given bounding area across relevant grids.
        /// </summary>
        /// <param name="boundsMin">The minimum corner of the bounding area.</param>
        /// <param name="boundsMax">The maximum corner of the bounding area.</param>
        /// <param name="padding">Value applied to the min/max bounds before snapping.</param>
        /// <returns>An enumerable of covered scan cells grouped by grid.</returns>
        public static IEnumerable<ScanCell> GetCoveredScanCells(
            Vector3d boundsMin, 
            Vector3d boundsMax,
            double padding = 0d)
        {
            SwiftList<ScanCell> scanCells = SwiftListPool<ScanCell>.Shared.Rent();
            SwiftHashSet<ushort> processedGrids = SwiftHashSetPool<ushort>.Shared.Rent();
            SwiftHashSet<int> nodeRedundancyCheck = SwiftHashSetPool<int>.Shared.Rent();

            (Vector3d snappedMin, Vector3d snappedMax) = 
                GlobalGridManager.SnapBoundsToNodeSize(boundsMin, boundsMax, padding);

            foreach (int cellIndex in GlobalGridManager.GetSpatialGridCells(boundsMin, boundsMax))
            {
                if (!GlobalGridManager.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                    continue;

                foreach (ushort gridIndex in gridList)
                {
                    if (!GlobalGridManager.ActiveGrids.IsAllocated(gridIndex) || !processedGrids.Add(gridIndex))
                        continue;

                    Grid currentGrid = GlobalGridManager.ActiveGrids[gridIndex];

                    // Convert snapped min/max to node indices
                    (int xMin, int yMin, int zMin) = currentGrid.SnapToScanCell(snappedMin);

                    (int xMax, int yMax, int zMax) = currentGrid.SnapToScanCell(snappedMax);

                    // Iterate through scan cell indices and collect valid scan cells
                    for (int x = xMin; x <= xMax; x++)
                    {
                        for (int y = yMin; y <= yMax; y++)
                        {
                            for (int z = zMin; z <= zMax; z++)
                            {
                                if (!currentGrid.TryGetScanCell(GlobalGridManager.GetSpawnHash(x, y, z), out ScanCell scanCell)
                                    || !nodeRedundancyCheck.Add(scanCell.SpawnToken))
                                {
                                    continue;
                                }

                                scanCells.Add(scanCell);
                            }
                        }
                    }
                }
            }

            foreach (ScanCell scanCell in scanCells)
                yield return scanCell;

            SwiftListPool<ScanCell>.Shared.Release(scanCells);
            SwiftHashSetPool<ushort>.Shared.Release(processedGrids);
            SwiftHashSetPool<int>.Shared.Release(nodeRedundancyCheck);
        }
    }
}

