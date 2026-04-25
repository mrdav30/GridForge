using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GridForge.Grids;

/// <summary>
/// Owns the mutable runtime state for one GridForge world.
/// </summary>
public sealed class GridWorld : IDisposable
{
    #region Constants

    /// <summary>
    /// Maximum number of grids that can be managed within a world.
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

    #endregion

    #region Properties

    /// <summary>
    /// The size of each grid voxel in world units for this world.
    /// </summary>
    public Fixed64 VoxelSize { get; }

    /// <summary>
    /// The size of a spatial hash cell used for grid lookup in this world.
    /// </summary>
    public int SpatialGridCellSize { get; }

    /// <summary>
    /// Resolution for snapping or searching within the grid.
    /// </summary>
    public Fixed64 VoxelResolution => VoxelSize * Fixed64.Half;

    /// <summary>
    /// Collection of all active grids owned by this world.
    /// </summary>
    public SwiftBucket<VoxelGrid> ActiveGrids { get; }

    /// <summary>
    /// Dictionary mapping exact bounds keys to grid indices to prevent duplicate grids.
    /// </summary>
    public SwiftDictionary<BoundsKey, ushort> BoundsTracker { get; }

    /// <summary>
    /// Dictionary mapping spatial hash keys to grid indices for fast lookups.
    /// </summary>
    public SwiftDictionary<int, SwiftHashSet<ushort>> SpatialGridHash { get; }

    /// <summary>
    /// Runtime token identifying this specific world instance.
    /// </summary>
    public int SpawnToken { get; private set; }

    /// <summary>
    /// The current version of the world, incremented on major changes.
    /// </summary>
    public uint Version { get; private set; }

    /// <summary>
    /// Indicates whether this world is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    private readonly ReaderWriterLockSlim _gridLock = new();

    #endregion

    #region Events

    private Action<GridEventInfo>? _onActiveGridAdded;
    private Action<GridEventInfo>? _onActiveGridRemoved;
    private Action<GridEventInfo>? _onActiveGridChange;
    private Action? _onReset;

    /// <summary>
    /// Event triggered when a new grid is added to this world.
    /// </summary>
    public event Action<GridEventInfo> OnActiveGridAdded
    {
        add => _onActiveGridAdded += value;
        remove => _onActiveGridAdded -= value;
    }

    /// <summary>
    /// Event triggered when a grid is removed from this world.
    /// </summary>
    public event Action<GridEventInfo> OnActiveGridRemoved
    {
        add => _onActiveGridRemoved += value;
        remove => _onActiveGridRemoved -= value;
    }

    /// <summary>
    /// Event triggered when a grid in this world undergoes a significant change.
    /// </summary>
    public event Action<GridEventInfo> OnActiveGridChange
    {
        add => _onActiveGridChange += value;
        remove => _onActiveGridChange -= value;
    }

    /// <summary>
    /// Event triggered when this world is reset.
    /// </summary>
    public event Action OnReset
    {
        add => _onReset += value;
        remove => _onReset -= value;
    }

    #endregion

    /// <summary>
    /// Initializes a new world with the supplied voxel and spatial-hash settings.
    /// </summary>
    /// <param name="voxelSize">Optional voxel size for this world.</param>
    /// <param name="spatialGridCellSize">Optional spatial hash cell size for this world.</param>
    public GridWorld(
        Fixed64? voxelSize = null,
        int spatialGridCellSize = DefaultSpatialGridCellSize)
    {
        ActiveGrids = new SwiftBucket<VoxelGrid>();
        BoundsTracker = new SwiftDictionary<BoundsKey, ushort>();
        SpatialGridHash = new SwiftDictionary<int, SwiftHashSet<ushort>>();

        VoxelSize = ResolveVoxelSize(voxelSize);
        SpatialGridCellSize = ResolveSpatialGridCellSize(spatialGridCellSize);
        SpawnToken = GetHashCode();
        Version = 1;
        IsActive = true;
    }

    #region Lifecycle

