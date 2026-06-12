using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Dimensions;
using SwiftCollections.Pool;
using SwiftCollections.Utility;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GridForge.Grids;

/// <summary>
/// Represents a 3D grid structure for spatial organization, managing voxels and scan cells.
/// Handles initialization, neighbor relationships, and occupancy tracking.
/// </summary>
public class VoxelGrid
{
    #region Fields & Properties

    /// <summary>
    /// Unique token identifying the grid instance.
    /// </summary>
    public int SpawnToken { get; private set; }

    /// <summary>
    /// World-local index of the grid within its owning world.
    /// </summary>
    public ushort GridIndex { get; private set; }

    /// <summary>
    /// The world that owns this grid instance.
    /// </summary>
    public GridWorld? World { get; private set; }

    /// <summary>
    /// Synchronizes obstacle mutations for this grid.
    /// </summary>
    internal object ObstacleSyncRoot { get; } = new object();

    /// <summary>
    /// Synchronizes occupant mutations for this grid.
    /// </summary>
    internal object OccupantSyncRoot { get; } = new object();

    /// <inheritdoc cref="GridConfiguration"/>
    public GridConfiguration Configuration { get; private set; }

    /// <summary>
    /// Minimum bounds of the grid in world coordinates.
    /// </summary>
    public Vector3d BoundsMin => Configuration.BoundsMin;

    /// <summary>
    /// Maximum bounds of the grid in world coordinates.
    /// </summary>
    public Vector3d BoundsMax => Configuration.BoundsMax;

    /// <summary>
    /// Center position of the grid in world space.
    /// </summary>
    public Vector3d BoundsCenter => Configuration.GridCenter;

    /// <summary>
    /// Grid width in number of voxels.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Grid height in number of voxels.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Grid length in number of voxels.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Total number of voxels within the grid.
    /// </summary>
    public int Size { get; private set; }

    /// <summary>
    /// The primary 3D collection of voxels managed by this grid.
    /// </summary>
    public SwiftArray3D<Voxel>? Voxels { get; private set; }

    /// <summary>
    /// Stores <see cref="SpatialDirection"/> as indices of neighboring grids based on their relative positions.
    /// </summary>
    /// <remarks>
    /// Unlike voxel adjacency (which is always 1:1), grids can share multiple neighbors in the same direction.
    /// </remarks>
    public SwiftSparseMap<SwiftHashSet<int>>? Neighbors { get; private set; }

    /// <summary>
    /// Count of currently linked neighboring grids.
    /// </summary>
    public byte NeighborCount { get; private set; }

    /// <summary>
    /// Determines whether this grid has any linked neighbors.
    /// </summary>
    public bool IsConjoined => Neighbors != null && NeighborCount > 0;

    /// <summary>
    /// Size of a scan cell used for spatial partitioning.
    /// </summary>
    public int ScanCellSize => Configuration.ScanCellSize;

    /// <summary>
    /// Collection of scan cells indexed by their grid-local scan cell key.
    /// </summary>
    public SwiftSparseMap<ScanCell>? ScanCells { get; private set; }

    /// <summary>
    /// Stores currently active (occupied) scan cells within the grid.
    /// </summary>
    public SwiftHashSet<int>? ActiveScanCells { get; internal set; }

    /// <summary>
    /// Indicates whether the grid is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Determines whether the grid is occupied (active and containing occupants).
    /// </summary>
    public bool IsOccupied => ActiveScanCells?.Count > 0;

    /// <summary>
    /// Tracks the number of obstacles currently registered in the grid.
    /// </summary>
    public int ObstacleCount { get; internal set; }

    /// <summary>
    /// Tracks the version of the grid, incremented when a <see cref="Voxel"/> is modified.
    /// </summary>
    public uint Version { get; private set; }

    private int _scanWidth;
    private int _scanHeight;
    private int _scanLength;
    private int _scanLayerSize;

    private IGridTopology? _topology;

    internal IGridTopology Topology => _topology!;

    #endregion

    #region Initialization & Reset

