using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GridForge.Grids
{
    /// <summary>
    /// Manages a collection of interconnected grids to support large or dynamic worlds.
    /// Handles grid storage, retrieval, and spatial hashing for fast lookups.
    /// </summary>
    public static class GlobalGridManager
    {
        #region Constants

        /// <summary>
        /// Maximum number of grids that can be managed.
        /// </summary>
        public const ushort MaxGrids = ushort.MaxValue - 1;

        /// <summary>
        /// Threshold for resizing active grids when their count decreases.
        /// </summary>
        public const float ResizeThreshold = 0.75f;

        /// <summary>
        /// The size of a spatial hash cell used for grid lookup.
        /// </summary>
        private const int SpatialCellSize = 50;

        /// <summary>
        /// The size of each grid node in world units.
        /// </summary>
        public static readonly Fixed64 NodeSize = new Fixed64(0.5f);

        /// <summary>
        /// Resolution for snapping or searching within the grid (half of NodeSize).
        /// </summary>
        public static readonly Fixed64 NodeResolution = NodeSize * Fixed64.Half;

        #endregion

        #region Properties

        /// <summary>
        /// Collection of all active grids managed by the system.
        /// </summary>
        public static SwiftBucket<Grid> ActiveGrids { get; private set; }

        public static SwiftDictionary<int, ushort> BoundsTracker { get; private set; }

        /// <summary>
        /// Dictionary mapping spatial hash keys to grid indices for fast lookups.
        /// </summary>
        public static SwiftDictionary<int, SwiftHashSet<ushort>> SpatialHash { get; private set; }

        /// <summary>
        /// The current version of the grid system, incremented on major changes.
        /// </summary>
        public static uint Version { get; private set; }

        public static bool IsActive { get; private set; }

        /// <summary>
        /// Predefined offsets for a 3x3x3 neighbor structure, excluding the center position.
        /// </summary>
        public static readonly (int x, int y, int z)[] DirectionOffsets = new (int x, int y, int z)[26]
        {
            (-1, 0, 0),
            (0, 0, -1),
            (0, 0, 1),
            (1, 0, 0),
            (-1, 0, -1),
            (-1, 0, 1),
            (1, 0, -1),
            (1, 0, 1),
            (-1, -1, 0),
            (0, -1, -1),
            (0, -1, 1),
            (1, -1, 0),
            (-1, -1, -1),
            (-1, -1, 1),
            (1, -1, -1),
            (1, -1, 1),
            (0, -1, 0),
            (-1, 1, 0),
            (0, 1, -1),
            (0, 1, 1),
            (1, 1, 0),
            (-1, 1, -1),
            (-1, 1, 1),
            (1, 1, -1),
            (1, 1, 1),
            (0, 1, 0)
        };

        #endregion

        private static readonly object _lock = new object();

        #region Setup & Reset

        /// <summary>
        /// Initializes necessary collections for managing grids.
        /// </summary>
        public static void Setup()
        {
            if (IsActive)
            {
                Console.WriteLine("Global Grid Manager already active.  Call `Reset` before attempting to setup.");
                return;
            }

            lock (_lock)
            {
                ActiveGrids ??= new SwiftBucket<Grid>();
                BoundsTracker ??= new SwiftDictionary<int, ushort>();
                SpatialHash ??= new SwiftDictionary<int, SwiftHashSet<ushort>>();

                Version = 1;
                IsActive = true;
            }
        }

        /// <summary>
        /// Resets the global grid manager, clearing all grids and spatial data.
        /// </summary>
        public static void Reset()
        {
            if (!IsActive)
            {
                Console.WriteLine("Global Grid Manager not active.  Call `Setup` before attempting to reset.");
                return;
            }

            lock (_lock)
            {
                if (ActiveGrids != null)
                {
                    foreach (Grid grid in ActiveGrids)
                        Pools.GridPool.Release(grid);

                    ActiveGrids.Clear();
                }

                BoundsTracker?.Clear();
                SpatialHash?.Clear();

                IsActive = false;
            }
        }

        #endregion

        #region Grid Management

        /// <summary>
        /// Adds a new grid to the world and registers it in the spatial hash.
        /// </summary>
        public static bool AddGrid(GridConfiguration configuration, out ushort allocatedIndex)
        {
            allocatedIndex = ushort.MaxValue;
            if ((uint)ActiveGrids.Count > MaxGrids)
            {
                Console.WriteLine($"No more grids can be added at this time.");
                return false;
            }

            if (configuration.GridMax.x < configuration.GridMin.x
                || configuration.GridMax.y < configuration.GridMin.y
                || configuration.GridMax.z < configuration.GridMin.z)
            {
                Console.WriteLine("Invalid Grid Bounds: GridMax must be greater than or equal to GridMin.");
                return false;
            }

            int hashedBounds = configuration.GridMin.GetHashCode() ^ configuration.GridMax.GetHashCode();
            if (BoundsTracker.ContainsKey(hashedBounds))
            {
                Console.WriteLine("A grid with these bounds has already been allocated.");
                allocatedIndex = BoundsTracker[hashedBounds];
                return true;
            }

            try
            {
                lock (_lock)
                {
                    Grid newGrid = Pools.GridPool.Rent();
                    allocatedIndex = (ushort)ActiveGrids.Add(newGrid);
                    BoundsTracker.Add(hashedBounds, allocatedIndex);

                    newGrid.Initialize(allocatedIndex, configuration);
                    foreach (int cellIndex in GetSpatialCells(configuration.GridMin, configuration.GridMax))
                    {
                        if (!SpatialHash.ContainsKey(cellIndex))
                            SpatialHash[cellIndex] = new SwiftHashSet<ushort>();

                        if (!SpatialHash[cellIndex].Add(allocatedIndex))
                            continue;

                        // Assign neighbors from grids sharing this spatial hash cell
                        foreach (ushort neighborIndex in SpatialHash[cellIndex])
                        {
                            if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == newGrid.GlobalIndex)
                                continue;

                            Grid neighborGrid = ActiveGrids[neighborIndex];

                            // Ensure the grids actually overlap before linking them as neighbors
                            if (!newGrid.IsGridOverlapValid(neighborGrid))
                                continue;

                            newGrid.AddGridNeighbor(neighborGrid);
                            neighborGrid.AddGridNeighbor(newGrid);
                        }

                    }

                    Version++;

                    return true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error occured while adding new grid: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes a grid and updates all references to ensure integrity.
        /// </summary>
        public static bool RemoveGrid(ushort index)
        {
            if (!ActiveGrids.IsAllocated(index))
                return false;

            try
            {
                lock (_lock)
                {
                    Grid gridToRemove = ActiveGrids[index];

                    int hashedBounds = gridToRemove.Bounds.Min.GetHashCode() ^ gridToRemove.Bounds.Max.GetHashCode();
                    if (BoundsTracker.ContainsKey(hashedBounds))
                        BoundsTracker.Remove(hashedBounds);

                    // Clearing out neighbor relationships handled on `Grid.Reset`
                    Pools.GridPool.Release(gridToRemove);
                    ActiveGrids.RemoveAt(index);

                    if (ActiveGrids.Count == 0)
                        ActiveGrids.TrimExcessCapacity();

                    Version++;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error occured while removing grid at index ({index}): {e.Message}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Notifies grids of a change in their structure.
        /// </summary>
        public static void SendGridChangeNotification(int index, bool significant = false)
        {
            if (significant)
                Version++;
            if (ActiveGrids.IsAllocated(index))
                ActiveGrids[index].NotifyGridVersionChange();
        }

        #endregion

        #region Grid Lookup & Querying

        /// <summary>
        /// Retrieves a grid by its global index.
        /// </summary>
        public static bool GetGrid(ushort index, out Grid outGrid)
        {
            outGrid = null;
            if ((uint)index > ActiveGrids.Count)
            {
                Console.WriteLine($"GlobalGridIndex '{index}' is out-of-bounds for ActiveGrids.");
                return false;
            }

            if (!ActiveGrids.IsAllocated(index))
            {
                Console.WriteLine($"GlobalGridIndex '{index}' has not been allocated to ActiveGrids.");
                return false;
            }

            outGrid = ActiveGrids[index];
            return true;
        }

        /// <summary>
        /// Retrieves the grid containing a given world position.
        /// </summary>
        public static bool GetGrid(Vector3d position, out Grid outGrid)
        {
            outGrid = null;
            int cellIndex = GetSpatialCellIndex(position);

            if (!SpatialHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                return false;

            foreach (ushort candidateIndex in gridList)
            {
                if (!GetGrid(candidateIndex, out Grid candidateGrid) || !ActiveGrids[candidateIndex].IsActive)
                    continue;

                if (candidateGrid.IsInBounds(position))
                {
                    outGrid = candidateGrid;
                    return true;
                }
            }

            Console.WriteLine($"No grid contains position {position}.");
            return false;
        }

        /// <summary>
        /// Retrieves the grid containing a given world position and the node at that position.
        /// </summary>
        public static bool GetGridAndNode(Vector3d position, out Grid outGrid, out Node outNode)
        {
            outNode = null;
            if (!GetGrid(position, out outGrid))
                return false;

            return outGrid.GetNode(position, out outNode);
        }

        /// <summary>
        /// Retrieves a grid by its unique global coordinates.
        /// </summary>
        public static bool GetGrid(CoordinatesGlobal coordinates, out Grid outGrid)
        {
            if (!GetGrid(coordinates.GridIndex, out outGrid))
                return false;

            if (coordinates.GridSpawnToken != outGrid.SpawnToken)
            {
                Console.WriteLine($"" +
                    $"Coordinate target grid instance Id {coordinates.GridSpawnToken} does not match current " +
                    $"instance at index {coordinates.GridIndex}.");
                outGrid = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Retrieves the grid containing a given global coordinate and the node at that position.
        /// </summary>
        public static bool GetGridAndNode(CoordinatesGlobal coordinates, out Grid outGrid, out Node outNode)
        {
            outNode = null;

            if (!GetGrid(coordinates, out outGrid))
                return false;

            if (!outGrid.GetNode(coordinates.NodeCoordinates, out outNode))
                return false;

            return true;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets all spatial cells that intersect a grid's bounding volume.
        /// </summary>
        /// <returns>A list of spatial cell indices.</returns>
        private static IEnumerable<int> GetSpatialCells(Vector3d min, Vector3d max)
        {
            // Determine the range of spatial hash cells that intersect the given bounds
            (int xMin, int yMin, int zMin) = (
                    (min.x / SpatialCellSize).FloorToInt(),
                    (min.y / SpatialCellSize).FloorToInt(),
                    (min.z / SpatialCellSize).FloorToInt()
                );
            (int xMax, int yMax, int zMax) = (
                    (max.x / SpatialCellSize).FloorToInt(),
                    (max.y / SpatialCellSize).FloorToInt(),
                    (max.z / SpatialCellSize).FloorToInt()
                );

            // Yield all spatial hash keys that fall within this range
            for (int z = zMin; z <= zMax; z++)
            {
                for (int y = yMin; y <= yMax; y++)
                {
                    for (int x = xMin; x <= xMax; x++)
                        yield return GetSpawnHash(x, y, z);
                }
            }
        }

        /// <summary>
        /// Finds grids that overlap with the specified target grid.
        /// </summary>
        public static IEnumerable<Grid> FindOverlappingGrids(Grid targetGrid)
        {
            // Check all spatial hash cells that this grid occupies
            foreach (int cellIndex in GetSpatialCells(targetGrid.Bounds.Min, targetGrid.Bounds.Max))
            {
                if (!SpatialHash.TryGetValue(cellIndex, out var gridList))
                    continue;

                // Check all grids sharing this spatial cell
                foreach (ushort neighborIndex in gridList)
                {
                    if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == targetGrid.GlobalIndex)
                        continue;

                    Grid neighborGrid = ActiveGrids[neighborIndex];

                    // Only return grids that have an actual overlap with targetGrid
                    if (targetGrid.IsGridOverlapValid(neighborGrid))
                        yield return neighborGrid;
                }
            }
        }

        /// <summary>
        /// Retrieves all grid nodes covered by the given bounding area.
        /// </summary>
        /// <param name="boundsMin">The minimum corner of the bounding area.</param>
        /// <param name="boundsMax">The maximum corner of the bounding area.</param>
        public static IEnumerable<Node> GetCoveredNodes(Vector3d boundsMin, Vector3d boundsMax)
        {
            Vector3d snappedMin = SnapToGridFloor(boundsMin);
            Vector3d snappedMax = SnapToGridCeil(boundsMax);

            SwiftHashSet<int> redundancyChecker = new SwiftHashSet<int>();
            for (Fixed64 x = snappedMin.x; x <= snappedMax.x; x += NodeResolution)
            {
                for (Fixed64 y = snappedMin.y; y <= snappedMax.y; y += NodeResolution)
                {
                    for (Fixed64 z = snappedMin.z; z <= snappedMax.z; z += NodeResolution)
                    {
                        Vector3d position = new Vector3d(x, y, z);
                        if (!GetGridAndNode(position, out _, out Node node)
                            || !redundancyChecker.Add(node.SpawnToken))
                        {
                            continue;
                        }

                        yield return node;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a given neighbor index corresponds to a diagonal neighbor in the 3x3x3 structure.
        /// </summary>
        public static bool IsDiagonalNeighbor(int index)
        {
            if ((uint)index >= DirectionOffsets.Length)
                return false;

            // Retrieve the offset corresponding to the given index
            (int x, int y, int z) = DirectionOffsets[index];

            // A neighbor is diagonal if at least two of its coordinates are nonzero
            return (x != 0 ? 1 : 0) +
                   (y != 0 ? 1 : 0) +
                   (z != 0 ? 1 : 0) >= 2;
        }

        /// <summary>
        /// Converts a 3D offset into a corresponding <see cref="LinearDirection"/> in a 3x3x3 grid.
        /// </summary>
        /// <param name="gridOffset">The (x, y, z) offset from the center node.</param>
        /// <returns>The corresponding <see cref="LinearDirection"/>, or <see cref="LinearDirection.None"/> if invalid.</returns>
        public static LinearDirection GetNeighborDirectionFromOffset((int x, int y, int z) gridOffset)
        {
            Debug.Assert(gridOffset.x >= -1 && gridOffset.x <= 1, "Invalid x offset.");
            Debug.Assert(gridOffset.y >= -1 && gridOffset.y <= 1, "Invalid y offset.");
            Debug.Assert(gridOffset.z >= -1 && gridOffset.z <= 1, "Invalid z offset.");

            // Convert the 3D offset into a 3x3x3 index (0 to 26)
            int index = ((gridOffset.z + 1) * 3 + (gridOffset.y + 1)) * 3 + (gridOffset.x + 1);

            // The center node (itself) should not be assigned a direction
            if (index == 13)
                return LinearDirection.None;

            // Ensure index is within the defined LinearDirection values
            if (index >= 0 && index < Enum.GetValues(typeof(LinearDirection)).Length)
                return (LinearDirection)index;

            return LinearDirection.None;
        }

        /// <summary>
        /// Computes a spatial hash key for a given position.
        /// </summary>
        public static int GetSpatialCellIndex(Vector3d position)
        {
            (int x, int y, int z) = (
                (position.x / SpatialCellSize).FloorToInt(),
                (position.y / SpatialCellSize).FloorToInt(),
                (position.z / SpatialCellSize).FloorToInt()
            );

            return GetSpawnHash(x, y, z);
        }

        /// <summary>
        /// Generates a hash value for a given set of 3D coordinates.
        /// </summary>
        public static int GetSpawnHash(int x, int y, int z)
        {
            int hash = 17;
            hash = hash * 31 ^ x;
            hash = hash * 31 ^ y;
            hash = hash * 31 ^ z;
            return hash;
        }

        /// <summary>
        /// Helper function to ceil snap a <see cref="Vector3d"/> to a grid.
        /// </summary>
        public static Vector3d SnapToGridCeil(Vector3d position)
        {
            Fixed64 x = ((position.x + NodeSize) / NodeSize).CeilToInt() * NodeSize;
            Fixed64 y = ((position.y + NodeSize) / NodeSize).CeilToInt() * NodeSize;
            Fixed64 z = ((position.z + NodeSize) / NodeSize).CeilToInt() * NodeSize;
            return new Vector3d(x, y, z);
        }

        /// <summary>
        /// Helper function to floor snap a <see cref="Vector3d"/> to a grid.
        /// </summary>
        public static Vector3d SnapToGridFloor(Vector3d position)
        {
            Fixed64 x = (position.x / NodeSize).FloorToInt() * NodeSize;
            Fixed64 y = (position.y / NodeSize).FloorToInt() * NodeSize;
            Fixed64 z = (position.z / NodeSize).FloorToInt() * NodeSize;
            return new Vector3d(x, y, z);
        }

        #endregion
    }
}
