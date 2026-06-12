//=======================================================================
// GridWorld.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Utility;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    /// The default rectangular cell edge in world units.
    /// </summary>
    public static readonly Fixed64 DefaultRectangularCellSize = Fixed64.One;

    /// <summary>
    /// The default size of a spatial hash cell used for grid lookup.
    /// </summary>
    public const int DefaultSpatialGridCellSize = 50;

    #endregion

    #region Properties

    /// <summary>
    /// The size of a spatial hash cell used for grid lookup in this world.
    /// </summary>
    public int SpatialGridCellSize { get; }

    /// <summary>
    /// Collection of all active grids owned by this world.
    /// </summary>
    public SwiftBucket<VoxelGrid> ActiveGrids { get; }

    /// <summary>
    /// Dictionary mapping exact grid configuration keys to grid indices to prevent duplicate grids.
    /// </summary>
    public SwiftDictionary<GridConfigurationKey, ushort> BoundsTracker { get; }

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

    internal Fixed64 MaxTopologyCellEdge { get; private set; }

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
    /// Initializes a new world with the supplied spatial-hash settings.
    /// </summary>
    /// <param name="spatialGridCellSize">Optional spatial hash cell size for this world.</param>
    public GridWorld(int spatialGridCellSize = DefaultSpatialGridCellSize)
    {
        ActiveGrids = new SwiftBucket<VoxelGrid>();
        BoundsTracker = new SwiftDictionary<GridConfigurationKey, ushort>();
        SpatialGridHash = new SwiftDictionary<int, SwiftHashSet<ushort>>();

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
            GridForgeLogger.Channel.Warn($"Grid world not active. Cannot reset an inactive world.");
            return;
        }

        NotifyResetHandlers();
        ReleaseActiveGrids();
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

    private void NotifyResetHandlers()
    {
        Action? resetHandlers = _onReset;
        if (resetHandlers == null)
            return;

        var handlerDelegates = resetHandlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action)handlerDelegates[i])();
            }
            catch (Exception ex)
            {
                GridForgeLogger.Channel.Error($"World reset notification error: {ex.Message}");
            }
        }
    }

    private void ReleaseActiveGrids()
    {
        foreach (VoxelGrid grid in ActiveGrids)
            Pools.GridPool.Release(grid);

        ActiveGrids.Clear();
        BoundsTracker.Clear();
        SpatialGridHash.Clear();
        MaxTopologyCellEdge = Fixed64.Zero;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAddGrid(GridConfiguration configuration, out ushort allocatedIndex) =>
        TryAddGridCore(configuration, null, null, out allocatedIndex);

    /// <summary>
    /// Adds a new grid to this world and materializes the supplied sparse voxel indices when sparse storage is configured.
    /// </summary>
    /// <param name="configuration">The grid configuration to normalize and register.</param>
    /// <param name="configuredVoxels">Grid-local voxel indices to materialize for sparse storage.</param>
    /// <param name="allocatedIndex">The allocated world-local grid slot on success.</param>
    /// <returns>True if the grid was added; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAddGrid(
        GridConfiguration configuration,
        IEnumerable<VoxelIndex>? configuredVoxels,
        out ushort allocatedIndex) =>
        TryAddGridCore(configuration, configuredVoxels, null, out allocatedIndex);

    /// <summary>
    /// Adds a new grid to this world and materializes true cells from the supplied sparse voxel mask when sparse storage is configured.
    /// </summary>
    /// <param name="configuration">The grid configuration to normalize and register.</param>
    /// <param name="configuredVoxels">A [x, y, z] mask whose true values identify sparse voxels to materialize.</param>
    /// <param name="allocatedIndex">The allocated world-local grid slot on success.</param>
    /// <returns>True if the grid was added; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAddGrid(
        GridConfiguration configuration,
        bool[,,]? configuredVoxels,
        out ushort allocatedIndex) =>
        TryAddGridCore(configuration, null, configuredVoxels, out allocatedIndex);

    private bool TryAddGridCore(
        GridConfiguration configuration,
        IEnumerable<VoxelIndex>? configuredVoxels,
        bool[,,]? configuredVoxelMask,
        out ushort allocatedIndex)
    {
        allocatedIndex = ushort.MaxValue;

        if (!CanAddGrid())
            return false;

        if (!TryNormalizeConfiguration(configuration, out GridConfiguration normalizedConfiguration))
            return false;

        if (!TryGetConfigurationDimensions(normalizedConfiguration, out GridDimensions dimensions)
            || !TryValidateGridDimensions(dimensions))
        {
            return false;
        }

        if (!TryPrepareConfiguredVoxels(
            normalizedConfiguration,
            dimensions,
            configuredVoxels,
            configuredVoxelMask,
            out VoxelIndex[] preparedVoxels))
        {
            return false;
        }

        GridConfigurationKey boundsKey = normalizedConfiguration.ToGridKey();

        if (TryFindExistingGrid(boundsKey, out allocatedIndex))
            return false;

        VoxelGrid newGrid = Pools.GridPool.Rent();
        GridEventInfo addedGridInfo = default;

        _gridLock.EnterWriteLock();
        try
        {
            allocatedIndex = (ushort)ActiveGrids.Add(newGrid);
            BoundsTracker.Add(boundsKey, allocatedIndex);

            newGrid.Initialize(this, allocatedIndex, normalizedConfiguration, preparedVoxels);
            UpdateMaxTopologyCellEdge(newGrid.Topology.MaxCellEdge);
            RegisterGridSpatialCells(newGrid, allocatedIndex);

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
            Fixed64 removedMaxCellEdge = gridToRemove.Topology.MaxCellEdge;
            UnregisterGridSpatialCells(gridToRemove, removeIndex);
            BoundsTracker.Remove(gridToRemove.Configuration.ToGridKey());
            ActiveGrids.RemoveAt(removeIndex);
            RecalculateMaxTopologyCellEdgeIfNeeded(removedMaxCellEdge);

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

    private bool CanAddGrid()
    {
        if (!IsActive)
        {
            GridForgeLogger.Channel.Error($"Grid world not active. Cannot add grids to an inactive world.");
            return false;
        }

        if ((uint)ActiveGrids.Count > MaxGrids)
        {
            GridForgeLogger.Channel.Warn($"No more grids can be added at this time.");
            return false;
        }

        return true;
    }

    private static bool TryPrepareConfiguredVoxels(
        GridConfiguration configuration,
        GridDimensions dimensions,
        IEnumerable<VoxelIndex>? configuredVoxels,
        bool[,,]? configuredVoxelMask,
        out VoxelIndex[] preparedVoxels)
    {
        preparedVoxels = Array.Empty<VoxelIndex>();
        if (configuration.StorageKind != GridStorageKind.Sparse)
            return true;

        if (configuredVoxelMask != null)
            return TryPrepareConfiguredVoxelMask(configuredVoxelMask, dimensions, out preparedVoxels);

        return TryPrepareConfiguredVoxelIndices(configuredVoxels, dimensions, out preparedVoxels);
    }

    private static bool TryGetConfigurationDimensions(GridConfiguration configuration, out GridDimensions dimensions)
    {
        dimensions = default;
        if (!GridTopologyFactory.TryCreate(configuration, out IGridTopology? topology))
            return false;

        dimensions = topology!.CalculateDimensions(configuration.BoundsMin, configuration.BoundsMax);
        return true;
    }

    private static bool TryValidateGridDimensions(GridDimensions dimensions)
    {
        if (dimensions.Width <= 0 || dimensions.Height <= 0 || dimensions.Length <= 0)
        {
            GridForgeLogger.Channel.Warn($"Grid dimensions must be positive.");
            return false;
        }

        long layerSize = (long)dimensions.Width * dimensions.Height;
        if (layerSize > int.MaxValue || layerSize * dimensions.Length > int.MaxValue)
        {
            GridForgeLogger.Channel.Warn($"Grid dimensions exceed the supported int voxel address space.");
            return false;
        }

        return true;
    }

    private static bool TryPrepareConfiguredVoxelMask(
        bool[,,] configuredVoxelMask,
        GridDimensions dimensions,
        out VoxelIndex[] preparedVoxels)
    {
        preparedVoxels = Array.Empty<VoxelIndex>();

        if (configuredVoxelMask.GetLength(0) != dimensions.Width
            || configuredVoxelMask.GetLength(1) != dimensions.Height
            || configuredVoxelMask.GetLength(2) != dimensions.Length)
        {
            GridForgeLogger.Channel.Warn($"Sparse voxel mask dimensions must match normalized grid dimensions.");
            return false;
        }

        int configuredCount = 0;
        for (int x = 0; x < dimensions.Width; x++)
        {
            for (int y = 0; y < dimensions.Height; y++)
            {
                for (int z = 0; z < dimensions.Length; z++)
                {
                    if (configuredVoxelMask[x, y, z])
                        configuredCount++;
                }
            }
        }

        if (configuredCount == 0)
            return true;

        preparedVoxels = new VoxelIndex[configuredCount];
        int index = 0;
        for (int x = 0; x < dimensions.Width; x++)
        {
            for (int y = 0; y < dimensions.Height; y++)
            {
                for (int z = 0; z < dimensions.Length; z++)
                {
                    if (configuredVoxelMask[x, y, z])
                        preparedVoxels[index++] = new VoxelIndex(x, y, z);
                }
            }
        }

        return true;
    }

    private static bool TryPrepareConfiguredVoxelIndices(
        IEnumerable<VoxelIndex>? configuredVoxels,
        GridDimensions dimensions,
        out VoxelIndex[] preparedVoxels)
    {
        preparedVoxels = Array.Empty<VoxelIndex>();
        if (configuredVoxels == null)
            return true;

        SwiftList<VoxelIndex> indices = configuredVoxels is ICollection<VoxelIndex> collection
            ? new SwiftList<VoxelIndex>(collection.Count)
            : new SwiftList<VoxelIndex>();

        foreach (VoxelIndex configuredVoxel in configuredVoxels)
        {
            if (!IsConfiguredVoxelInBounds(configuredVoxel, dimensions))
            {
                GridForgeLogger.Channel.Warn($"Sparse voxel index {configuredVoxel} is outside normalized grid dimensions.");
                return false;
            }

            indices.Add(configuredVoxel);
        }

        if (indices.Count == 0)
            return true;

        preparedVoxels = indices.ToArray();
        Array.Sort(preparedVoxels, VoxelIndexComparer.Instance);
        CompactPreparedVoxels(ref preparedVoxels);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsConfiguredVoxelInBounds(VoxelIndex voxelIndex, GridDimensions dimensions) =>
        (uint)voxelIndex.x < (uint)dimensions.Width
        && (uint)voxelIndex.y < (uint)dimensions.Height
        && (uint)voxelIndex.z < (uint)dimensions.Length;

    private static void CompactPreparedVoxels(ref VoxelIndex[] preparedVoxels)
    {
        if (preparedVoxels.Length < 2)
            return;

        int writeIndex = 1;
        VoxelIndex previous = preparedVoxels[0];
        for (int readIndex = 1; readIndex < preparedVoxels.Length; readIndex++)
        {
            VoxelIndex current = preparedVoxels[readIndex];
            if (current == previous)
                continue;

            preparedVoxels[writeIndex++] = current;
            previous = current;
        }

        if (writeIndex != preparedVoxels.Length)
            Array.Resize(ref preparedVoxels, writeIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateMaxTopologyCellEdge(Fixed64 candidate)
    {
        if (candidate > MaxTopologyCellEdge)
            MaxTopologyCellEdge = candidate;
    }

    private void RecalculateMaxTopologyCellEdgeIfNeeded(Fixed64 removedMaxCellEdge)
    {
        if (removedMaxCellEdge < MaxTopologyCellEdge)
            return;

        Fixed64 maxCellEdge = Fixed64.Zero;
        foreach (VoxelGrid grid in ActiveGrids)
        {
            if (grid != null && grid.IsActive && grid.Topology.MaxCellEdge > maxCellEdge)
                maxCellEdge = grid.Topology.MaxCellEdge;
        }

        MaxTopologyCellEdge = maxCellEdge;
    }

    private bool TryFindExistingGrid(GridConfigurationKey boundsKey, out ushort allocatedIndex)
    {
        _gridLock.EnterReadLock();
        try
        {
            if (BoundsTracker.TryGetValue(boundsKey, out allocatedIndex))
            {
                GridForgeLogger.Channel.Warn($"A grid with these bounds has already been allocated.");
                return true;
            }
        }
        finally
        {
            _gridLock.ExitReadLock();
        }

        allocatedIndex = ushort.MaxValue;
        return false;
    }

    private void RegisterGridSpatialCells(VoxelGrid newGrid, ushort allocatedIndex)
    {
        foreach (int cellIndex in GetSpatialGridCells(newGrid.BoundsMin, newGrid.BoundsMax))
        {
            if (!SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
            {
                gridList = new SwiftHashSet<ushort>();
                SpatialGridHash.Add(cellIndex, gridList);
            }

            LinkGridWithCellNeighbors(newGrid, allocatedIndex, gridList);
            gridList.Add(allocatedIndex);
        }
    }

    private void LinkGridWithCellNeighbors(
        VoxelGrid newGrid,
        ushort allocatedIndex,
        SwiftHashSet<ushort> gridList)
    {
        foreach (ushort neighborIndex in gridList)
        {
            if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == allocatedIndex)
                continue;

            VoxelGrid neighborGrid = ActiveGrids[neighborIndex];
            if (!VoxelGrid.IsGridOverlapValid(newGrid, neighborGrid))
                continue;

            newGrid.TryAddGridNeighbor(neighborGrid);
            neighborGrid.TryAddGridNeighbor(newGrid);
        }
    }

    private void UnregisterGridSpatialCells(VoxelGrid gridToRemove, ushort removeIndex)
    {
        foreach (int cellIndex in GetSpatialGridCells(gridToRemove.BoundsMin, gridToRemove.BoundsMax))
        {
            if (!SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                continue;

            gridList.Remove(gridToRemove.GridIndex);

            if (gridToRemove.IsConjoined)
                UnlinkGridCellNeighbors(gridToRemove, removeIndex, gridList);

            if (gridList.Count == 0)
                SpatialGridHash.Remove(cellIndex);
        }
    }

    private void UnlinkGridCellNeighbors(
        VoxelGrid gridToRemove,
        ushort removeIndex,
        SwiftHashSet<ushort> gridList)
    {
        foreach (ushort neighborIndex in gridList)
        {
            if (!ActiveGrids.IsAllocated(neighborIndex) || neighborIndex == removeIndex)
                continue;

            VoxelGrid neighborGrid = ActiveGrids[neighborIndex];
            if (VoxelGrid.IsGridOverlapValid(gridToRemove, neighborGrid))
                neighborGrid.TryRemoveGridNeighbor(gridToRemove);
        }
    }

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
        if (!CanResolveGrid(index))
            return false;

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
        if (!TryGetSpatialGridCandidates(position, out SwiftHashSet<ushort>? gridList))
            return false;

        if (TryGetContainingGrid(position, gridList!, out outGrid))
            return true;

        GridForgeLogger.Channel.Info($"No grid contains position {position}.");
        return false;
    }

    /// <summary>
    /// Retrieves the grid containing a 2D XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <returns>True if a containing grid was found; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetGrid(Vector2d position, out VoxelGrid? outGrid) =>
         TryGetGrid(position, default, out outGrid);

    /// <summary>
    /// Retrieves the grid containing a 2D XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <returns>True if a containing grid was found; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetGrid(Vector2d position, Fixed64 layerY, out VoxelGrid? outGrid) =>
        TryGetGrid(GridPlane2d.ToWorld(position, layerY), out outGrid);

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
    /// Retrieves the grid and voxel containing a 2D XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <param name="outVoxel">The resolved voxel, if found.</param>
    /// <returns>True if both the grid and voxel were resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetGridAndVoxel(
        Vector2d position,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel) =>
         TryGetGridAndVoxel(position, default, out outGrid, out outVoxel);

    /// <summary>
    /// Retrieves the grid and voxel containing a 2D XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="outGrid">The resolved grid, if found.</param>
    /// <param name="outVoxel">The resolved voxel, if found.</param>
    /// <returns>True if both the grid and voxel were resolved; otherwise false.</returns>  
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetGridAndVoxel(
        Vector2d position,
        Fixed64 layerY,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel) =>
         TryGetGridAndVoxel(GridPlane2d.ToWorld(position, layerY), out outGrid, out outVoxel);

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
    /// Retrieves a voxel from a 2D XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    public bool TryGetVoxel(
        Vector2d position,
        out Voxel? result)
    {
        return TryGetVoxel(position, default, out result);
    }

    /// <summary>
    /// Retrieves a voxel from a 2D XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    public bool TryGetVoxel(
        Vector2d position,
        Fixed64 layerY,
        out Voxel? result)
    {
        return TryGetVoxel(GridPlane2d.ToWorld(position, layerY), out result);
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

    internal static bool TryNormalizeConfiguration(GridConfiguration configuration, out GridConfiguration normalizedConfiguration)
    {
        normalizedConfiguration = default;
        if (!GridTopologyFactory.TryCreate(configuration, out IGridTopology? topology))
            return false;

        (Vector3d boundsMin, Vector3d boundsMax) =
            topology!.NormalizeBounds(configuration.BoundsMin, configuration.BoundsMax);

        normalizedConfiguration = new GridConfiguration(
            boundsMin,
            boundsMax,
            configuration.ScanCellSize,
            configuration.TopologyKind,
            configuration.TopologyMetrics,
            configuration.StorageKind);
        return true;
    }

    /// <summary>
    /// Increments the version of the specified grid and optionally the world version.
    /// </summary>
    public void IncrementGridVersion(int index, bool significant = false)
    {
        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"Grid world not active. Cannot increment grid versions.");
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

    /// <summary>
    /// Enumerates the spatial-hash cells that intersect the supplied bounds.
    /// </summary>
    public IEnumerable<int> GetSpatialGridCells(Vector3d min, Vector3d max)
    {
        (int xMin, int yMin, int zMin, int xMax, int yMax, int zMax) = GetSpatialGridCellBounds(min, max);

        for (int z = zMin; z <= zMax; z++)
        {
            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = xMin; x <= xMax; x++)
                    yield return SwiftHashTools.CombineHashCodes(x, y, z);
            }
        }
    }

    /// <summary>
    /// Computes normalized spatial-hash cell bounds for the supplied world-space bounds.
    /// </summary>
    internal (int xMin, int yMin, int zMin, int xMax, int yMax, int zMax) GetSpatialGridCellBounds(
        Vector3d min,
        Vector3d max)
    {
        (int xMin, int yMin, int zMin) = SnapToSpatialGrid(min);
        (int xMax, int yMax, int zMax) = SnapToSpatialGrid(max);

        (xMin, xMax) = xMin > xMax ? (xMax, xMin) : (xMin, xMax);
        (yMin, yMax) = yMin > yMax ? (yMax, yMin) : (yMin, yMax);
        (zMin, zMax) = zMin > zMax ? (zMax, zMin) : (zMin, zMax);

        return (xMin, yMin, zMin, xMax, yMax, zMax);
    }

    /// <summary>
    /// Finds active grids in this world that overlap the supplied target grid.
    /// </summary>
    public IEnumerable<VoxelGrid> FindOverlappingGrids(VoxelGrid targetGrid)
    {
        SwiftHashSet<VoxelGrid> overlappingGrids = new();

        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"Grid world not active. Cannot resolve overlaps.");
            return overlappingGrids;
        }

        foreach (int cellIndex in GetSpatialGridCells(targetGrid.BoundsMin, targetGrid.BoundsMax))
            AddOverlappingGridsFromCell(targetGrid, cellIndex, overlappingGrids);

        return overlappingGrids;
    }

    private bool CanResolveGrid(int index)
    {
        if (!CanResolveActiveGrid())
            return false;

        if (!IsGridIndexInActiveRange(index))
        {
            GridForgeLogger.Channel.Error($"GridIndex '{index}' is out-of-bounds for ActiveGrids.");
            return false;
        }

        return IsGridIndexAllocated(index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanResolveActiveGrid()
    {
        if (IsActive)
            return true;

        GridForgeLogger.Channel.Warn($"Grid world not active. Cannot resolve grids.");
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsGridIndexInActiveRange(int index) =>
         (uint)index < MaxGrids && (uint)index <= ActiveGrids.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsGridIndexAllocated(int index)
    {
        if (ActiveGrids.IsAllocated(index))
            return true;

        GridForgeLogger.Channel.Error($"GridIndex '{index}' has not been allocated to ActiveGrids.");
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetSpatialGridCandidates(
        Vector3d position,
        out SwiftHashSet<ushort>? gridList)
    {
        gridList = null;
        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"Grid world not active. Cannot resolve positions.");
            return false;
        }

        return SpatialGridHash.TryGetValue(GetSpatialGridKey(position), out gridList);
    }

    private bool TryGetContainingGrid(
        Vector3d position,
        SwiftHashSet<ushort> gridList,
        out VoxelGrid? outGrid)
    {
        outGrid = null;

        foreach (ushort candidateIndex in gridList)
        {
            if (TryGetActiveGridCandidate(candidateIndex, out VoxelGrid? candidateGrid)
                && candidateGrid!.IsInBounds(position))
            {
                outGrid = candidateGrid;
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetActiveGridCandidate(ushort gridIndex, out VoxelGrid? grid)
    {
        grid = null;
        if (!ActiveGrids.IsAllocated(gridIndex))
            return false;

        grid = ActiveGrids[gridIndex];
        return grid.IsActive;
    }

    private void AddOverlappingGridsFromCell(
        VoxelGrid targetGrid,
        int cellIndex,
        SwiftHashSet<VoxelGrid> overlappingGrids)
    {
        if (!SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
            return;

        foreach (ushort neighborIndex in gridList)
            TryAddOverlappingGrid(targetGrid, neighborIndex, overlappingGrids);
    }

    private void TryAddOverlappingGrid(
        VoxelGrid targetGrid,
        ushort neighborIndex,
        SwiftHashSet<VoxelGrid> overlappingGrids)
    {
        if (neighborIndex == targetGrid.GridIndex
            || !TryGetActiveGridCandidate(neighborIndex, out VoxelGrid? neighborGrid))
        {
            return;
        }

        if (VoxelGrid.IsGridOverlapValid(targetGrid, neighborGrid!))
            overlappingGrids.Add(neighborGrid!);
    }

    /// <summary>
    /// Computes the spatial-hash key for the supplied world-space position.
    /// </summary>
    public int GetSpatialGridKey(Vector3d position)
    {
        (int x, int y, int z) = (
            position.X.FloorToInt() / SpatialGridCellSize,
            position.Y.FloorToInt() / SpatialGridCellSize,
            position.Z.FloorToInt() / SpatialGridCellSize
        );

        return SwiftHashTools.CombineHashCodes(x, y, z);
    }

    internal void NotifyActiveGridChange(VoxelGrid grid)
    {
        if (grid == null || !grid.IsActive)
            return;

        NotifyActiveGridChange(CreateGridEventInfo(grid));
    }

    #endregion

    #region Private Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ResolveSpatialGridCellSize(int spatialGridCellSize)
    {
        if (spatialGridCellSize <= 0)
        {
            GridForgeLogger.Channel.Warn($"Spatial grid cell size must be greater than zero. Falling back to default size {DefaultSpatialGridCellSize}.");
            return DefaultSpatialGridCellSize;
        }

        return spatialGridCellSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GridEventInfo CreateGridEventInfo(VoxelGrid grid) =>
         new(SpawnToken, grid.GridIndex, grid.SpawnToken, grid.Configuration, grid.Version);

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
                GridForgeLogger.Channel.Error($"[Grid {eventInfo.GridIndex}] added notification error: {ex.Message}");
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
                GridForgeLogger.Channel.Error($"[Grid {eventInfo.GridIndex}] removed notification error: {ex.Message}");
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
                GridForgeLogger.Channel.Error($"[Grid {eventInfo.GridIndex}] change notification error: {ex.Message}");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int xMin, int yMin, int zMin) SnapToSpatialGrid(Vector3d position)
    {
        return (
            (position.X.Abs() / SpatialGridCellSize).FloorToInt() * position.X.Sign(),
            (position.Y.Abs() / SpatialGridCellSize).FloorToInt() * position.Y.Sign(),
            (position.Z.Abs() / SpatialGridCellSize).FloorToInt() * position.Z.Sign()
        );
    }

    private sealed class VoxelIndexComparer : IComparer<VoxelIndex>
    {
        public static readonly VoxelIndexComparer Instance = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(VoxelIndex left, VoxelIndex right)
        {
            int result = left.x.CompareTo(right.x);
            if (result != 0)
                return result;

            result = left.y.CompareTo(right.y);
            return result != 0 ? result : left.z.CompareTo(right.z);
        }
    }

    #endregion
}
