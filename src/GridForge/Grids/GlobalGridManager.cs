using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

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
        /// The default size of each grid voxel in world units.
        /// </summary>
        public static readonly Fixed64 DefaultVoxelSize = Fixed64.One;

        /// <summary>
        /// The default size of a spatial hash cell used for grid lookup.
        /// </summary>
        public const int DefaultSpatialGridCellSize = 50;

        /// <summary>
        /// The size of each grid voxel in world units.
        /// </summary>
        public static Fixed64 VoxelSize { get; private set; }

        /// <summary>
        /// The size of a spatial hash cell used for grid lookup.
        /// </summary>
        public static int SpatialGridCellSize { get; private set; }

        /// <summary>
        /// Resolution for snapping or searching within the grid (half of VoxelSize).
        /// </summary>
        public static Fixed64 VoxelResolution = VoxelSize * Fixed64.Half;

        #endregion

        #region Properties

        /// <summary>
        /// Collection of all active grids managed by the system.
        /// </summary>
        public static SwiftBucket<VoxelGrid> ActiveGrids { get; private set; }

        /// <summary>
        /// Dictionary mapping hashed bounds to grid indices to prevent duplicate grids.
        /// </summary>
        public static SwiftDictionary<int, ushort> BoundsTracker { get; private set; }

        /// <summary>
        /// Dictionary mapping spatial hash keys to grid indices for fast lookups.
        /// </summary>
        public static SwiftDictionary<int, SwiftHashSet<ushort>> SpatialGridHash { get; private set; }

        /// <summary>
        /// The current version of the grid system, incremented on major changes.
        /// </summary>
        public static uint Version { get; private set; }

        /// <summary>
        /// Indicates whether the GlobalGridManager is active and initialized.
        /// Prevents duplicate setup calls.
        /// </summary>
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
            (0, -1, 0),
            (0, 1, 0),
            (-1, 0, -1),
            (-1, 0, 1),
            (1, 0, -1),
            (1, 0, 1),
            (-1, -1, 0),
            (0, -1, -1),
            (0, -1, 1),
            (1, -1, 0),
            (-1, 1, 0),
            (0, 1, -1),
            (0, 1, 1),
            (1, 1, 0),
            (-1, -1, -1),
            (-1, -1, 1),
            (1, -1, -1),
            (1, -1, 1),
            (-1, 1, -1),
            (-1, 1, 1),
            (1, 1, -1),
            (1, 1, 1)
        };

        /// <summary>
        /// Lock for managing concurrent access to grid operations.
        /// Ensures thread safety for read/write operations.
        /// </summary>
        private static readonly ReaderWriterLockSlim _gridLock = new ReaderWriterLockSlim();

        #endregion

        #region Action Delegates

        /// <summary>
        /// Event triggers when grid is added or removed.
        /// Allows external systems to react to the active grid mutation.
        /// </summary>
        public static Action<GridChange, uint> OnActiveGridChange;

        /// <summary>
        /// Event triggered when the GlobalGridManager is reset.
        /// Allows external systems to react to a full grid wipe.
        /// </summary>
        public static Action OnReset;

        #endregion

        #region Setup & Reset

        /// <summary>
        /// Initializes necessary collections for managing grids.
        /// </summary>
        public static void Setup() => Setup(DefaultVoxelSize, DefaultSpatialGridCellSize);

        /// <inheritdoc cref="Setup()"/>
        /// <param name="voxelSize"></param>
        /// <param name="spatialGridCellSize"></param>
        public static void Setup(Fixed64 voxelSize, int spatialGridCellSize = DefaultSpatialGridCellSize)
        {
            if (IsActive)
            {
                GridForgeLogger.Warn("Global Grid Manager already active.  Call `Reset` before attempting to setup.");
                return;
            }

            VoxelSize = voxelSize > Fixed64.One ? Fixed64.One : voxelSize > Fixed64.Zero ? voxelSize : DefaultVoxelSize;
            SpatialGridCellSize = spatialGridCellSize;

            ActiveGrids ??= new SwiftBucket<VoxelGrid>();
            BoundsTracker ??= new SwiftDictionary<int, ushort>();
            SpatialGridHash ??= new SwiftDictionary<int, SwiftHashSet<ushort>>();

            Version = 1;
            IsActive = true;
        }

        /// <summary>
        /// Resets the global grid manager, clearing all grids and spatial data.
        /// </summary>
        public static void Reset()
        {
            if (!IsActive)
            {
                GridForgeLogger.Warn("Global Grid Manager not active.  Call `Setup` before attempting to reset.");
                return;
            }

            try
            {
                OnReset?.Invoke(); // Fire off before we remove the reference
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"Reset notification error: {ex.Message}");
            }

            if (ActiveGrids != null)
            {
                foreach (VoxelGrid grid in ActiveGrids)
                    Pools.GridPool.Release(grid);

                ActiveGrids.Clear();
            }

            BoundsTracker?.Clear();
            SpatialGridHash?.Clear();

            IsActive = false;
        }

        #endregion

        #region Grid Management

        /// <summary>
        /// Adds a new grid to the world and registers it in the spatial hash.
        /// </summary>
        public static GridAddResult TryAddGrid(GridConfiguration configuration, out ushort allocatedIndex)
        {
            allocatedIndex = ushort.MaxValue;
            if ((uint)ActiveGrids.Count > MaxGrids)
            {
                GridForgeLogger.Warn($"No more grids can be added at this time.");
                return GridAddResult.MaxGridsReached;
            }

            if (configuration.BoundsMax < configuration.BoundsMin)
            {
                GridForgeLogger.Error("Invalid Grid Bounds: GridMax must be greater than or equal to GridMin.");
                return GridAddResult.InvalidBounds;
            }

            // Create a unique hash based on the grid's min/max bounds to prevent duplicates
            int hashedBounds = configuration.GetHashCode();

            _gridLock.EnterReadLock();
            try
            {
                if (BoundsTracker.TryGetValue(hashedBounds, out allocatedIndex))
                {
                    GridForgeLogger.Warn("A grid with these bounds has already been allocated.");
                    return GridAddResult.AlreadyExists;
                }
            }
            finally
            {
                _gridLock.ExitReadLock();
            }

            VoxelGrid newGrid = Pools.GridPool.Rent();
            _gridLock.EnterWriteLock();
            try
            {
                allocatedIndex = (ushort)ActiveGrids.Add(newGrid);
                BoundsTracker.Add(hashedBounds, allocatedIndex);

                newGrid.Initialize(allocatedIndex, configuration);
                foreach (int cellIndex in GetSpatialGridCells(configuration.BoundsMin, configuration.BoundsMax))
                {
                    if (!SpatialGridHash.ContainsKey(cellIndex))
                        SpatialGridHash.Add(cellIndex, new SwiftHashSet<ushort>());

                    // Assign neighbors from grids sharing this spatial hash cell
                    foreach (ushort neighborIndex in SpatialGridHash[cellIndex])
                    {
                        if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == allocatedIndex)
                            continue;

                        VoxelGrid neighborGrid = ActiveGrids[neighborIndex];

                        // Ensure the grids actually overlap before linking them as neighbors
                        if (!VoxelGrid.IsGridOverlapValid(newGrid, neighborGrid))
                            continue;

                        newGrid.TryAddGridNeighbor(neighborGrid);
                        neighborGrid.TryAddGridNeighbor(newGrid);
                    }

                    SpatialGridHash[cellIndex].Add(allocatedIndex);
                }

                Version++;
            }
            finally
            {
                _gridLock.ExitWriteLock();
            }

            NotifyActiveGridChange(GridChange.Add, allocatedIndex);

            return GridAddResult.Success;
        }

        /// <summary>
        /// Removes a grid and updates all references to ensure integrity.
        /// </summary>
        public static bool TryRemoveGrid(ushort removeIndex)
        {
            if (!ActiveGrids.IsAllocated(removeIndex))
                return false;

            // Fire off before we remove the reference
            NotifyActiveGridChange(GridChange.Remove, removeIndex);

            VoxelGrid gridToRemove;
            _gridLock.EnterWriteLock();
            try
            {
                gridToRemove = ActiveGrids[removeIndex];
                // remove grid from spatial hash
                foreach (int cellIndex in GetSpatialGridCells(gridToRemove.BoundsMin, gridToRemove.BoundsMax))
                {
                    if (!SpatialGridHash.ContainsKey(cellIndex))
                        continue;

                    SpatialGridHash[cellIndex].Remove(gridToRemove.GlobalIndex);

                    if (gridToRemove.IsConjoined)
                    {
                        // Remove the reference to this grid from its neighbors
                        foreach (ushort neighborIndex in SpatialGridHash[cellIndex])
                        {
                            if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == removeIndex)
                                continue;

                            VoxelGrid neighborGrid = ActiveGrids[neighborIndex];

                            if (!VoxelGrid.IsGridOverlapValid(gridToRemove, neighborGrid))
                                continue;

                            neighborGrid.TryRemoveGridNeighbor(gridToRemove);
                        }
                    }

                    // Remove empty spatial hash cells to prevent memory buildup
                    if (SpatialGridHash[cellIndex].Count == 0)
                        SpatialGridHash.Remove(cellIndex);
                }

                int hashedBounds = gridToRemove.Configuration.GetHashCode();
                BoundsTracker.Remove(hashedBounds);
                ActiveGrids.RemoveAt(removeIndex);

                Version++;
            }
            finally
            {
                _gridLock.ExitWriteLock();
            }

            // Clearing out neighbor relationships for this voxel handled on `Grid.Reset`
            Pools.GridPool.Release(gridToRemove);

            if (ActiveGrids.Count == 0)
                ActiveGrids.TrimExcessCapacity();

            return true;
        }

        private static void NotifyActiveGridChange(GridChange change, uint index)
        {
            try
            {
                OnActiveGridChange?.Invoke(change, index);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Grid {index}] notification error: {ex.Message} | Change: {change}");
            }
        }

        /// <summary>
        /// Notifies grids of a change in their structure.
        /// </summary>
        public static void IncrementGridVersion(int index, bool significant = false)
        {
            _gridLock.EnterWriteLock();
            try
            {
                if (significant)
                    Version++;
                if (ActiveGrids.IsAllocated(index))
                    ActiveGrids[index].Version++;
            }
            finally
            {
                _gridLock.ExitWriteLock();
            }
        }

        #endregion

        #region Grid Lookup & Querying

        /// <summary>
        /// Retrieves a grid by its global index.
        /// </summary>
        public static bool TryGetGrid(int index, out VoxelGrid outGrid)
        {
            outGrid = null;
            if ((uint)index > ActiveGrids.Count)
            {
                GridForgeLogger.Error($"GlobalGridIndex '{index}' is out-of-bounds for ActiveGrids.");
                return false;
            }

            if (!ActiveGrids.IsAllocated(index))
            {
                GridForgeLogger.Error($"GlobalGridIndex '{index}' has not been allocated to ActiveGrids.");
                return false;
            }

            outGrid = ActiveGrids[index];
            return true;
        }

        /// <summary>
        /// Retrieves the grid containing a given world position.
        /// </summary>
        public static bool TryGetGrid(Vector3d position, out VoxelGrid outGrid)
        {
            outGrid = null;
            int cellIndex = GetSpatialGridKey(position);

            if (!SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                return false;

            foreach (ushort candidateIndex in gridList)
            {
                if (!TryGetGrid(candidateIndex, out VoxelGrid candidateGrid) || !ActiveGrids[candidateIndex].IsActive)
                    continue;

                if (candidateGrid.IsInBounds(position))
                {
                    outGrid = candidateGrid;
                    return true;
                }
            }

            GridForgeLogger.Info($"No grid contains position {position}.");
            return false;
        }

        /// <summary>
        /// Retrieves a grid by its unique global index.
        /// </summary>
        public static bool TryGetGrid(GlobalVoxelIndex globalVoxelIndex, out VoxelGrid result)
        {
            // Ensure the grid is valid and the voxel belongs to the expected grid version
            return TryGetGrid(globalVoxelIndex.GridIndex, out result)
                && globalVoxelIndex.GridSpawnToken == result.SpawnToken;
        }

        /// <summary>
        /// Retrieves the grid containing a given world position and the voxel at that position.
        /// </summary>
        public static bool TryGetGridAndVoxel(
            Vector3d position,
            out VoxelGrid outGrid,
            out Voxel outVoxel)
        {
            outVoxel = null;
            return TryGetGrid(position, out outGrid)
                && outGrid.TryGetVoxel(position, out outVoxel);
        }

        /// <summary>
        /// Retrieves the grid containing a given global coordinate and the voxel at that position.
        /// </summary>
        public static bool TryGetGridAndVoxel(
            GlobalVoxelIndex globalVoxelIndex,
            out VoxelGrid outGrid,
            out Voxel result)
        {
            result = null;
            return TryGetGrid(globalVoxelIndex, out outGrid)
                && outGrid.TryGetVoxel(globalVoxelIndex.VoxelIndex, out result);
        }

        /// <summary>
        /// Retrieves the voxel from the given position.
        /// </summary>
        public static bool TryGetVoxel(
            Vector3d position,
            out Voxel result)
        {
            result = null;
            return TryGetGrid(position, out VoxelGrid grid)
                && grid.TryGetVoxel(position, out result);
        }

        /// <summary>
        /// Retrieves the voxel from the given global coordinate.
        /// </summary>
        public static bool TryGetVoxel(
            GlobalVoxelIndex globalVoxelIndex,
            out Voxel result)
        {
            result = null;
            return TryGetGrid(globalVoxelIndex, out VoxelGrid grid)
                && grid.TryGetVoxel(globalVoxelIndex.VoxelIndex, out result);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Retrieves all spatial hash cell indices that intersect the given bounding volume.
        /// </summary>
        /// <param name="min">The minimum corner of the bounding box.</param>
        /// <param name="max">The maximum corner of the bounding box.</param>
        /// <returns>An enumerable of spatial hash cell indices covering the given bounds.</returns>
        public static IEnumerable<int> GetSpatialGridCells(Vector3d min, Vector3d max)
        {
            // Convert min/max positions to their respective spatial grid indices.
            (int xMin, int yMin, int zMin) = SnapToSpatialGrid(min);
            (int xMax, int yMax, int zMax) = SnapToSpatialGrid(max);

            // Ensure correct ordering of min/max values in case of inverted bounds.
            // This prevents negative ranges that would otherwise cause an empty iteration.
            (xMin, xMax) = xMin > xMax ? (xMax, xMin) : (xMin, xMax);
            (yMin, yMax) = yMin > yMax ? (yMax, yMin) : (yMin, yMax);
            (zMin, zMax) = zMin > zMax ? (zMax, zMin) : (zMin, zMax);

            // Iterate through all spatial hash cells within the computed range.
            // This ensures we cover all relevant grid partitions.
            for (int z = zMin; z <= zMax; z++)
            {
                for (int y = yMin; y <= yMax; y++)
                {
                    for (int x = xMin; x <= xMax; x++)
                        yield return GetSpawnHash(x, y, z);
                }
            }

            yield break;
        }

        /// <summary>
        /// Ensures consistent and accurate placement within the correct spatial grid..
        /// </summary>
        /// <param name="position"></param>
        private static (int xMin, int yMin, int zMin) SnapToSpatialGrid(Vector3d position)
        {
            // - Use Abs() to ensure the division is done on positive values, preventing rounding issues.
            // - Apply FloorToInt() to obtain the correct spatial cell index.
            // - Restore the original sign using Sign() after flooring.
            return (
                    (position.x.Abs() / SpatialGridCellSize).FloorToInt() * position.x.Sign(),
                    (position.y.Abs() / SpatialGridCellSize).FloorToInt() * position.y.Sign(),
                    (position.z.Abs() / SpatialGridCellSize).FloorToInt() * position.z.Sign()
                );
        }

        /// <summary>
        /// Finds grids that overlap with the specified target grid.
        /// </summary>
        public static IEnumerable<VoxelGrid> FindOverlappingGrids(VoxelGrid targetGrid)
        {
            SwiftHashSet<VoxelGrid> overlappingGrids = new SwiftHashSet<VoxelGrid>();

            // Check all spatial hash cells that this grid occupies
            foreach (int cellIndex in GetSpatialGridCells(targetGrid.BoundsMin, targetGrid.BoundsMax))
            {
                if (!SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                    continue;

                // Check all grids sharing this spatial cell
                foreach (ushort neighborIndex in gridList)
                {
                    if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == targetGrid.GlobalIndex)
                        continue;

                    VoxelGrid neighborGrid = ActiveGrids[neighborIndex];

                    // Only return grids that have an actual overlap with targetGrid
                    if (VoxelGrid.IsGridOverlapValid(targetGrid, neighborGrid))
                        overlappingGrids.Add(neighborGrid);
                }
            }

            return overlappingGrids;
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
        /// Converts a 3D offset into a corresponding <see cref="SpatialDirection"/> in a 3x3x3 grid.
        /// </summary>
        /// <param name="gridOffset">The (x, y, z) offset from the center voxel.</param>
        /// <returns>The corresponding <see cref="SpatialDirection"/>, or <see cref="SpatialDirection.None"/> if invalid.</returns>
        public static SpatialDirection GetNeighborDirectionFromOffset((int x, int y, int z) gridOffset)
        {
            Debug.Assert(gridOffset.x >= -1 && gridOffset.x <= 1, "Invalid x offset.");
            Debug.Assert(gridOffset.y >= -1 && gridOffset.y <= 1, "Invalid y offset.");
            Debug.Assert(gridOffset.z >= -1 && gridOffset.z <= 1, "Invalid z offset.");

            // Convert the 3D offset into a 3x3x3 index (0 to 26)
            int index = ((gridOffset.z + 1) * 3 + (gridOffset.y + 1)) * 3 + (gridOffset.x + 1);

            // The center voxel (itself) should not be assigned a direction
            if (index == 13)
                return SpatialDirection.None;

            // Ensure index is within the defined LinearDirection values
            if (index >= 0 && index < Enum.GetValues(typeof(SpatialDirection)).Length)
                return (SpatialDirection)index;

            return SpatialDirection.None;
        }

        /// <summary>
        /// Computes a spatial hash key for a given position.
        /// </summary>
        public static int GetSpatialGridKey(Vector3d position)
        {
            (int x, int y, int z) = (
                position.x.FloorToInt() / SpatialGridCellSize,
                position.y.FloorToInt() / SpatialGridCellSize,
                position.z.FloorToInt() / SpatialGridCellSize
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
        public static Vector3d CeilToVoxelSize(Vector3d position)
        {
            return new Vector3d(
                (position.x / VoxelSize).CeilToInt() * VoxelSize,
                (position.y / VoxelSize).CeilToInt() * VoxelSize,
                (position.z / VoxelSize).CeilToInt() * VoxelSize
            );
        }

        /// <summary>
        /// Helper function to floor snap a <see cref="Vector3d"/> to a grid.
        /// </summary>
        public static Vector3d FloorToVoxelSize(Vector3d position)
        {
            // - Use Abs() to ensure the division is done on positive values, preventing rounding issues.
            // - Apply FloorToInt() to obtain the correct spatial cell index.
            // - Restore the original sign using Sign() after flooring.
            return new Vector3d(
                (position.x.Abs() / VoxelSize).FloorToInt() * VoxelSize * position.x.Sign(),
                (position.y.Abs() / VoxelSize).FloorToInt() * VoxelSize * position.y.Sign(),
                (position.z.Abs() / VoxelSize).FloorToInt() * VoxelSize * position.z.Sign()
            );
        }

        /// <summary>
        /// Snaps the given bounds to the the global voxel size
        /// </summary>
        public static (Vector3d min, Vector3d max) SnapBoundsToVoxelSize(
            Vector3d min,
            Vector3d max,
            double padding = 0)
        {
            // Ensure padding is non-negative
            Fixed64 fixedPadding = FixedMath.Max((Fixed64)padding, Fixed64.Zero);

            min -= fixedPadding;
            max += fixedPadding;

            Vector3d snapMin = CeilToVoxelSize(min);
            Vector3d snapMax = FloorToVoxelSize(max);

            // Ensure correct ordering of bounds
            (snapMin.x, snapMax.x) = snapMin.x > snapMax.x
                ? (snapMax.x, snapMin.x)
                : (snapMin.x, snapMax.x);
            (snapMin.y, snapMax.y) = snapMin.y > snapMax.y
                ? (snapMax.y, snapMin.y)
                : (snapMin.y, snapMax.y);
            (snapMin.z, snapMax.z) = snapMin.z > snapMax.z
                ? (snapMax.z, snapMin.z)
                : (snapMin.z, snapMax.z);

            return (snapMin, snapMax);
        }

        #endregion
    }
}