    /// <summary>
    /// Initializes the grid with an explicit owning world.
    /// </summary>
    /// <param name="world">The world that will own this grid.</param>
    /// <param name="gridIndex">The unique index of this grid in the world.</param>
    /// <param name="configuration">The normalized configuration settings for the grid.</param>
    internal void Initialize(GridWorld world, ushort gridIndex, GridConfiguration configuration)
    {
        if (IsActive)
        {
            GridForgeLogger.Channel.Warn($"Grid at {nameof(gridIndex)} is already active.");
            return;
        }

        Version = 1;

        World = world;
        GridIndex = gridIndex;

        Configuration = configuration;
        if (!GridTopologyFactory.TryCreate(configuration, out IGridTopology? topology))
        {
            World = null;
            GridIndex = ushort.MaxValue;
            Configuration = default;
            Version = 0;
            return;
        }

        _topology = topology;

        SpawnToken = GetHashCode();

        // +1 to account for inclusive bounds and to ensure that even the smallest grids (1x1x1) remain valid.
        GridDimensions dimensions = topology!.CalculateDimensions(BoundsMin, BoundsMax);
        Width = dimensions.Width;
        Height = dimensions.Height;
        Length = dimensions.Length;
        Size = Width * Height * Length;

        GenerateScanCells();
        GenerateVoxels();

        IsActive = true;
    }

    /// <summary>
    /// Resets the grid, clearing all voxels and scan cells.
    /// </summary>
    internal void Reset()
    {
        if (!IsActive)
            return;

        ReleaseVoxels();

        // Just in case since voxels should have already cleared any registered obstacles.
        ObstacleCount = 0;

        ReleaseScanCells();
        ReleaseActiveScanCells();
        ReleaseNeighbors();

        Configuration = default;
        World = null;
        _topology = null;

        SpawnToken = 0;
        Version = 0;

        GridIndex = ushort.MaxValue;

        ClearDimensions();

        IsActive = false;
    }

    private void ReleaseVoxels()
    {
        if (Voxels == null)
            return;

        foreach (Voxel voxel in Voxels)
        {
            if (voxel == null)
                continue;

            voxel.Reset(this);
            Pools.VoxelPool.Release(voxel);
        }

        Voxels = null;
    }

    private void ReleaseScanCells()
    {
        if (ScanCells == null)
            return;

        foreach (ScanCell cell in ScanCells.Values)
        {
            if (cell == null)
                continue;

            Pools.ScanCellPool.Release(cell);
        }

        Pools.ScanCellMapPool.Release(ScanCells);
        ScanCells = null;
    }

    private void ReleaseActiveScanCells()
    {
        if (ActiveScanCells == null)
            return;

        SwiftHashSetPool<int>.Shared.Release(ActiveScanCells);
        ActiveScanCells = null;
    }

    private void ReleaseNeighbors()
    {
        if (Neighbors == null)
            return;

        foreach (SwiftHashSet<int> neighbors in Neighbors.Values)
        {
            if (neighbors == null)
                continue;

            SwiftHashSetPool<int>.Shared.Release(neighbors);
        }

        Neighbors = null;
        NeighborCount = 0;
    }

    private void ClearDimensions()
    {
        Width = 0;
        Height = 0;
        Length = 0;
        Size = 0;
        _scanWidth = 0;
        _scanHeight = 0;
        _scanLength = 0;
        _scanLayerSize = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint IncrementVersion()
    {
        Version = Version == uint.MaxValue ? 1u : Version + 1u;
        return Version;
    }

    #endregion

    #region Grid Construction

    /// <summary>
    /// Generates the scan cell overlay for the grid.
    /// </summary>
    private void GenerateScanCells()
    {
        _scanWidth = ((Width - 1) / ScanCellSize) + 1;
        _scanHeight = ((Height - 1) / ScanCellSize) + 1;
        _scanLength = ((Length - 1) / ScanCellSize) + 1;
        _scanLayerSize = _scanWidth * _scanHeight;

        ScanCells = Pools.ScanCellMapPool.Rent();

        for (int x = 0; x < _scanWidth; x++)
        {
            for (int y = 0; y < _scanHeight; y++)
            {
                for (int z = 0; z < _scanLength; z++)
                {
                    int cellKey = x + y * _scanWidth + z * _scanLayerSize;

                    ScanCell scanCell = Pools.ScanCellPool.Rent();
                    scanCell.Initialize(World!, GridIndex, cellKey);
                    ScanCells.Add(cellKey, scanCell);
                }
            }
        }
    }

    /// <summary>
    /// Generates the 3D grid structure based on the configured settings.
    /// </summary>
    private void GenerateVoxels()
    {
#if DEBUG
        long startMem = GC.GetTotalMemory(true);
#endif

        Voxels = new SwiftArray3D<Voxel>(Width, Height, Length);

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int z = 0; z < Length; z++)
                {
                    VoxelIndex index = new(x, y, z);
                    Vector3d position = Topology.GetWorldPosition(BoundsMin, index);

                    // Rent a voxel from the object pool and initialize it
                    Voxel voxel = Pools.VoxelPool.Rent();

                    bool isBoundaryVoxel = IsOnBoundary(index);
                    int scanCellKey = GetScanCellKey(index);

                    voxel.Initialize(
                        new WorldVoxelIndex(World!.SpawnToken, GridIndex, SpawnToken, index),
                        position,
                        scanCellKey,
                        isBoundaryVoxel,
                        Version);

                    Voxels[x, y, z] = voxel;
                }
            }
        }

