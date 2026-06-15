//=======================================================================
// VoxelGrid.cs
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
    /// Total addressable voxel count within the grid bounds.
    /// </summary>
    public int Size { get; private set; }

    /// <summary>
    /// The number of physical voxels configured in the grid storage.
    /// Dense grids report <see cref="Size"/>; sparse grids report configured voxels only.
    /// </summary>
    public int ConfiguredVoxelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storage?.ConfiguredVoxelCount ?? 0;
    }

    /// <summary>
    /// The physical voxel storage strategy used by this grid.
    /// </summary>
    public GridStorageKind StorageKind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storage?.Kind ?? GridStorageKind.Dense;
    }

    /// <summary>
    /// The dense 3D collection of voxels managed by this grid when dense storage is active.
    /// </summary>
    internal SwiftArray3D<Voxel>? Voxels
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _denseStorage.Voxels;
    }

    /// <summary>
    /// Stores topology-local neighbor slots for neighboring grids based on their relative positions.
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
    /// Count of topology-local neighbor slots supported by this grid.
    /// </summary>
    internal int NeighborSlotCount => _topology?.NeighborSlotCount ?? 0;

    /// <summary>
    /// The active topology kind for this grid.
    /// </summary>
    internal GridTopologyKind? TopologyKind => _topology?.Kind;

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
    internal SwiftSparseMap<ScanCell>? ScanCells
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storage?.ScanCells;
    }

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
    private IVoxelGridStorage? _storage;
    private readonly DenseVoxelGridStorage _denseStorage = new();
    private readonly SparseVoxelGridStorage _sparseStorage = new();

    internal IGridTopology Topology => _topology!;

    internal int ScanWidth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _scanWidth;
    }

    internal int ScanHeight
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _scanHeight;
    }

    internal int ScanLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _scanLength;
    }

    #endregion

    #region Initialization & Reset

    /// <summary>
    /// Initializes the grid with an explicit owning world and configured sparse voxel set.
    /// </summary>
    /// <param name="world">The world that will own this grid.</param>
    /// <param name="gridIndex">The unique index of this grid in the world.</param>
    /// <param name="configuration">The normalized configuration settings for the grid.</param>
    /// <param name="topology">The validated topology instance for this grid.</param>
    /// <param name="configuredVoxels">The validated sparse voxel indices to materialize.</param>
    internal void Initialize(
        GridWorld world,
        ushort gridIndex,
        GridConfiguration configuration,
        IGridTopology topology,
        VoxelIndex[] configuredVoxels)
    {
        Version = 1;

        World = world;
        GridIndex = gridIndex;

        Configuration = configuration;
        _topology = topology;

        SpawnToken = GetHashCode();

        // +1 to account for inclusive bounds and to ensure that even the smallest grids (1x1x1) remain valid.
        GridDimensions dimensions = topology.CalculateDimensions(BoundsMin, BoundsMax);
        Width = dimensions.Width;
        Height = dimensions.Height;
        Length = dimensions.Length;
        Size = Width * Height * Length;

        ConfigureScanDimensions();
        if (configuration.StorageKind == GridStorageKind.Sparse)
        {
            _sparseStorage.Initialize(this, configuredVoxels);
            _storage = _sparseStorage;
        }
        else
        {
            _denseStorage.Initialize(this);
            _storage = _denseStorage;
        }

        IsActive = true;
    }

    /// <summary>
    /// Resets the grid, clearing all voxels and scan cells.
    /// </summary>
    internal void Reset()
    {
        if (!IsActive)
            return;

        _storage!.Reset(this);
        _storage = null;

        // Just in case since voxels should have already cleared any registered obstacles.
        ObstacleCount = 0;

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
            SwiftHashSetPool<int>.Shared.Release(neighbors);

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

    private void ConfigureScanDimensions()
    {
        _scanWidth = ((Width - 1) / ScanCellSize) + 1;
        _scanHeight = ((Height - 1) / ScanCellSize) + 1;
        _scanLength = ((Length - 1) / ScanCellSize) + 1;
        _scanLayerSize = _scanWidth * _scanHeight;
    }

    #endregion

    #region Boundary Management

    /// <summary>
    /// Determines the rectangular-prism direction from grid <paramref name="a"/> to neighboring grid <paramref name="b"/>.
    /// </summary>
    /// <param name="a">The source rectangular-prism grid.</param>
    /// <param name="b">The neighboring rectangular-prism grid.</param>
    /// <returns>The rectangular direction from <paramref name="a"/> to <paramref name="b"/>, or <see cref="RectangularDirection.None"/> when the grids are not rectangular neighbors.</returns>
    public static RectangularDirection GetRectangularNeighborDirection(VoxelGrid a, VoxelGrid b)
    {
        return TryGetNeighborSlot(a, b, GridTopologyKind.RectangularPrism, out int slot)
            ? (RectangularDirection)slot
            : RectangularDirection.None;
    }

    /// <summary>
    /// Determines the hex-prism direction from grid <paramref name="a"/> to neighboring grid <paramref name="b"/>.
    /// </summary>
    /// <param name="a">The source hex-prism grid.</param>
    /// <param name="b">The neighboring hex-prism grid.</param>
    /// <returns>The hex direction from <paramref name="a"/> to <paramref name="b"/>, or <see cref="HexDirection.None"/> when the grids are not hex-prism neighbors.</returns>
    public static HexDirection GetHexNeighborDirection(VoxelGrid a, VoxelGrid b)
    {
        return TryGetNeighborSlot(a, b, GridTopologyKind.HexPrism, out int slot)
            ? (HexDirection)slot
            : HexDirection.None;
    }

    private static bool TryGetNeighborSlot(VoxelGrid a, VoxelGrid b, GridTopologyKind expectedKind, out int slot)
    {
        if (a._topology?.Kind != expectedKind || b._topology?.Kind != expectedKind)
        {
            slot = -1;
            return false;
        }

        return TryGetNeighborSlot(a, b, out slot);
    }

    private static bool TryGetNeighborSlot(VoxelGrid a, VoxelGrid b, out int slot)
    {
        slot = -1;

        if (a._topology == null
            || b._topology == null
            || a._topology.Kind != b._topology.Kind)
        {
            return false;
        }

        return a._topology.TryGetNeighborSlotFromWorldDelta(b.BoundsCenter - a.BoundsCenter, out slot)
            && (uint)slot < (uint)a._topology.NeighborSlotCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal VoxelIndex GetNeighborOffset(int slot) => Topology.GetNeighborOffset(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetNeighborSlot(RectangularDirection direction, out int slot)
    {
        slot = (int)direction;
        return _topology?.Kind == GridTopologyKind.RectangularPrism
            && (uint)slot < (uint)_topology.NeighborSlotCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetNeighborSlot(HexDirection direction, out int slot)
    {
        slot = (int)direction;
        return _topology?.Kind == GridTopologyKind.HexPrism
            && (uint)slot < (uint)_topology.NeighborSlotCount;
    }

    /// <summary>
    /// Adds a neighboring grid and updates relationships.
    /// </summary>
    /// <param name="neighborGrid">The neighboring grid to add.</param>
    internal bool TryAddGridNeighbor(VoxelGrid neighborGrid)
    {
        if (!TryGetNeighborSlot(this, neighborGrid, out int neighborSlot))
            return false;

        // Ensure the neighbor array is allocated and store the new neighbor
        Neighbors ??= new SwiftSparseMap<SwiftHashSet<int>>();
        if (!Neighbors.TryGetValue(neighborSlot, out SwiftHashSet<int> neighborSet))
        {
            neighborSet = SwiftHashSetPool<int>.Shared.Rent();
            Neighbors.Add(neighborSlot, neighborSet);
        }

        if (!neighborSet.Add(neighborGrid.GridIndex))
            return false;

        NeighborCount++;
        IncrementVersion();

        return true;
    }

    /// <summary>
    /// Removes a neighboring grid relationship.
    /// </summary>
    /// <param name="neighborGrid">The neighboring grid to remove.</param>
    internal bool TryRemoveGridNeighbor(VoxelGrid neighborGrid)
    {
        if (!TryGetGridNeighborSet(neighborGrid, out int neighborSlot, out SwiftHashSet<int>? neighborSet))
            return false;

        if (!neighborSet!.Remove(neighborGrid.GridIndex))
            return false;

        ReleaseNeighborSetIfEmpty(neighborSlot, neighborSet);

        if (--NeighborCount == 0)
            Neighbors = null;

        IncrementVersion();

        return true;
    }

    private bool TryGetGridNeighborSet(
        VoxelGrid neighborGrid,
        out int neighborSlot,
        out SwiftHashSet<int>? neighborSet)
    {
        neighborSet = null;
        neighborSlot = -1;

        if (!IsConjoined)
            return false;

        return TryGetNeighborSlot(this, neighborGrid, out neighborSlot)
            && Neighbors!.TryGetValue(neighborSlot, out neighborSet);
    }

    private void ReleaseNeighborSetIfEmpty(int neighborIndex, SwiftHashSet<int> neighborSet)
    {
        if (neighborSet.Count > 0)
            return;

        GridForgeLogger.Channel.Info($"Releasing unused neighbor collection.");
        SwiftHashSetPool<int>.Shared.Release(neighborSet);
        Neighbors!.Remove(neighborIndex);
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
        IsActive && Topology.IsInBounds(BoundsMin, BoundsMax, Width, Height, Length, target);

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
                if (World!.TryGetGrid(neighborIndex, out VoxelGrid? neighborGrid))
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
    /// Determines if a topology-local voxel index is facing the rectangular-prism boundary in the supplied direction.
    /// </summary>
    public bool IsFacingBoundary(VoxelIndex voxelIndex, RectangularDirection direction) =>
        TryGetNeighborSlot(direction, out int slot)
        && _topology!.IsFacingBoundary(voxelIndex, slot, Width, Height, Length);

    /// <summary>
    /// Determines if a topology-local voxel index is facing the hex-prism boundary in the supplied direction.
    /// </summary>
    public bool IsFacingBoundary(VoxelIndex voxelIndex, HexDirection direction) =>
        TryGetNeighborSlot(direction, out int slot)
        && _topology!.IsFacingBoundary(voxelIndex, slot, Width, Height, Length);

    /// <summary>
    /// Converts a world position to a topology-local voxel index within the grid.
    /// Rectangular-prism grids return X/Y/Z coordinates; hex-prism grids return axial Q, layer, and axial R in X/Y/Z fields.
    /// </summary>
    public bool TryGetVoxelIndex(Vector3d position, out VoxelIndex result)
    {
        result = default;

        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"This Grid is not currently allocated.");
            return false;
        }

        if (!Topology.TryGetVoxelIndex(BoundsMin, BoundsMax, Width, Height, Length, position, out VoxelIndex voxelIndex))
        {
            GridForgeLogger.Channel.Warn($"Position does not fall in the bounds of this grid");
            return false;
        }

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
    /// Checks if a voxel at the given topology-local coordinates is allocated within the grid.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsVoxelAllocated(int x, int y, int z) =>
        IsValidVoxelIndex(x, y, z) && _storage!.TryGetVoxel(x, y, z, out _);

    /// <summary>
    /// Checks whether a physical voxel is configured at the supplied grid-local index.
    /// </summary>
    /// <param name="voxelIndex">The grid-local voxel index to test.</param>
    /// <returns>True when the index resolves to a configured voxel; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsVoxel(VoxelIndex voxelIndex) =>
        IsVoxelAllocated(voxelIndex.x, voxelIndex.y, voxelIndex.z);

    /// <summary>
    /// Configures a sparse voxel at runtime. Dense grids, invalid indices, and already-configured
    /// sparse voxels return false.
    /// </summary>
    /// <param name="voxelIndex">The grid-local voxel index to configure.</param>
    /// <param name="voxel">The configured voxel when the operation succeeds.</param>
    /// <returns>True when a new sparse voxel was configured; otherwise false.</returns>
    public bool TryAddVoxel(VoxelIndex voxelIndex, out Voxel? voxel)
    {
        voxel = null;
        if (!CanMutateSparseVoxel(voxelIndex))
            return false;

        if (!_sparseStorage.TryAddVoxel(this, voxelIndex, out voxel))
            return false;

        uint gridVersion = IncrementVersion();
        voxel!.CachedGridVersion = gridVersion;
        World!.NotifyActiveGridChange(this, GridEventKind.SparseVoxelAdded, voxelIndex, voxel.WorldPosition);
        return true;
    }

    /// <summary>
    /// Removes a configured sparse voxel at runtime when it has no unsafe runtime state.
    /// Dense grids, missing voxels, occupied voxels, voxels with obstacle tokens, partitioned voxels,
    /// and voxels with active event subscribers return false.
    /// </summary>
    /// <param name="voxelIndex">The grid-local voxel index to remove.</param>
    /// <returns>True when the sparse voxel was removed; otherwise false.</returns>
    public bool TryRemoveVoxel(VoxelIndex voxelIndex)
    {
        if (!CanMutateSparseVoxel(voxelIndex)
            || !TryGetVoxel(voxelIndex, out Voxel? voxel)
            || !CanRemoveSparseVoxel(voxel!))
        {
            return false;
        }

        Vector3d affectedPosition = voxel!.WorldPosition;
        _sparseStorage.TryRemoveVoxel(this, voxelIndex, out _);

        IncrementVersion();
        World!.NotifyActiveGridChange(this, GridEventKind.SparseVoxelRemoved, voxelIndex, affectedPosition);
        return true;
    }

    private bool CanMutateSparseVoxel(VoxelIndex voxelIndex) =>
        IsActive
        && StorageKind == GridStorageKind.Sparse
        && IsValidVoxelIndex(voxelIndex.x, voxelIndex.y, voxelIndex.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanRemoveSparseVoxel(Voxel voxel) =>
        voxel.IsAllocated
        && !voxel.IsOccupied
        && voxel.ObstacleCount == 0
        && !voxel.IsPartioned
        && !voxel.HasEventSubscribers;

    /// <summary>
    /// Retrieves the <see cref="Voxel"/> at the specified topology-local coordinates, if allocated.
    /// </summary>
    public bool TryGetVoxel(int x, int y, int z, out Voxel? result)
    {
        result = null;

        if (!IsValidVoxelIndex(x, y, z))
            return false;

        return _storage!.TryGetVoxel(x, y, z, out result);
    }

    /// <summary>
    /// Enumerates physical voxels configured in this grid in deterministic storage order.
    /// </summary>
    public IEnumerable<Voxel> EnumerateVoxels() =>
        _storage?.EnumerateVoxels() ?? Array.Empty<Voxel>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void VisitVoxels<TVisitor>(ref TVisitor visitor)
        where TVisitor : struct, IVoxelStorageVisitor =>
        _storage?.VisitVoxels(ref visitor);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AddVoxelsInIndexRange(
        VoxelIndex min,
        VoxelIndex max,
        SwiftList<Voxel> results,
        SwiftHashSet<Voxel> redundancy) =>
        _storage?.AddVoxelsInIndexRange(min, max, results, redundancy);

    /// <summary>
    /// Retrieves a grid voxel from a topology-local coordinate.
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
    /// Retrieves the physical voxel whose center is nearest to the supplied world position.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The world position to resolve.</param>
    /// <param name="result">The closest physical voxel, if found.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestVoxel(Vector3d position, out Voxel? result) =>
        TryGetClosestVoxel(position, out result, out _);

    internal bool TryGetClosestVoxel(
        Vector3d position,
        out Voxel? result,
        out Fixed64 distanceSquared)
    {
        result = null;
        distanceSquared = Fixed64.MaxValue;

        if (!IsActive)
        {
            GridForgeLogger.Channel.Warn($"This Grid is not currently allocated.");
            return false;
        }

        if (ConfiguredVoxelCount == 0)
            return false;

        VoxelIndex closestIndex = Topology.GetClosestVoxelIndex(BoundsMin, Width, Height, Length, position);
        return _storage!.TryGetClosestVoxel(this, closestIndex, position, out result, out distanceSquared);
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
    /// Retrieves the physical voxel whose center is nearest to a 2D XZ-plane world position on the default world Y layer.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="result">The closest physical voxel, if found.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestVoxel(Vector2d position, out Voxel? result) =>
        TryGetClosestVoxel(position, default, out result);

    /// <summary>
    /// Retrieves the physical voxel whose center is nearest to a 2D XZ-plane world position on the supplied world Y layer.
    /// Sparse grids only consider configured physical voxels.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve. Defaults to zero when omitted by paired overloads.</param>
    /// <param name="result">The closest physical voxel, if found.</param>
    /// <returns>True if a physical voxel was resolved; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetClosestVoxel(Vector2d position, Fixed64 layerY, out Voxel? result) =>
        TryGetClosestVoxel(GridPlane2d.ToWorld(position, layerY), out result);

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
        return _storage?.TryGetScanCell(key, out outScanCell) == true;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AddScanCellsInRange(
        int xMin,
        int yMin,
        int zMin,
        int xMax,
        int yMax,
        int zMax,
        SwiftList<ScanCell> results,
        SwiftHashSet<ScanCell> redundancy) =>
        _storage?.AddScanCellsInRange(
            this,
            xMin,
            yMin,
            zMin,
            xMax,
            yMax,
            zMax,
            results,
            redundancy);

    /// <summary>
    /// Enumerates all currently active scan cells within the grid.
    /// </summary>
    public IEnumerable<ScanCell> GetActiveScanCells()
    {
        if (!IsActive || !IsOccupied)
            yield break;

        foreach (int activeCellKey in ActiveScanCells!)
        {
            if (_storage!.TryGetScanCell(activeCellKey, out ScanCell? scanCell))
                yield return scanCell!;
        }
    }

    /// <summary>
    /// Helper function to ceil snap a <see cref="Vector3d"/> through this grid's topology, ensuring it stays within grid bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d CeilToGrid(Vector3d position) =>
         Topology.CeilToGrid(BoundsMin, BoundsMax, Width, Height, Length, position);

    /// <summary>
    /// Helper function to floor snap a <see cref="Vector3d"/> through this grid's topology, ensuring it stays within grid bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d FloorToGrid(Vector3d position) =>
        Topology.FloorToGrid(BoundsMin, BoundsMax, Width, Height, Length, position);

    /// <summary>
    /// Snaps a given position to the topology-local scan cell in the grid.
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
