using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System.Collections.Generic;

namespace GridForge.Utility.Debugging
{
    /// <summary>
    /// Provides utilities for tracing lines through a grid, aligning them to grid nodes.
    /// Uses fixed-point calculations to ensure deterministic and accurate grid traversal.
    /// </summary>
    public static class GridLineTracer
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
        /// <param name="includeEnd">Whether to include the end node in the traced line.</param>
        /// <returns>A collection of <see cref="Node"/> objects representing the traced path.</returns>
        public static IEnumerable<Node> TraceLine(
            Vector3d start,
            Vector3d end,
            bool includeEnd = true)
        {
            SwiftHashSet<int> visitedCoordinates = new SwiftHashSet<int>();

            // Compute directional differences
            Vector3d diff = end - start;
            Vector3d delta = Vector3d.Abs(diff);

            // Determine the total number of points to trace along the longest axis
            Fixed64 steps = FixedMath.Round(FixedMath.Max(FixedMath.Max(delta.x, delta.y), delta.z)) + Fixed64.One;
            Fixed64 nodeSize = GlobalGridManager.NodeSize;

            // Calculate the interval (step size) for each axis
            Fixed64 stepX = diff.x / (steps + nodeSize);
            Fixed64 stepY = diff.y / (steps + nodeSize);
            Fixed64 stepZ = diff.z / (steps + nodeSize);

            // Traverse the grid along the computed line
            for (Fixed64 i = nodeSize; i <= steps; i += nodeSize)
            {
                Vector3d tracePos = new Vector3d(
                    start.x + stepX * i,
                    start.y + stepY * i,
                    start.z + stepZ * i
                );

                if (!GlobalGridManager.GetGridAndNode(tracePos, out _, out Node node))
                    continue;

                if (visitedCoordinates.Add(node.SpawnToken))
                    yield return node;
            }

            // Include endpoint if requested
            if (includeEnd && GlobalGridManager.GetGridAndNode(end, out _, out Node endNode))
            {
                if (visitedCoordinates.Add(endNode.SpawnToken))
                    yield return endNode;
            }
        }

        /// <summary>
        /// Traces a 2D line between two points, snapping them to grid coordinates.
        /// </summary>
        /// <remarks>
        /// This method projects the 2D line onto the X-Y plane and follows the closest grid-aligned path.
        /// </remarks>
        /// <param name="start">Starting 2D position.</param>
        /// <param name="end">Ending 2D position.</param>
        /// <param name="includeEnd">Whether to include the end node.</param>
        /// <returns>A collection of <see cref="Node"/> objects representing the traced path.</returns>
        public static IEnumerable<Node> TraceLine(
            Vector2d start,
            Vector2d end,
            bool includeEnd = true)
        {
            SwiftHashSet<int> visitedCoordinates = new SwiftHashSet<int>();

            // Compute directional differences
            Vector2d diff = end - start;
            Vector2d delta = Vector2d.Abs(diff);

            // Determine step count
            Fixed64 steps = FixedMath.Round(FixedMath.Max(delta.x, delta.y)) + Fixed64.One;
            Fixed64 nodeSize = GlobalGridManager.NodeSize;

            // Compute step size
            Fixed64 stepX = diff.x / (steps + nodeSize);
            Fixed64 stepY = diff.y / (steps + nodeSize);

            // Traverse the grid
            for (Fixed64 i = nodeSize; i <= steps; i += nodeSize)
            {
                Vector3d tracePos = new Vector3d(
                    start.x + stepX * i,
                    start.y + stepY * i,
                    Fixed64.Zero // Stay on the X-Y plane
                );

                if (!GlobalGridManager.GetGridAndNode(tracePos, out _, out Node node))
                    continue;

                // If valid coordinates were returned, yield them if they haven't been visited yet
                if (visitedCoordinates.Add(node.SpawnToken))
                    yield return node;
            }

            // Include endpoint if requested
            if (includeEnd && GlobalGridManager.GetGridAndNode(end.ToVector3d(Fixed64.Zero), out _, out Node endNode))
            {
                if (visitedCoordinates.Add(endNode.SpawnToken))
                    yield return endNode;
            }
        }
    }
}