#if DEBUG
        long usedMem = GC.GetTotalMemory(true) - startMem;
        GridForgeLogger.Channel.Info($"Grid generated using {usedMem} Bytes.");
#endif
    }

    #endregion

    #region Boundary Management

    /// <summary>
    /// Determines the relative direction of a neighboring grid based on its center offset.
    /// </summary>
    /// <param name="a">The first grid.</param>
    /// <param name="b">The second grid.</param>
    /// <returns>The direction from grid 'a' to grid 'b'.</returns>
    public static SpatialDirection GetNeighborDirection(VoxelGrid a, VoxelGrid b)
    {
        Vector3d centerDifference = b.BoundsCenter - a.BoundsCenter;
        (int x, int y, int z) gridOffset = (
                centerDifference.X.Sign(),
                centerDifference.Y.Sign(),
                centerDifference.Z.Sign()
            );
        return GridDirectionUtility.GetNeighborDirectionFromOffset(gridOffset);
    }

    /// <summary>
    /// Adds a neighboring grid and updates relationships.
    /// </summary>
    /// <param name="neighborGrid">The neighboring grid to add.</param>
    internal bool TryAddGridNeighbor(VoxelGrid neighborGrid)
    {
        SpatialDirection neighborDirection = GetNeighborDirection(this, neighborGrid);
        int neightborIndex = (int)neighborDirection;

        if (neightborIndex == -1)
            return false;

        // Ensure the neighbor array is allocated and store the new neighbor
        Neighbors ??= new SwiftSparseMap<SwiftHashSet<int>>();
        if (!Neighbors.TryGetValue(neightborIndex, out SwiftHashSet<int> neighborSet))
        {
            neighborSet = SwiftHashSetPool<int>.Shared.Rent();
            Neighbors.Add(neightborIndex, neighborSet);
        }

        if (!neighborSet.Add(neighborGrid.GridIndex))
            return false;

        NeighborCount++;
        IncrementVersion();

        // Notify grid voxels that a new neighbor has been added
        NotifyBoundaryChange(neighborDirection);

        return true;
    }

    /// <summary>
    /// Removes a neighboring grid relationship.
    /// </summary>
    /// <param name="neighborGrid">The neighboring grid to remove.</param>
    internal bool TryRemoveGridNeighbor(VoxelGrid neighborGrid)
    {
        if (!TryGetGridNeighborSet(neighborGrid, out SpatialDirection neighborDirection, out int neighborIndex, out SwiftHashSet<int>? neighborSet))
            return false;

        if (!neighborSet!.Remove(neighborGrid.GridIndex))
            return false;

        ReleaseNeighborSetIfEmpty(neighborIndex, neighborSet);

        if (--NeighborCount == 0)
            Neighbors = null;

        IncrementVersion();

        NotifyBoundaryChange(neighborDirection); // Notify voxels of the removed neighbor

        return true;
    }

    private bool TryGetGridNeighborSet(
        VoxelGrid neighborGrid,
        out SpatialDirection neighborDirection,
        out int neighborIndex,
        out SwiftHashSet<int>? neighborSet)
    {
        neighborSet = null;
        neighborDirection = SpatialDirection.None;
        neighborIndex = -1;

        if (!IsConjoined)
            return false;

        neighborDirection = GetNeighborDirection(this, neighborGrid);
        neighborIndex = (int)neighborDirection;
        return neighborIndex != -1 && Neighbors!.TryGetValue(neighborIndex, out neighborSet);
    }

    private void ReleaseNeighborSetIfEmpty(int neighborIndex, SwiftHashSet<int> neighborSet)
    {
        if (neighborSet.Count > 0)
            return;

        GridForgeLogger.Channel.Info($"Releasing unused neighbor collection.");
        SwiftHashSetPool<int>.Shared.Release(neighborSet);
        Neighbors!.Remove(neighborIndex);
    }

    /// <summary>
    /// Notifies only the relevant boundary voxels when a neighboring grid is added or removed.
    /// Instead of looping through all voxels, it targets specific boundary rows or columns.
    /// </summary>
    /// <param name="direction">The direction of the affected boundary.</param>
    public void NotifyBoundaryChange(SpatialDirection direction)
    {
        SwiftThrowHelper.ThrowIfNull(Voxels, nameof(Voxels));

        int directionIndex = (int)direction;
        if (directionIndex < 0 || directionIndex >= SpatialAwareness.DirectionOffsets.Length)
            return;

        (int x, int y, int z) offset = SpatialAwareness.DirectionOffsets[directionIndex];

        (int xStart, int xEnd) = SpatialAwareness.GetBoundaryRange(offset.x, Width);
        (int yStart, int yEnd) = SpatialAwareness.GetBoundaryRange(offset.y, Height);
        (int zStart, int zEnd) = SpatialAwareness.GetBoundaryRange(offset.z, Length);

        VoxelGrid.InvalidateBoundaryVoxels(Voxels!, xStart, xEnd, yStart, yEnd, zStart, zEnd);
    }

    private static void InvalidateBoundaryVoxels(
        SwiftArray3D<Voxel> voxels,
        int xStart,
        int xEnd,
        int yStart,
        int yEnd,
        int zStart,
        int zEnd)
    {
        for (int x = xStart; x <= xEnd; x++)
        {
            for (int y = yStart; y <= yEnd; y++)
            {
                for (int z = zStart; z <= zEnd; z++)
                    voxels[x, y, z]?.InvalidateNeighborCache();
            }
        }
    }

    #endregion

    #region Grid Queries

    /// <summary>
    /// Determines if a voxel coordinate is at the boundary of the grid.
    /// Used to determine if a voxel should update when a neighboring grid is added/removed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsOnBoundary(VoxelIndex coord) =>
         coord.x == 0 || coord.x == Width - 1
      || coord.y == 0 || coord.y == Height - 1
      || coord.z == 0 || coord.z == Length - 1;

    /// <summary>
    /// Checks whether a given position falls within the grid bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsInBounds(Vector3d target) =>
        IsActive && Topology.IsInBounds(BoundsMin, BoundsMax, target);

    /// <summary>
    /// Checks if two grids are overlapping within a given tolerance threshold.
    /// This is used to determine if grids should be linked as neighbors.
    /// </summary>
    /// <param name="a">The first grid.</param>
    /// <param name="b">The second grid.</param>
    /// <param name="tolerance">Optional tolerance to account for minor floating-point errors.</param>
    /// <returns>True if the grids overlap within the tolerance, otherwise false.</returns>
    public static bool IsGridOverlapValid(VoxelGrid a, VoxelGrid b, Fixed64? tolerance = null)
    {
        Fixed64 toleranceValue = tolerance ?? a.Topology.OverlapTolerance;

        return AxisOverlaps(a.BoundsMin.X, a.BoundsMax.X, b.BoundsMin.X, b.BoundsMax.X, toleranceValue)
            && AxisOverlaps(a.BoundsMin.Y, a.BoundsMax.Y, b.BoundsMin.Y, b.BoundsMax.Y, toleranceValue)
            && AxisOverlaps(a.BoundsMin.Z, a.BoundsMax.Z, b.BoundsMin.Z, b.BoundsMax.Z, toleranceValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AxisOverlaps(
        Fixed64 firstMin,
        Fixed64 firstMax,
        Fixed64 secondMin,
        Fixed64 secondMax,
        Fixed64 tolerance) => firstMax >= secondMin - tolerance && firstMin <= secondMax + tolerance;

    /// <summary>
    /// Retrieves all neighboring grids connected to this grid.
    /// </summary>
    /// <returns>An enumeration of all neighboring grids.</returns>
    public IEnumerable<VoxelGrid> GetAllGridNeighbors()
    {
        if (!IsConjoined)
            yield break;

        var values = Neighbors!.DenseValues;
        int count = Neighbors.Count;

        for (int i = 0; i < count; i++)
        {
            SwiftHashSet<int> neighborSet = values[i];
            foreach (int neighborIndex in neighborSet)
            {
                if (World != null && World.TryGetGrid(neighborIndex, out VoxelGrid? neighborGrid))
                {
                    yield return neighborGrid!;
                }
            }
        }
    }

    /// <summary>
    /// Determines whether the given voxel coordinates are within the valid range of the grid.
    /// </summary>
    public bool IsValidVoxelIndex(int x, int y, int z)
    {
        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"This Grid is not currently active.");
            return false;
        }

        bool result = IsVoxelIndexInBounds(x, y, z);

        if (!result)
            GridForgeLogger.Channel.Info($"The coordinate {(x, y, z)} is not valid for this grid.");

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsVoxelIndexInBounds(int x, int y, int z) =>
         (uint)x < (uint)Width
      && (uint)y < (uint)Height
      && (uint)z < (uint)Length;

    /// <summary>
    /// Determines if a voxel is facing the boundary of the grid in a specific direction.
    /// Used to notify voxels when adjacent grids are added/removed.
    /// </summary>
    public bool IsFacingBoundaryDirection(VoxelIndex voxelIndex, SpatialDirection direction)
    {
        int directionIndex = (int)direction;
        if (directionIndex < 0 || directionIndex >= SpatialAwareness.DirectionOffsets.Length)
            return false;

        (int x, int y, int z) = SpatialAwareness.DirectionOffsets[directionIndex];

        return SpatialAwareness.IsAxisFacingBoundary(voxelIndex.x, x, Width)
            && SpatialAwareness.IsAxisFacingBoundary(voxelIndex.y, y, Height)
            && SpatialAwareness.IsAxisFacingBoundary(voxelIndex.z, z, Length);
    }

    /// <summary>
    /// Converts a world position to voxel index within the grid.
    /// </summary>
    public bool TryGetVoxelIndex(Vector3d position, out VoxelIndex result)
    {
        result = default;

        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"This Grid is not currently allocated.");
            return false;
        }

        if (!Topology.TryGetVoxelIndex(BoundsMin, BoundsMax, position, out VoxelIndex voxelIndex))
        {
            GridForgeLogger.Channel.Warn($"Position does not fall in the bounds of this grid");
            return false;
        }

        if (!IsValidVoxelIndex(voxelIndex.x, voxelIndex.y, voxelIndex.z))
            return false;

        result = voxelIndex;
        return true;
    }

    /// <summary>
    /// Converts a 2D XZ-plane world position on the default world Y layer to a voxel index within the grid.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="result">The resolved voxel index, if found.</param>
    /// <returns>True if the position resolved to an allocated voxel index; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetVoxelIndex(Vector2d position, out VoxelIndex result) =>
        TryGetVoxelIndex(position, default, out result);

    /// <summary>
    /// Converts a 2D XZ-plane world position on the supplied world Y layer to a voxel index within the grid.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="result">The resolved voxel index, if found.</param>
    /// <returns>True if the position resolved to an allocated voxel index; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetVoxelIndex(Vector2d position, Fixed64 layerY, out VoxelIndex result) =>
        TryGetVoxelIndex(GridPlane2d.ToWorld(position, layerY), out result);

    /// <summary>
    /// Checks if a voxel at the given coordinates is allocated within the grid.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsVoxelAllocated(int x, int y, int z) =>
        IsValidVoxelIndex(x, y, z) && Voxels![x, y, z]?.IsAllocated == true;

    /// <summary>
    /// Retrieves the <see cref="Voxel"/> at the specified coordinates, if allocated.
    /// </summary>
    public bool TryGetVoxel(int x, int y, int z, out Voxel? result)
    {
        result = null;

        if (!IsVoxelAllocated(x, y, z))
        {
            GridForgeLogger.Channel.Warn($"Voxel at coorinate {(x, y, z)} is has not been allocated to the grid.");
            return false;
        }

        result = Voxels![x, y, z];
        return true;
    }

    /// <summary>
    /// Retrieves a grid voxel from a given coordinate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetVoxel(VoxelIndex voxelIndex, out Voxel? result) =>
         TryGetVoxel(voxelIndex.x, voxelIndex.y, voxelIndex.z, out result);

    /// <summary>
    /// Retrieve <see cref="Voxel"/> from world <see cref="Vector3d"/> points
    /// </summary>
    /// <returns><see cref="Voxel"/> at the given position or null if the position is not valid.</returns>
    public bool TryGetVoxel(Vector3d position, out Voxel? result)
    {
        result = null;
        return TryGetVoxelIndex(position, out VoxelIndex coordinate)
            && TryGetVoxel(coordinate.x, coordinate.y, coordinate.z, out result);
    }

    /// <summary>
    /// Retrieves a <see cref="Voxel"/> from a 2D XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetVoxel(Vector2d position, out Voxel? result) =>
        TryGetVoxel(position, default, out result);

    /// <summary>
    /// Retrieves a <see cref="Voxel"/> from a 2D XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="result">The resolved voxel, if found.</param>
    /// <returns>True if the voxel was resolved; otherwise false.</returns>
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetVoxel(Vector2d position, Fixed64 layerY, out Voxel? result) =>
        TryGetVoxel(GridPlane2d.ToWorld(position, layerY), out result);

    /// <summary>
    /// Computes the scan cell key for a given world position.
    /// </summary>
    public int GetScanCellKey(Vector3d position)
    {
        if (!TryGetVoxelIndex(position, out VoxelIndex voxelIndex))
            return -1;

        return GetScanCellKey(voxelIndex);
    }

    /// <summary>
    /// Calculates the spatial cell index for a given position.
    /// </summary>
    public int GetScanCellKey(VoxelIndex voxelIndex)
    {
        (int x, int y, int z) = (
                voxelIndex.x / ScanCellSize,
                voxelIndex.y / ScanCellSize,
                voxelIndex.z / ScanCellSize
            );

        int scanCellKey = GetScanCellKey(x, y, z);
        if (scanCellKey == -1)
        {
            GridForgeLogger.Channel.Warn($"Position {voxelIndex} is not in the bounds for this grids Scan Cell overlay.");
            return -1;
        }

        return scanCellKey;
    }

    /// <summary>
    /// Calculates a unique scan cell key from grid-local scan cell coordinates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetScanCellKey(int x, int y, int z)
    {
        if ((uint)x >= (uint)_scanWidth
            || (uint)y >= (uint)_scanHeight
            || (uint)z >= (uint)_scanLength)
        {
            return -1;
        }

        return x + y * _scanWidth + z * _scanLayerSize;
    }

    /// <summary>
    /// Retrieves a scan cell from the grid using its key.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetScanCell(int key, out ScanCell? outScanCell)
    {
        outScanCell = null;
        return ScanCells?.TryGetValue(key, out outScanCell) == true;
    }

    /// <summary>
    /// Retrieves the scan cell corresponding to a given world position.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetScanCell(Vector3d position, out ScanCell? outScanCell)
    {
        int key = GetScanCellKey(position);
        return TryGetScanCell(key, out outScanCell);
    }

    /// <summary>
    /// Retrieves the scan cell associated with the given voxel index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetScanCell(VoxelIndex voxelIndex, out ScanCell? outScanCell)
    {
        outScanCell = null;
        return TryGetVoxel(voxelIndex, out Voxel? voxel)
            && TryGetScanCell(voxel!.ScanCellKey, out outScanCell);
    }

    /// <summary>
    /// Enumerates all currently active scan cells within the grid.
    /// </summary>
    public IEnumerable<ScanCell> GetActiveScanCells()
    {
        if (!IsActive || !IsOccupied)
            yield break;

        foreach (int activeCellKey in ActiveScanCells!)
        {
            if (ScanCells!.TryGetValue(activeCellKey, out ScanCell scanCell))
                yield return scanCell;
        }
    }

    /// <summary>
    /// Helper function to ceil snap a <see cref="Vector3d"/> to this grid's voxel size, ensuring it stays within grid bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d CeilToGrid(Vector3d position) =>
         Topology.CeilToGrid(BoundsMin, BoundsMax, position);

    /// <summary>
    /// Helper function to floor snap a <see cref="Vector3d"/> to this grid's voxel size, ensuring it stays within grid bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d FloorToGrid(Vector3d position) =>
        Topology.FloorToGrid(BoundsMin, BoundsMax, position);

    /// <summary>
    /// Snaps a given position to the closest scan cell in the grid
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int x, int y, int z) SnapToScanCell(Vector3d position) =>
        Topology.SnapToScanCell(BoundsMin, position, ScanCellSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (Vector3d min, Vector3d max) NormalizeBounds(Vector3d min, Vector3d max, Fixed64? padding = null) =>
        Topology.NormalizeBounds(min, max, padding);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Vector3d GetWorldPosition(VoxelIndex index) =>
        Topology.GetWorldPosition(BoundsMin, index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Vector3d GetWorldOffset((int x, int y, int z) offset) =>
        Topology.GetWorldOffset(offset);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(
            GridIndex.GetHashCode(),
            BoundsMin.GetHashCode(),
            BoundsMax.GetHashCode());

    #endregion
}
