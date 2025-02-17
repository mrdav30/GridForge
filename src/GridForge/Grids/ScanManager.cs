using FixedMathSharp;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GridForge.Grids
{
    /// <summary>
    /// Provides efficient querying methods for retrieving occupants within a grid.
    /// Handles spatial lookups for nodes, filtering by occupant type, and fetching occupants using unique tickets.
    /// </summary>
    public static class ScanManager
    {
        /// <summary>
        /// Retrieves all occupants at a given world position within the grid.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="position">The world position to check for occupants.</param>
        /// <returns>An enumerable collection of node occupants.</returns>
        public static IEnumerable<INodeOccupant> GetNodeOccupants(this Grid grid, Vector3d position)
        {
            return grid.TryGetNode(position, out Node targetNode)
                ? GetNodeOccupants(grid, targetNode)
                : Enumerable.Empty<INodeOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given node coordinate within the grid.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="coordinates">The local coordinates of the node.</param>
        /// <returns>An enumerable collection of node occupants.</returns>
        public static IEnumerable<INodeOccupant> GetNodeOccupants(this Grid grid, CoordinatesLocal coordinates)
        {
            return grid.TryGetNode(coordinates, out Node targetNode)
                ? GetNodeOccupants(grid, targetNode)
                : Enumerable.Empty<INodeOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given node.
        /// </summary>
        /// <param name="grid">The grid containing the node.</param>
        /// <param name="targetNode">The target node to retrieve occupants from.</param>
        /// <returns>An enumerable collection of node occupants.</returns>
        public static IEnumerable<INodeOccupant> GetNodeOccupants(this Grid grid, Node targetNode)
        {
            return targetNode.IsOccupied
                && grid.TryGetScanCell(targetNode.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                ? scanCell.GetOccupantsFor(targetNode.SpawnToken)
                : Enumerable.Empty<INodeOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at a given world position.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="grid">The grid to query.</param>
        /// <param name="position">The world position of the node.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetNodeOccupantsByType<T>(this Grid grid, Vector3d position) where T : INodeOccupant
        {
            return grid.TryGetNode(position, out Node targetNode)
                ? GetNodeOccupantsByType<T>(grid, targetNode)
                : Enumerable.Empty<T>();
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at a given node coordinate.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="grid">The grid to query.</param>
        /// <param name="coordinates">The local node coordinates.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetNodeOccupantsByType<T>(this Grid grid, CoordinatesLocal coordinates) where T : INodeOccupant
        {
            return grid.TryGetNode(coordinates, out Node targetNode)
                ? GetNodeOccupantsByType<T>(grid, targetNode)
                : Enumerable.Empty<T>();
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at a given node.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="grid">The grid containing the node.</param>
        /// <param name="targetNode">The target node.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetNodeOccupantsByType<T>(this Grid grid, Node targetNode) where T : INodeOccupant
        {
            return targetNode == null
                ? Enumerable.Empty<T>()
                : GetNodeOccupants(grid, targetNode).OfType<T>();
        }

        /// <summary>
        /// Retrieves a specific occupant at a given world position using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="position">The world position of the node.</param>
        /// <param name="occupantTicket">The unique identifier of the occupant.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetNodeOccupant(
            this Grid grid,
            Vector3d position,
            int occupantTicket,
            out INodeOccupant occupant)
        {
            occupant = null;
            return grid.TryGetNodeCoordinates(position, out CoordinatesLocal targetCoordinates)
                && TryGetNodeOccupant(grid, targetCoordinates, occupantTicket, out occupant);
        }

        /// <summary>
        /// Retrieves a specific occupant at a given node coordinate using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="coordinatesLocal">The local node coordinates.</param>
        /// <param name="occupantTicket">The unique identifier of the occupant.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetNodeOccupant(
            this Grid grid,
            CoordinatesLocal coordinatesLocal,
            int occupantTicket,
            out INodeOccupant occupant)
        {
            occupant = null;
            return grid.TryGetNode(coordinatesLocal, out Node targetNode)
                && TryGetNodeOccupant(grid, targetNode, occupantTicket, out occupant);
        }

        /// <summary>
        /// Retrieves a specific occupant from a given node using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid containing the node.</param>
        /// <param name="targetNode">The target node.</param>
        /// <param name="occupantTicket">The unique identifier of the occupant.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetNodeOccupant(
            this Grid grid,
            Node targetNode,
            int occupantTicket,
            out INodeOccupant occupant)
        {
            occupant = null;
            return targetNode.IsOccupied
                && grid.TryGetScanCell(targetNode.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                && scanCell.TryGetOccupantAt(targetNode.SpawnToken, occupantTicket, out occupant);
        }

        /// <summary>
        /// Retrieves all occupants at a given world position within the grid.
        /// </summary>
        public static IEnumerable<INodeOccupant> GetOccupants(this Grid grid, Vector3d position)
        {
            return grid.TryGetNode(position, out Node targetNode)
                ? GetOccupants(grid, targetNode)
                : Enumerable.Empty<INodeOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given coordinate within the grid.
        /// </summary>
        public static IEnumerable<INodeOccupant> GetOccupants(this Grid grid, CoordinatesLocal coordinates)
        {
            return grid.TryGetNode(coordinates, out Node targetNode)
                ? GetOccupants(grid, targetNode)
                : Enumerable.Empty<INodeOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given node.
        /// </summary>
        public static IEnumerable<INodeOccupant> GetOccupants(this Grid grid, Node targetNode)
        {
            return targetNode.IsOccupied
                && grid.TryGetScanCell(targetNode.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                ? scanCell.GetOccupants()
                : Enumerable.Empty<INodeOccupant>();
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        public static IEnumerable<INodeOccupant> GetConditionalOccupants(
            this Grid grid,
            Vector3d position,
            Func<byte, bool> groupConditional)
        {
            return grid.TryGetNode(position, out Node targetNode)
                ? GetConditionalOccupants(grid, targetNode, groupConditional)
                : Enumerable.Empty<INodeOccupant>();
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        public static IEnumerable<INodeOccupant> GetConditionalOccupants(
            this Grid grid,
            CoordinatesLocal coordinates,
            Func<byte, bool> groupConditional)
        {
            return grid.TryGetNode(coordinates, out Node targetNode)
                ? GetConditionalOccupants(grid, targetNode, groupConditional)
                : Enumerable.Empty<INodeOccupant>();
        }

        /// <summary>
        /// Retrieves occupants at a given node that match a specified group condition.
        /// </summary>
        public static IEnumerable<INodeOccupant> GetConditionalOccupants(
            this Grid grid,
            Node targetNode,
            Func<byte, bool> groupConditional)
        {
            return groupConditional != null
                && targetNode.IsOccupied
                && grid.TryGetScanCell(targetNode.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                ? scanCell.GetConditionalOccupants(groupConditional)
                : Enumerable.Empty<INodeOccupant>();
        }


        /// <summary>
        /// Scans for occupants within a given radius from a specified position.
        /// </summary>
        /// <param name="position">The center position to scan from.</param>
        /// <param name="radius">The search radius.</param>
        /// <param name="groupConditional">Optional filter for occupant groups.</param>
        /// <returns>An enumerable of all occupants found within the radius.</returns>
        public static IEnumerable<INodeOccupant> ScanRadius(
            Vector3d position,
            Fixed64 radius,
            Func<byte, bool> groupConditional = null)
        {
            Fixed64 squaredRadius = radius * radius;
            SwiftHashSet<INodeOccupant> results = SwiftCollectionPool<SwiftHashSet<INodeOccupant>, INodeOccupant>.Rent();

            // Get bounds for the search area
            Vector3d boundsMin = position - radius;
            Vector3d boundsMax = position + radius;

            foreach (ScanCell scanCell in GridTracer.GetCoveredScanCells(boundsMin, boundsMax))
            {
                if (!scanCell.IsOccupied)
                    continue;

                IEnumerable<INodeOccupant> occupants = groupConditional == null
                    ? scanCell.GetOccupants()
                    : scanCell.GetConditionalOccupants(groupConditional);

                foreach (INodeOccupant occupant in occupants)
                {
                    if ((occupant.WorldPosition - position).SqrMagnitude <= squaredRadius)
                        results.Add(occupant);
                }
            }

            foreach (INodeOccupant result in results)
                yield return result;

            SwiftCollectionPool<SwiftHashSet<INodeOccupant>, INodeOccupant>.Release(results);
        }

        /// <summary>
        /// Scans for occupants of a specific type within a given radius.
        /// </summary>
        public static IEnumerable<T> ScanRadius<T>(Vector3d position, Fixed64 radius) where T : INodeOccupant
        {
            return ScanRadius(position, radius).OfType<T>();
        }
    }
}
