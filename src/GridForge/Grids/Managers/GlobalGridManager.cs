using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace GridForge.Grids;

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
    public static Fixed64 VoxelResolution => VoxelSize * Fixed64.Half;

    #endregion

    #region Properties

    /// <summary>
    /// Collection of all active grids managed by the system.
    /// </summary>
    public static SwiftBucket<VoxelGrid> ActiveGrids { get; private set; }

    /// <summary>
    /// Dictionary mapping exact bounds keys to grid indices to prevent duplicate grids.
    /// </summary>
    public static SwiftDictionary<BoundsKey, ushort> BoundsTracker { get; private set; }

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
    /// Lock for managing concurrent access to grid operations.
    /// Ensures thread safety for read/write operations.
    /// </summary>
    private static readonly ReaderWriterLockSlim _gridLock = new ReaderWriterLockSlim();

    #endregion

    #region Events

    /// <summary>
    /// Event triggered when a new grid is added.
    /// Subscribers can use this to initialize references or update systems dependent on grid presence.
    /// </summary>
    private static Action<GridEventInfo> _onActiveGridAdded;

    /// <inheritdoc cref="_onActiveGridAdded"/>
    public static event Action<GridEventInfo> OnActiveGridAdded
    {
        add => _onActiveGridAdded += value;
        remove => _onActiveGridAdded -= value;
    }

    /// <summary>
    /// Event triggered when a grid is removed.
    /// Subscribers should use this to clean up references or handle the loss of a grid in their systems.
    /// </summary>
    private static Action<GridEventInfo> _onActiveGridRemoved;

    /// <inheritdoc cref="_onActiveGridRemoved"/>
    public static event Action<GridEventInfo> OnActiveGridRemoved
    {
        add => _onActiveGridRemoved += value;
        remove => _onActiveGridRemoved -= value;
    }

    /// <summary>
    /// Event triggered when a grid undergoes a significant change (e.g., structure modification).
    /// Subscribers can use this to react to changes in grid state, such as updating pathfinding data or refreshing visuals.
    /// </summary>
    private static Action<GridEventInfo> _onActiveGridChange;

    /// <inheritdoc cref="_onActiveGridChange"/>
    public static event Action<GridEventInfo> OnActiveGridChange
    {
        add => _onActiveGridChange += value;
        remove => _onActiveGridChange -= value;
    }

    /// <summary>
    /// Event triggered when the GlobalGridManager is reset.
    /// Allows external systems to react to a full grid wipe.
    /// </summary>
    private static Action _onReset;

    /// <inheritdoc cref="_onReset"/>
    public static event Action OnReset
    {
        add => _onReset += value;
        remove => _onReset -= value;
    }

    #endregion

    #region Setup & Reset

    /// <summary>
    /// Initializes necessary collections for managing grids.
    /// </summary>
    public static void Setup() => Setup(DefaultVoxelSize, DefaultSpatialGridCellSize);

    /// <inheritdoc cref="Setup()"/>
    /// <param name="voxelSize"></param>
    /// <param name="spatialGridCellSize"></param>
    public static void Setup(
        Fixed64? voxelSize = null,
        int spatialGridCellSize = DefaultSpatialGridCellSize)
    {
        voxelSize ??= DefaultVoxelSize;

        if (IsActive)
        {
            GridForgeLogger.Warn("Global Grid Manager already active.  Call `Reset` before attempting to setup.");
            return;
        }

        if (voxelSize.Value <= Fixed64.Zero)
        {
            GridForgeLogger.Warn($"Voxel size must be greater than zero. Falling back to default size {DefaultVoxelSize}.");
            VoxelSize = DefaultVoxelSize;
        }
        else
            VoxelSize = voxelSize.Value;

        if (spatialGridCellSize <= 0)
        {
            GridForgeLogger.Warn($"Spatial grid cell size must be greater than zero. Falling back to default size {DefaultSpatialGridCellSize}.");
            SpatialGridCellSize = DefaultSpatialGridCellSize;
        }
        else
            SpatialGridCellSize = spatialGridCellSize;

        ActiveGrids ??= new SwiftBucket<VoxelGrid>();
        BoundsTracker ??= new SwiftDictionary<BoundsKey, ushort>();
        SpatialGridHash ??= new SwiftDictionary<int, SwiftHashSet<ushort>>();

        Version = 1;
        IsActive = true;
    }

    /// <summary>
    /// Resets the global grid manager, clearing all grids and spatial data.
    /// </summary>
    public static void Reset(bool deactivate = false)
    {
        if (!IsActive)
        {
            GridForgeLogger.Warn("Global Grid Manager not active.  Call `Setup` before attempting to reset.");
            return;
        }

        Action resetHandlers = _onReset;
        if (resetHandlers != null)
        {
            var handlerDelegates = resetHandlers.GetInvocationList();
            for (int i = 0; i < handlerDelegates.Length; i++)
            {
                try
                {
                    ((Action)handlerDelegates[i])(); // Fire off before we remove the reference
                }
                catch (Exception ex)
                {
                    GridForgeLogger.Error($"Reset notification error: {ex.Message}");
                }
            }
        }

        if (ActiveGrids != null)
        {
            foreach (VoxelGrid grid in ActiveGrids)
                Pools.GridPool.Release(grid);

            ActiveGrids.Clear();
        }

        BoundsTracker?.Clear();
        SpatialGridHash?.Clear();

        if (deactivate)
            IsActive = false;
    }

    #endregion

    #region Grid Management

    /// <summary>
    /// Adds a new grid to the world and registers it in the spatial hash.
    /// </summary>
    public static bool TryAddGrid(GridConfiguration configuration, out ushort allocatedIndex)
    {
        allocatedIndex = ushort.MaxValue;

        if (!IsActive)
        {
            GridForgeLogger.Error("Global Grid Manager not active.  Call `Setup` first.");
            return false;
        }

        if ((uint)ActiveGrids.Count > MaxGrids)
        {
            GridForgeLogger.Warn($"No more grids can be added at this time.");
            return false;
        }

        if (configuration.BoundsMax < configuration.BoundsMin)
        {
            GridForgeLogger.Error("Invalid Grid Bounds: GridMax must be greater than or equal to GridMin.");
            return false;
        }

        BoundsKey boundsKey = configuration.ToBoundsKey();

        _gridLock.EnterReadLock();
        try
        {
            if (BoundsTracker.TryGetValue(boundsKey, out allocatedIndex))
            {
                GridForgeLogger.Warn("A grid with these bounds has already been allocated.");
                return false;
            }
        }
        finally
        {
            _gridLock.ExitReadLock();
        }

        VoxelGrid newGrid = Pools.GridPool.Rent();
        GridEventInfo addedGridInfo = default;
        _gridLock.EnterWriteLock();
        try
        {
            allocatedIndex = (ushort)ActiveGrids.Add(newGrid);
            BoundsTracker.Add(boundsKey, allocatedIndex);

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
            addedGridInfo = CreateGridEventInfo(newGrid);
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }

        NotifyActiveGridAdded(addedGridInfo);

        return true;
    }

    /// <summary>
    /// Removes a grid and updates all references to ensure integrity.
    /// </summary>
    public static bool TryRemoveGrid(ushort removeIndex)
    {
        if (!IsActive || !ActiveGrids.IsAllocated(removeIndex))
            return false;

        VoxelGrid gridToRemove;
        GridEventInfo removedGridInfo = default;
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

            BoundsKey boundsKey = gridToRemove.Configuration.ToBoundsKey();
            BoundsTracker.Remove(boundsKey);

            ActiveGrids.RemoveAt(removeIndex);

            Version++;
            removedGridInfo = CreateGridEventInfo(gridToRemove);
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }

        // Clearing out neighbor relationships for this voxel handled on `Grid.Reset`
        Pools.GridPool.Release(gridToRemove);

        NotifyActiveGridRemoved(removedGridInfo);

        if (ActiveGrids.Count == 0)
            ActiveGrids.TrimExcessCapacity();

        return true;
    }

    private static GridEventInfo CreateGridEventInfo(VoxelGrid grid)
    {
        return new GridEventInfo(grid.GlobalIndex, grid.SpawnToken, grid.Configuration, grid.Version);
    }

    private static void NotifyActiveGridAdded(GridEventInfo eventInfo)
    {
        Action<GridEventInfo> handlers = _onActiveGridAdded;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<GridEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Grid {eventInfo.GridIndex}] added notification error: {ex.Message}");
            }
        }
    }

    private static void NotifyActiveGridRemoved(GridEventInfo eventInfo)
    {
        Action<GridEventInfo> handlers = _onActiveGridRemoved;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<GridEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Grid {eventInfo.GridIndex}] removed notification error: {ex.Message}");
            }
        }
    }

    internal static void NotifyActiveGridChange(VoxelGrid grid)
    {
        if (grid == null || !grid.IsActive)
            return;

        NotifyActiveGridChange(CreateGridEventInfo(grid));
    }

    private static void NotifyActiveGridChange(GridEventInfo eventInfo)
    {
        Action<GridEventInfo> handlers = _onActiveGridChange;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<GridEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Grid {eventInfo.GridIndex}] change notification error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Notifies grids of a change in their structure.
    /// </summary>
    public static void IncrementGridVersion(int index, bool significant = false)
    {
        if (!IsActive)
        {
            GridForgeLogger.Warn("Global Grid Manager not active.  Call `Setup` first.");
            return;
        }

        _gridLock.EnterWriteLock();
        try
        {
            if (significant)
                Version++;
            if (ActiveGrids.IsAllocated(index))
                ActiveGrids[index].IncrementVersion();
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
        if (!IsActive)
        {
            GridForgeLogger.Warn("Global Grid Manager not active.  Call `Setup` first.");
            return false;
        }

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
        if (!IsActive)
        {
            GridForgeLogger.Warn("Global Grid Manager not active.  Call `Setup` first.");
            return false;
        }

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
                    yield return SwiftHashTools.CombineHashCodes(x, y, z);
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
        SwiftHashSet<VoxelGrid> overlappingGrids = new();

        if (!IsActive)
        {
            GridForgeLogger.Warn("Global Grid Manager not active.  Call `Setup` first.");
            return overlappingGrids;
        }

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
    /// Converts a 3D offset into a corresponding <see cref="SpatialDirection"/> in a 3x3x3 grid.
    /// </summary>
    /// <param name="gridOffset">The (x, y, z) offset from the center voxel.</param>
    /// <returns>The corresponding <see cref="SpatialDirection"/>, or <see cref="SpatialDirection.None"/> if invalid.</returns>
    public static SpatialDirection GetNeighborDirectionFromOffset((int x, int y, int z) gridOffset)
    {
        Debug.Assert(gridOffset.x >= -1 && gridOffset.x <= 1, "Invalid x offset.");
        Debug.Assert(gridOffset.y >= -1 && gridOffset.y <= 1, "Invalid y offset.");
        Debug.Assert(gridOffset.z >= -1 && gridOffset.z <= 1, "Invalid z offset.");

        if (gridOffset == (0, 0, 0))
            return SpatialDirection.None;

        for (int i = 0; i < SpatialAwareness.DirectionOffsets.Length; i++)
        {
            if (SpatialAwareness.DirectionOffsets[i] == gridOffset)
                return (SpatialDirection)i;
        }

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

        return SwiftHashTools.CombineHashCodes(x, y, z);
    }

    /// <summary>
    /// Helper function to ceil snap a <see cref="Vector3d"/> to a grid.
    /// </summary>
    public static Vector3d CeilToVoxelSize(Vector3d position)
    {
        // - Use Abs() to ensure the division is done on positive values, preventing rounding issues.
        // - Apply CeilToInt() to obtain the correct spatial cell index.
        // - Restore the original sign using Sign() after ceiling.
        return new Vector3d(
            (position.x.Abs() / VoxelSize).CeilToInt() * VoxelSize * position.x.Sign(),
            (position.y.Abs() / VoxelSize).CeilToInt() * VoxelSize * position.y.Sign(),
            (position.z.Abs() / VoxelSize).CeilToInt() * VoxelSize * position.z.Sign()
        );
    }

    /// <summary>
    /// Helper function to floor snap a <see cref="Vector3d"/> to a grid.
    /// </summary>
    public static Vector3d FloorToVoxelSize(Vector3d position)
    {
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
        Fixed64? padding = null)
    {
        // Ensure padding is non-negative
        Fixed64 fixedPadding = padding.HasValue && padding.Value > Fixed64.Zero 
            ? padding.Value 
            : Fixed64.Zero;

        min -= fixedPadding;
        max += fixedPadding;

        Vector3d snapMin = FloorToVoxelSize(min);
        Vector3d snapMax = CeilToVoxelSize(max);

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
