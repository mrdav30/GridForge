//=======================================================================
// VoxelGrid.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Dimensions;
using SwiftCollections.Pool;

namespace GridForge.Grids;

/// <summary>
/// Represents a 3D grid structure for spatial organization, managing voxels and scan cells.
/// Handles initialization, neighbor relationships, and occupancy tracking.
/// </summary>
public class VoxelGrid
{
    #region Fields & Properties

    /// <summary>
    /// Nonzero 64-bit world-local allocation generation identifying this active grid instance.
    /// Zero indicates an inactive or unallocated grid.
    /// </summary>
    /// <value>Nonzero 64-bit world-local allocation generation identifying this active grid instance. Zero indicates an inactive or unallocated grid.</value>
    public long SpawnToken { get; private set; }

    /// <summary>
    /// World-local index of the grid within its owning world.
    /// </summary>
    /// <value>World-local index of the grid within its owning world.</value>
    public ushort GridIndex { get; private set; }

    /// <summary>
    /// The world that owns this grid instance.
    /// </summary>
    /// <value>The world that owns this grid instance.</value>
    public GridWorld? World { get; private set; }

    /// <summary>
    /// Synchronizes obstacle mutations for this grid.
    /// </summary>
    internal object ObstacleSyncRoot { get; } = new object();

    /// <summary>
    /// Synchronizes occupant mutations for this grid.
    /// </summary>
    internal object OccupantSyncRoot { get; } = new object();

    /// <summary>Gets the normalized configuration of this grid generation.</summary>
    /// <value>A copied value containing snapped world-space bounds, topology metrics, storage kind, and scan-cell size. Reset replaces it with the default value.</value>
    public GridConfiguration Configuration { get; private set; }

    /// <summary>
    /// Minimum bounds of the grid in world coordinates.
    /// </summary>
    /// <value>Minimum bounds of the grid in world coordinates.</value>
    public Vector3d BoundsMin => Configuration.BoundsMin;

    /// <summary>
    /// Maximum bounds of the grid in world coordinates.
    /// </summary>
    /// <value>Maximum bounds of the grid in world coordinates.</value>
    public Vector3d BoundsMax => Configuration.BoundsMax;

    /// <summary>
    /// Center position of the grid in world space.
    /// </summary>
    /// <value>Center position of the grid in world space.</value>
    public Vector3d BoundsCenter => Configuration.GridCenter;

    /// <summary>
    /// Grid width in number of voxels.
    /// </summary>
    /// <value>Grid width in number of voxels.</value>
    public int Width { get; private set; }

    /// <summary>
    /// Grid height in number of voxels.
    /// </summary>
    /// <value>Grid height in number of voxels.</value>
    public int Height { get; private set; }

    /// <summary>
    /// Grid length in number of voxels.
    /// </summary>
    /// <value>Grid length in number of voxels.</value>
    public int Length { get; private set; }

    /// <summary>
    /// Total addressable voxel count within the grid bounds.
    /// </summary>
    /// <value>Total addressable voxel count within the grid bounds.</value>
    public int Size { get; private set; }