    /// <summary>
    /// Clears all grids and spatial data owned by this world.
    /// </summary>
    /// <param name="deactivate">If true, marks the world inactive and releases its event handlers.</param>
    public void Reset(bool deactivate = false)
    {
        if (!IsActive)
        {
            GridForgeLogger.Warn("Grid world not active. Cannot reset an inactive world.");
            return;
        }

        Action? resetHandlers = _onReset;
        if (resetHandlers != null)
        {
            var handlerDelegates = resetHandlers.GetInvocationList();
            for (int i = 0; i < handlerDelegates.Length; i++)
            {
                try
                {
                    ((Action)handlerDelegates[i])();
                }
                catch (Exception ex)
                {
                    GridForgeLogger.Error($"World reset notification error: {ex.Message}");
                }
            }
        }

        foreach (VoxelGrid grid in ActiveGrids)
            Pools.GridPool.Release(grid);

        ActiveGrids.Clear();
        BoundsTracker.Clear();
        SpatialGridHash.Clear();
        GridOccupantManager.ClearTrackedOccupancies(this);

        if (!deactivate)
            return;

        GridOccupantManager.ReleaseTrackedOccupancies(this);
        IsActive = false;
        SpawnToken = 0;
        _onActiveGridAdded = null;
        _onActiveGridRemoved = null;
        _onActiveGridChange = null;
        _onReset = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Reset(deactivate: true);
        _gridLock.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Grid Management

    /// <summary>
    /// Adds a new grid to this world and registers it in the spatial hash.
    /// </summary>
    /// <param name="configuration">The grid configuration to normalize and register.</param>
    /// <param name="allocatedIndex">The allocated world-local grid slot on success.</param>
    /// <returns>True if the grid was added; otherwise false.</returns>
    public bool TryAddGrid(GridConfiguration configuration, out ushort allocatedIndex)
    {
        allocatedIndex = ushort.MaxValue;

        if (!IsActive)
        {
            GridForgeLogger.Error("Grid world not active. Cannot add grids to an inactive world.");
            return false;
        }

        if ((uint)ActiveGrids.Count > MaxGrids)
        {
            GridForgeLogger.Warn("No more grids can be added at this time.");
            return false;
        }

        GridConfiguration normalizedConfiguration = NormalizeConfiguration(configuration);
        BoundsKey boundsKey = normalizedConfiguration.ToBoundsKey();

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

            newGrid.Initialize(this, allocatedIndex, normalizedConfiguration);
            foreach (int cellIndex in GetSpatialGridCells(normalizedConfiguration.BoundsMin, normalizedConfiguration.BoundsMax))
            {
                if (!SpatialGridHash.ContainsKey(cellIndex))
                    SpatialGridHash.Add(cellIndex, new SwiftHashSet<ushort>());

                foreach (ushort neighborIndex in SpatialGridHash[cellIndex])
                {
                    if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == allocatedIndex)
                        continue;

                    VoxelGrid neighborGrid = ActiveGrids[neighborIndex];
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
    /// Removes a grid from this world and updates all references to ensure integrity.
    /// </summary>
    /// <param name="removeIndex">The world-local grid slot to remove.</param>
    /// <returns>True if the grid was removed; otherwise false.</returns>
    public bool TryRemoveGrid(ushort removeIndex)
    {
        if (!IsActive || !ActiveGrids.IsAllocated(removeIndex))
            return false;

        VoxelGrid gridToRemove;
        GridEventInfo removedGridInfo = default;

        _gridLock.EnterWriteLock();
        try
        {
            gridToRemove = ActiveGrids[removeIndex];
            foreach (int cellIndex in GetSpatialGridCells(gridToRemove.BoundsMin, gridToRemove.BoundsMax))
            {
                if (!SpatialGridHash.ContainsKey(cellIndex))
                    continue;

                SpatialGridHash[cellIndex].Remove(gridToRemove.GridIndex);

                if (gridToRemove.IsConjoined)
                {
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

                if (SpatialGridHash[cellIndex].Count == 0)
                    SpatialGridHash.Remove(cellIndex);
            }

            BoundsTracker.Remove(gridToRemove.Configuration.ToBoundsKey());
            ActiveGrids.RemoveAt(removeIndex);

            Version++;
            removedGridInfo = CreateGridEventInfo(gridToRemove);
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }

        Pools.GridPool.Release(gridToRemove);
        NotifyActiveGridRemoved(removedGridInfo);

        if (ActiveGrids.Count == 0)
            ActiveGrids.TrimExcessCapacity();

        return true;
    }

    #endregion

    #region Lookup

    /// <summary>
    /// Retrieves a grid by its world-local index.
    /// </summary>
    /// <param name="index">The world-local grid slot to resolve.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <returns>True if the grid was resolved; otherwise false.</returns>
    public bool TryGetGrid(int index, out VoxelGrid? outGrid)
    {
        outGrid = null;
        if (!IsActive)
        {
            GridForgeLogger.Warn("Grid world not active. Cannot resolve grids.");
            return false;
        }

        if ((uint)index > ActiveGrids.Count)
        {
            GridForgeLogger.Error($"GridIndex '{index}' is out-of-bounds for ActiveGrids.");
            return false;
        }

        if (!ActiveGrids.IsAllocated(index))
        {
            GridForgeLogger.Error($"GridIndex '{index}' has not been allocated to ActiveGrids.");
            return false;
        }

        outGrid = ActiveGrids[index];
        return true;
    }

    /// <summary>
    /// Retrieves the grid containing a given world position.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <returns>True if a containing grid was found; otherwise false.</returns>
    public bool TryGetGrid(Vector3d position, out VoxelGrid? outGrid)
    {
        outGrid = null;
        if (!IsActive)
        {
            GridForgeLogger.Warn("Grid world not active. Cannot resolve positions.");
            return false;
        }

        int cellIndex = GetSpatialGridKey(position);
        if (!SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
            return false;

        foreach (ushort candidateIndex in gridList)
        {
            if (!TryGetGrid(candidateIndex, out VoxelGrid? candidateGrid) || !ActiveGrids[candidateIndex].IsActive)
                continue;

            if (candidateGrid?.IsInBounds(position) == true)
            {
                outGrid = candidateGrid;
                return true;
            }
        }

        GridForgeLogger.Info($"No grid contains position {position}.");
        return false;
    }

    /// <summary>
    /// Retrieves a grid by a world-scoped voxel identity.
    /// </summary>
    /// <param name="worldVoxelIndex">The voxel identity whose grid should be resolved.</param>
    /// <param name="result">The resolved grid, if found.</param>
    /// <returns>True if the grid was resolved; otherwise false.</returns>
    public bool TryGetGrid(WorldVoxelIndex worldVoxelIndex, out VoxelGrid? result)
    {
        result = null;
        if (worldVoxelIndex.WorldSpawnToken != SpawnToken
            || !TryGetGrid(worldVoxelIndex.GridIndex, out VoxelGrid? resolvedGrid)
            || worldVoxelIndex.GridSpawnToken != resolvedGrid?.SpawnToken)
        {
            return false;
        }

        result = resolvedGrid;
        return true;
    }

    /// <summary>
    /// Retrieves the grid and voxel containing a given world position.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <param name="outVoxel">The resolved voxel, if found.</param>
    /// <returns>True if both the grid and voxel were resolved; otherwise false.</returns>
    public bool TryGetGridAndVoxel(
        Vector3d position,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel)
    {
        outVoxel = null;
        return TryGetGrid(position, out outGrid)
            && outGrid?.TryGetVoxel(position, out outVoxel) == true;
    }

    /// <summary>
    /// Retrieves the grid and voxel for a given voxel identity.
    /// </summary>
    /// <param name="worldVoxelIndex">The voxel identity to resolve.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if both the grid and voxel were resolved; otherwise false.</returns>
    public bool TryGetGridAndVoxel(
        WorldVoxelIndex worldVoxelIndex,
        out VoxelGrid? outGrid,
        out Voxel? result)
    {
        result = null;
        return TryGetGrid(worldVoxelIndex, out outGrid)
            && outGrid?.TryGetVoxel(worldVoxelIndex.VoxelIndex, out result) == true;
    }

    /// <summary>
    /// Retrieves a voxel from a world position.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    public bool TryGetVoxel(
        Vector3d position,
        out Voxel? result)
    {
        result = null;
        return TryGetGrid(position, out VoxelGrid? grid)
            && grid?.TryGetVoxel(position, out result) == true;
    }

    /// <summary>
    /// Retrieves a voxel from a world-scoped voxel identity.
    /// </summary>
    /// <param name="worldVoxelIndex">The voxel identity to resolve.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    public bool TryGetVoxel(
        WorldVoxelIndex worldVoxelIndex,
        out Voxel? result)
    {
        result = null;
        return TryGetGrid(worldVoxelIndex, out VoxelGrid? grid)
            && grid?.TryGetVoxel(worldVoxelIndex.VoxelIndex, out result) == true;
    }

    #endregion

    #region Internal Helpers

    internal GridConfiguration NormalizeConfiguration(GridConfiguration configuration)
    {
        (Vector3d boundsMin, Vector3d boundsMax) =
            SnapBoundsToVoxelSize(configuration.BoundsMin, configuration.BoundsMax);

        return new GridConfiguration(boundsMin, boundsMax, configuration.ScanCellSize);
    }

    internal void IncrementGridVersion(int index, bool significant = false)
    {
        if (!IsActive)
        {
            GridForgeLogger.Warn("Grid world not active. Cannot increment grid versions.");
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

    internal IEnumerable<int> GetSpatialGridCells(Vector3d min, Vector3d max)
    {
        (int xMin, int yMin, int zMin) = SnapToSpatialGrid(min);
        (int xMax, int yMax, int zMax) = SnapToSpatialGrid(max);

        (xMin, xMax) = xMin > xMax ? (xMax, xMin) : (xMin, xMax);
        (yMin, yMax) = yMin > yMax ? (yMax, yMin) : (yMin, yMax);
        (zMin, zMax) = zMin > zMax ? (zMax, zMin) : (zMin, zMax);

        for (int z = zMin; z <= zMax; z++)
        {
            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = xMin; x <= xMax; x++)
                    yield return SwiftHashTools.CombineHashCodes(x, y, z);
            }
        }
    }

    internal IEnumerable<VoxelGrid> FindOverlappingGrids(VoxelGrid targetGrid)
    {
        SwiftHashSet<VoxelGrid> overlappingGrids = new();

        if (!IsActive)
        {
            GridForgeLogger.Warn("Grid world not active. Cannot resolve overlaps.");
            return overlappingGrids;
        }

        foreach (int cellIndex in GetSpatialGridCells(targetGrid.BoundsMin, targetGrid.BoundsMax))
        {
            if (!SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                continue;

            foreach (ushort neighborIndex in gridList)
            {
                if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == targetGrid.GridIndex)
                    continue;

                VoxelGrid neighborGrid = ActiveGrids[neighborIndex];
                if (VoxelGrid.IsGridOverlapValid(targetGrid, neighborGrid))
                    overlappingGrids.Add(neighborGrid);
            }
        }

        return overlappingGrids;
    }

    internal int GetSpatialGridKey(Vector3d position)
    {
        (int x, int y, int z) = (
            position.x.FloorToInt() / SpatialGridCellSize,
            position.y.FloorToInt() / SpatialGridCellSize,
            position.z.FloorToInt() / SpatialGridCellSize
        );

        return SwiftHashTools.CombineHashCodes(x, y, z);
    }

    internal Vector3d CeilToVoxelSize(Vector3d position)
    {
        return new Vector3d(
            (position.x.Abs() / VoxelSize).CeilToInt() * VoxelSize * position.x.Sign(),
            (position.y.Abs() / VoxelSize).CeilToInt() * VoxelSize * position.y.Sign(),
            (position.z.Abs() / VoxelSize).CeilToInt() * VoxelSize * position.z.Sign()
        );
    }

    internal Vector3d FloorToVoxelSize(Vector3d position)
    {
        return new Vector3d(
            (position.x.Abs() / VoxelSize).FloorToInt() * VoxelSize * position.x.Sign(),
            (position.y.Abs() / VoxelSize).FloorToInt() * VoxelSize * position.y.Sign(),
            (position.z.Abs() / VoxelSize).FloorToInt() * VoxelSize * position.z.Sign()
        );
    }

    internal (Vector3d min, Vector3d max) SnapBoundsToVoxelSize(
        Vector3d min,
        Vector3d max,
        Fixed64? padding = null)
    {
        Fixed64 fixedPadding = padding.HasValue && padding.Value > Fixed64.Zero
            ? padding.Value
            : Fixed64.Zero;

        min -= fixedPadding;
        max += fixedPadding;

        Vector3d snapMin = FloorToVoxelSize(min);
        Vector3d snapMax = CeilToVoxelSize(max);

        (snapMin.x, snapMax.x) = snapMin.x > snapMax.x ? (snapMax.x, snapMin.x) : (snapMin.x, snapMax.x);
        (snapMin.y, snapMax.y) = snapMin.y > snapMax.y ? (snapMax.y, snapMin.y) : (snapMin.y, snapMax.y);
        (snapMin.z, snapMax.z) = snapMin.z > snapMax.z ? (snapMax.z, snapMin.z) : (snapMin.z, snapMax.z);

        return (snapMin, snapMax);
    }

    internal void NotifyActiveGridChange(VoxelGrid grid)
    {
        if (grid == null || !grid.IsActive)
            return;

        NotifyActiveGridChange(CreateGridEventInfo(grid));
    }

    #endregion

    #region Private Helpers

    private static Fixed64 ResolveVoxelSize(Fixed64? voxelSize)
    {
        Fixed64 resolved = voxelSize ?? DefaultVoxelSize;
        if (resolved <= Fixed64.Zero)
        {
            GridForgeLogger.Warn($"Voxel size must be greater than zero. Falling back to default size {DefaultVoxelSize}.");
            return DefaultVoxelSize;
        }

        return resolved;
    }

    private static int ResolveSpatialGridCellSize(int spatialGridCellSize)
    {
        if (spatialGridCellSize <= 0)
        {
            GridForgeLogger.Warn($"Spatial grid cell size must be greater than zero. Falling back to default size {DefaultSpatialGridCellSize}.");
            return DefaultSpatialGridCellSize;
        }

        return spatialGridCellSize;
    }

    private GridEventInfo CreateGridEventInfo(VoxelGrid grid)
    {
        return new GridEventInfo(SpawnToken, grid.GridIndex, grid.SpawnToken, grid.Configuration, grid.Version);
    }

    private void NotifyActiveGridAdded(GridEventInfo eventInfo)
    {
        Action<GridEventInfo>? handlers = _onActiveGridAdded;
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

    private void NotifyActiveGridRemoved(GridEventInfo eventInfo)
    {
        Action<GridEventInfo>? handlers = _onActiveGridRemoved;
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

    private void NotifyActiveGridChange(GridEventInfo eventInfo)
    {
        Action<GridEventInfo>? handlers = _onActiveGridChange;
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

    private (int xMin, int yMin, int zMin) SnapToSpatialGrid(Vector3d position)
    {
        return (
            (position.x.Abs() / SpatialGridCellSize).FloorToInt() * position.x.Sign(),
            (position.y.Abs() / SpatialGridCellSize).FloorToInt() * position.y.Sign(),
            (position.z.Abs() / SpatialGridCellSize).FloorToInt() * position.z.Sign()
        );
    }

    #endregion
}