    /// <summary>
    /// The number of physical voxels configured in the grid storage.
    /// Dense grids report <see cref="Size"/>; sparse grids report configured voxels only.
    /// </summary>
    /// <value>The number of physical voxels configured in the grid storage. Dense grids report <see cref="P:GridForge.Grids.VoxelGrid.Size" />; sparse grids report configured voxels only.</value>
    public int ConfiguredVoxelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storage?.ConfiguredVoxelCount ?? 0;
    }

    /// <summary>
    /// The physical voxel storage strategy used by this grid.
    /// </summary>
    /// <value>The physical voxel storage strategy used by this grid.</value>
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
    /// Each topology-local slot can contain multiple neighboring grid IDs. Contact
    /// queries can also return multiple voxel contacts across different grid cell sizes.
    /// </remarks>
    /// <value>Grid-owned live lookup storage, or null when unavailable. Inspect only; direct edits bypass manager invariants. Grid mutation can invalidate enumeration, and reset releases this storage for pooling.</value>
    public SwiftSparseMap<SwiftHashSet<int>>? Neighbors { get; private set; }

    /// <summary>
    /// Count of currently linked neighboring grids.
    /// </summary>
    /// <value>Count of currently linked neighboring grids.</value>
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
    /// <value>Determines whether this grid has any linked neighbors.</value>
    public bool IsConjoined => Neighbors != null && NeighborCount > 0;

    /// <summary>
    /// Size of a scan cell used for spatial partitioning.
    /// </summary>
    /// <value>Size of a scan cell used for spatial partitioning.</value>
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
    /// <value>Grid-owned live lookup storage, or null when unavailable. Inspect only; direct edits bypass manager invariants. Grid mutation can invalidate enumeration, and reset releases this storage for pooling.</value>
    public SwiftHashSet<int>? ActiveScanCells { get; internal set; }

    /// <summary>
    /// Indicates whether the grid is currently active.
    /// </summary>
    /// <value>Indicates whether the grid is currently active.</value>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Determines whether the grid is occupied (active and containing occupants).
    /// </summary>
    /// <value>Determines whether the grid is occupied (active and containing occupants).</value>
    public bool IsOccupied => ActiveScanCells?.Count > 0;

    /// <summary>
    /// Tracks the number of obstacles currently registered in the grid.
    /// </summary>
    /// <value>The total obstacle-token attachments across physical voxels. A stacked blocker contributes one attachment per covered voxel; this is not a distinct-blocker count.</value>
    public int ObstacleCount { get; internal set; }

    // Unlike the token attachment count, this is bounded by ConfiguredVoxelCount.
    internal int BlockedVoxelCount { get; set; }

    /// <summary>
    /// Tracks the version of the grid, incremented when a <see cref="Voxel"/> is modified.
    /// </summary>
    /// <value>A live grid's revision wraps from <see cref="uint.MaxValue"/> to one.
    /// Pair retained observations with the owning grid generation and the relevant committed change sequence.</value>
    public uint Version { get; private set; }

    internal ulong LastChangeSequence { get; set; }

    // Retained through removal callbacks, which can end the original world lifetime.
    internal long RemovalWorldToken { get; set; }
    internal long OwnerWorldToken => RemovalWorldToken != 0 ? RemovalWorldToken : World!.SpawnToken;
    internal bool SuppressTeardownObstacleClears { get; set; }

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
    /// <param name="spawnToken">The world-local allocation generation for this grid.</param>
    /// <param name="configuration">The normalized configuration settings for the grid.</param>
    /// <param name="topology">The validated topology instance for this grid.</param>
    /// <param name="configuredVoxels">The validated sparse voxel indices to materialize.</param>
    internal void Initialize(
        GridWorld world,
        ushort gridIndex,
        long spawnToken,
        GridConfiguration configuration,
        IGridTopology topology,
        VoxelIndex[] configuredVoxels)
    {
        Version = 1;

        World = world;
        GridIndex = gridIndex;

        Configuration = configuration;
        _topology = topology;

        SpawnToken = spawnToken;

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

        try
        {
            _storage!.Reset(this);
        }
        finally
        {
            SuppressTeardownObstacleClears = false;
            if (RemovalWorldToken != 0)
                World!.FinishGridRemoval(this);
        }
        _storage = null;

        // Just in case since voxels should have already cleared any registered obstacles.
        ObstacleCount = 0;
        BlockedVoxelCount = 0;

        ReleaseActiveScanCells();
        ReleaseNeighbors();

        Configuration = default;
        World = null;
        _topology = null;

        SpawnToken = 0;
        Version = 0;
        LastChangeSequence = 0;

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
    /// <param name="coord">The topology-local coordinate to inspect.</param>
    /// <returns>True when any local component is zero or its final address; this does not validate the other components.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsOnBoundary(VoxelIndex coord) =>
         coord.x == 0 || coord.x == Width - 1
      || coord.y == 0 || coord.y == Height - 1
      || coord.z == 0 || coord.z == Length - 1;

    /// <summary>
    /// Checks whether a given position falls within the grid bounds.
    /// </summary>
    /// <param name="target">The fixed-point position in world coordinates.</param>
    /// <returns>True when the topology admits the world position within this grid address space. This does not require sparse physical presence.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsInBounds(Vector3d target) =>
        IsActive && Topology.IsInBounds(BoundsMin, BoundsMax, Width, Height, Length, target);

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
    /// <param name="x">The local X or axial Q address component.</param>
    /// <param name="y">The local Y or vertical layer address component.</param>
    /// <param name="z">The local Z or axial R address component.</param>
    /// <returns>True when every component lies within the grid dimensions; sparse physical presence is not required.</returns>
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
    /// <param name="voxelIndex">The topology-local X/Y/Z or Q/layer/R address.</param>
    /// <param name="direction">The topology-local neighbor direction.</param>
    /// <returns>True when the selected topology direction faces the addressed boundary; false for an unsupported direction.</returns>
    public bool IsFacingBoundary(VoxelIndex voxelIndex, RectangularDirection direction) =>
        TryGetNeighborSlot(direction, out int slot)
        && _topology!.IsFacingBoundary(voxelIndex, slot, Width, Height, Length);

    /// <summary>
    /// Determines if a topology-local voxel index is facing the hex-prism boundary in the supplied direction.
    /// </summary>
    /// <param name="voxelIndex">The topology-local X/Y/Z or Q/layer/R address.</param>
    /// <param name="direction">The topology-local neighbor direction.</param>
    /// <returns>True when the selected topology direction faces the addressed boundary; false for an unsupported direction.</returns>
    public bool IsFacingBoundary(VoxelIndex voxelIndex, HexDirection direction) =>
        TryGetNeighborSlot(direction, out int slot)
        && _topology!.IsFacingBoundary(voxelIndex, slot, Width, Height, Length);

    /// <summary>
    /// Converts a world position to a topology-local voxel index within the grid.
    /// Rectangular-prism grids return X/Y/Z coordinates; hex-prism grids return axial Q, layer, and axial R in X/Y/Z fields.
    /// </summary>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="result">The resolved value on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the world position maps to an in-bounds address; otherwise false and the result is default. Sparse presence is not required.</returns>
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
    /// <param name="x">The local X or axial Q address component.</param>
    /// <param name="y">The local Y or vertical layer address component.</param>
    /// <param name="z">The local Z or axial R address component.</param>
    /// <returns>True when the address contains a physical voxel; false for out-of-bounds or missing sparse addresses.</returns>
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
    /// <exception cref="OverflowException">The addition would require an unavailable committed change stamp.</exception>
    public bool TryAddVoxel(VoxelIndex voxelIndex, out Voxel? voxel)
    {
        voxel = null;
        bool drainCommittedChanges;
        GridWorld? world = World;
        if (world == null)
            return false;

        world.EnterReadLock();
        try
        {
            lock (world.ChangeSyncRoot)
            {
                if (!CanMutateSparseVoxel(voxelIndex))
                    return false;

                if (world.IsChangeSequenceExhausted)
                    return GridWorld.RejectExhaustedMutation(!TryGetVoxel(voxelIndex, out _));
                if (!_sparseStorage.TryAddVoxel(this, voxelIndex, out voxel))
                    return false;

                uint gridVersion = IncrementVersion();
                voxel!.CachedGridVersion = gridVersion;
                GridEventInfo eventInfo = world.CreateGridEventInfo(
                    this,
                    GridEventKind.SparseVoxelAdded,
                    voxelIndex,
                    voxel.WorldPosition,
                    voxel.WorldPosition,
                    world.AllocateChangeStamp(),
                    hasVoxelState: true,
                    isVoxelPresent: true,
                    obstacleCount: 0);
                drainCommittedChanges = world.EnqueueCommittedChange(new GridCommittedChange(eventInfo));
            }
        }
        finally
        {
            world.ExitReadLock();
        }

        if (drainCommittedChanges)
            world.DrainCommittedChanges();
        return true;
    }

    /// <summary>
    /// Removes a configured sparse voxel at runtime when it has no unsafe runtime state.
    /// Dense grids, missing voxels, occupied voxels, voxels with obstacle tokens, partitioned voxels,
    /// and voxels with active event subscribers return false.
    /// </summary>
    /// <param name="voxelIndex">The grid-local voxel index to remove.</param>
    /// <returns>True when the sparse voxel was removed; otherwise false.</returns>
    /// <exception cref="OverflowException">The removal would require an unavailable committed change stamp.</exception>
    public bool TryRemoveVoxel(VoxelIndex voxelIndex)
    {
        bool drainCommittedChanges;
        GridWorld? world = World;
        if (world == null)
            return false;

        world.EnterReadLock();
        try
        {
            lock (world.ChangeSyncRoot)
            {
                if (!CanMutateSparseVoxel(voxelIndex)
                    || !TryGetVoxel(voxelIndex, out Voxel? voxel)
                    || !CanRemoveSparseVoxel(voxel!))
                {
                    return false;
                }

                world.EnsureChangeStampAvailable();
                Vector3d affectedPosition = voxel!.WorldPosition;
                _sparseStorage.TryRemoveVoxel(this, voxelIndex, out _);

                IncrementVersion();
                GridEventInfo eventInfo = world.CreateGridEventInfo(
                    this,
                    GridEventKind.SparseVoxelRemoved,
                    voxelIndex,
                    affectedPosition,
                    affectedPosition,
                    world.AllocateChangeStamp(),
                    hasVoxelState: true,
                    isVoxelPresent: false,
                    obstacleCount: 0);
                drainCommittedChanges = world.EnqueueCommittedChange(new GridCommittedChange(eventInfo));
            }
        }
        finally
        {
            world.ExitReadLock();
        }

        if (drainCommittedChanges)
            world.DrainCommittedChanges();
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
    /// <param name="x">The local X or axial Q address component.</param>
    /// <param name="y">The local Y or vertical layer address component.</param>
    /// <param name="z">The local Z or axial R address component.</param>
    /// <param name="result">The resolved value on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when a physical voxel is found; otherwise false and result is null.</returns>
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
    /// <returns>A lazy storage traversal of physical voxels only. References are borrowed and invalidated by grid mutation, removal, reset, or pooled reuse.</returns>
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
    /// <param name="voxelIndex">The topology-local X/Y/Z or Q/layer/R address.</param>
    /// <param name="result">The resolved value on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when a physical voxel is found; otherwise false and result is null.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetVoxel(VoxelIndex voxelIndex, out Voxel? result) =>
         TryGetVoxel(voxelIndex.x, voxelIndex.y, voxelIndex.z, out result);

    // The caller already reserved the address inspection against a stable world snapshot.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryInspectVoxel(VoxelIndex index, ulong limit, ref ulong work, out Voxel? result)
    {
        if (ReferenceEquals(_storage, _sparseStorage))
            return _sparseStorage.TryInspectVoxel(index, limit, ref work, out result);

        // Candidate ranges contain valid local indices and dense storage has no holes.
        _denseStorage.TryGetVoxel(index.x, index.y, index.z, out result);
        return true;
    }

    /// <summary>
    /// Retrieve <see cref="Voxel"/> from world <see cref="Vector3d"/> points
    /// </summary>
    /// <returns><see cref="Voxel"/> at the given position or null if the position is not valid.</returns>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="result">The resolved value on success; default on failure. Use it only when the operation succeeds.</param>
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
    /// <param name="position">The fixed-point position in world units.</param>
    /// <returns>The flattened scan-cell key computed from this position/address. Callers must supply an in-bounds address; this method does not validate it.</returns>
    public int GetScanCellKey(Vector3d position)
    {
        if (!TryGetVoxelIndex(position, out VoxelIndex voxelIndex))
            return -1;

        return GetScanCellKey(voxelIndex);
    }

    /// <summary>
    /// Calculates the spatial cell index for a given position.
    /// </summary>
    /// <param name="voxelIndex">The topology-local X/Y/Z or Q/layer/R address.</param>
    /// <returns>The flattened scan-cell key computed from this position/address. Callers must supply an in-bounds address; this method does not validate it.</returns>
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
    /// <param name="key">The grid-local scan-cell key.</param>
    /// <param name="outScanCell">The resolved scan cell on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the computed key resolves to an existing scan cell; otherwise false and the output is null.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetScanCell(int key, out ScanCell? outScanCell)
    {
        outScanCell = null;
        return _storage?.TryGetScanCell(key, out outScanCell) == true;
    }

    /// <summary>
    /// Retrieves the scan cell corresponding to a given world position.
    /// </summary>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="outScanCell">The resolved scan cell on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the computed key resolves to an existing scan cell; otherwise false and the output is null.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetScanCell(Vector3d position, out ScanCell? outScanCell)
    {
        int key = GetScanCellKey(position);
        return TryGetScanCell(key, out outScanCell);
    }

    /// <summary>
    /// Retrieves the scan cell associated with the given voxel index.
    /// </summary>
    /// <param name="voxelIndex">The topology-local X/Y/Z or Q/layer/R address.</param>
    /// <param name="outScanCell">The resolved scan cell on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the computed key resolves to an existing scan cell; otherwise false and the output is null.</returns>
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
    /// <returns>A lazy view of scan cells currently containing occupants. Occupancy mutation invalidates enumeration; cells are borrowed until grid reset.</returns>
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
    /// <param name="position">The fixed-point position in world units.</param>
    /// <returns>For rectangular grids, the ceiling world-space cell origin on each axis, clamped to the address space. For hex grids, both floor and ceiling methods return the same rounded axial cell with a floored vertical layer, clamped to dimensions.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d CeilToGrid(Vector3d position) =>
         Topology.CeilToGrid(BoundsMin, BoundsMax, Width, Height, Length, position);

    /// <summary>
    /// Helper function to floor snap a <see cref="Vector3d"/> through this grid's topology, ensuring it stays within grid bounds.
    /// </summary>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <returns>For rectangular grids, the floor world-space cell origin on each axis, clamped to the address space. For hex grids, both floor and ceiling methods return the same rounded axial cell with a floored vertical layer, clamped to dimensions.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d FloorToGrid(Vector3d position) =>
        Topology.FloorToGrid(BoundsMin, BoundsMax, Width, Height, Length, position);

    /// <summary>
    /// Snaps a given position to the topology-local scan cell in the grid.
    /// </summary>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <returns>The topology-local scan-cell (x, y, z) coordinate; this is not a voxel index or world position.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int x, int y, int z) SnapToScanCell(Vector3d position) =>
        Topology.SnapToScanCell(BoundsMin, position, ScanCellSize);

    /// <summary>
    /// Normalizes world-space bounds to this grid's topology-aligned coverage bounds.
    /// </summary>
    /// <param name="min">The first world-space bounds corner.</param>
    /// <param name="max">The second world-space bounds corner.</param>
    /// <param name="padding">Optional positive expansion in world units, subtracted from the first corner and added to the second before normalization. Null, zero, and negative values use zero.</param>
    /// <returns>Topology-aligned minimum and maximum bounds suitable for deterministic coverage scans.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (Vector3d min, Vector3d max) NormalizeBounds(Vector3d min, Vector3d max, Fixed64? padding = null) =>
        Topology.NormalizeBounds(min, max, padding);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Vector3d GetWorldPosition(VoxelIndex index) =>
        Topology.GetWorldPosition(BoundsMin, index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Vector3d GetWorldOffset((int x, int y, int z) offset) =>
        Topology.GetWorldOffset(offset);

    #endregion
}
