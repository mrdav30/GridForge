using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;

namespace GridForge.Grids;

/// <summary>
/// Temporary compatibility facade over a default <see cref="GridWorld"/> instance.
/// </summary>
public static class GlobalGridManager
{
    #region Constants

    /// <summary>
    /// Maximum number of grids that can be managed.
    /// </summary>
    public const ushort MaxGrids = GridWorld.MaxGrids;

    /// <summary>
    /// The default size of each grid voxel in world units.
    /// </summary>
    public static readonly Fixed64 DefaultVoxelSize = GridWorld.DefaultVoxelSize;

    /// <summary>
    /// The default size of a spatial hash cell used for grid lookup.
    /// </summary>
    public const int DefaultSpatialGridCellSize = GridWorld.DefaultSpatialGridCellSize;

    #endregion

    #region Properties

    /// <summary>
    /// The size of each grid voxel in world units.
    /// </summary>
    public static Fixed64 VoxelSize { get; private set; }

    /// <summary>
    /// The size of a spatial hash cell used for grid lookup.
    /// </summary>
    public static int SpatialGridCellSize { get; private set; }

    /// <summary>
    /// Resolution for snapping or searching within the grid.
    /// </summary>
    public static Fixed64 VoxelResolution => VoxelSize * Fixed64.Half;

    /// <summary>
    /// Collection of all active grids managed by the default world.
    /// </summary>
    public static SwiftBucket<VoxelGrid> ActiveGrids { get; private set; } = null!;

    /// <summary>
    /// Dictionary mapping exact bounds keys to grid indices to prevent duplicate grids.
    /// </summary>
    public static SwiftDictionary<BoundsKey, ushort> BoundsTracker { get; private set; } = null!;

    /// <summary>
    /// Dictionary mapping spatial hash keys to grid indices for fast lookups.
    /// </summary>
    public static SwiftDictionary<int, SwiftHashSet<ushort>> SpatialGridHash { get; private set; } = null!;

    /// <summary>
    /// The current version of the default world.
    /// </summary>
    public static uint Version { get; private set; }

    /// <summary>
    /// Indicates whether the default world is active and initialized.
    /// </summary>
    public static bool IsActive { get; private set; }

    internal static GridWorld? DefaultWorld { get; private set; }

    #endregion

    #region Events

    private static Action<GridEventInfo>? _onActiveGridAdded;
    private static Action<GridEventInfo>? _onActiveGridRemoved;
    private static Action<GridEventInfo>? _onActiveGridChange;
    private static Action? _onReset;

    /// <summary>
    /// Event triggered when a new grid is added.
    /// </summary>
    public static event Action<GridEventInfo> OnActiveGridAdded
    {
        add => _onActiveGridAdded += value;
        remove => _onActiveGridAdded -= value;
    }

    /// <summary>
    /// Event triggered when a grid is removed.
    /// </summary>
    public static event Action<GridEventInfo> OnActiveGridRemoved
    {
        add => _onActiveGridRemoved += value;
        remove => _onActiveGridRemoved -= value;
    }

    /// <summary>
    /// Event triggered when a grid undergoes a significant change.
    /// </summary>
    public static event Action<GridEventInfo> OnActiveGridChange
    {
        add => _onActiveGridChange += value;
        remove => _onActiveGridChange -= value;
    }

    /// <summary>
    /// Event triggered when the default world is reset.
    /// </summary>
    public static event Action OnReset
    {
        add => _onReset += value;
        remove => _onReset -= value;
    }

    #endregion

    static GlobalGridManager()
    {
        InitializeInactiveState();
    }

    #region Setup & Reset

    /// <summary>
    /// Initializes the default world using the default settings.
    /// </summary>
    public static void Setup() => Setup(DefaultVoxelSize, DefaultSpatialGridCellSize);

    /// <summary>
    /// Initializes the default world using the supplied settings.
    /// </summary>
    /// <param name="voxelSize">Optional voxel size for the default world.</param>
    /// <param name="spatialGridCellSize">Optional spatial hash cell size for the default world.</param>
    public static void Setup(
        Fixed64? voxelSize = null,
        int spatialGridCellSize = DefaultSpatialGridCellSize)
    {
        if (IsActive)
        {
            GridForgeLogger.Warn("Global Grid Manager already active. Call `Reset` before attempting to setup.");
            return;
        }

        if (DefaultWorld != null)
            DetachDefaultWorld(DefaultWorld);

        DefaultWorld = new GridWorld(voxelSize, spatialGridCellSize);
        AttachDefaultWorld(DefaultWorld);
        SyncFromDefaultWorld();
    }

    /// <summary>
    /// Resets the default world, clearing all grids and spatial data.
    /// </summary>
    /// <param name="deactivate">If true, deactivates the facade and clears its subscribers.</param>
    public static void Reset(bool deactivate = false)
    {
        if (!IsActive || DefaultWorld == null)
        {
            GridForgeLogger.Warn("Global Grid Manager not active. Call `Setup` before attempting to reset.");
            return;
        }

        GridWorld currentWorld = DefaultWorld;
        currentWorld.Reset(deactivate);
        SyncFromDefaultWorld();

        if (!deactivate)
            return;

        DetachDefaultWorld(currentWorld);
        DefaultWorld = null;
        _onActiveGridAdded = null;
        _onActiveGridRemoved = null;
        _onActiveGridChange = null;
        _onReset = null;
        InitializeInactiveState();
    }

    #endregion

    #region Grid Management

    /// <summary>
    /// Adds a new grid to the default world.
    /// </summary>
    public static bool TryAddGrid(GridConfiguration configuration, out ushort allocatedIndex)
    {
        allocatedIndex = ushort.MaxValue;
        if (!TryGetDefaultWorld(out GridWorld? world))
            return false;

        bool result = world!.TryAddGrid(configuration, out allocatedIndex);
        SyncFromDefaultWorld();
        return result;
    }

    /// <summary>
    /// Removes a grid from the default world.
    /// </summary>
    public static bool TryRemoveGrid(ushort removeIndex)
    {
        if (!TryGetDefaultWorld(out GridWorld? world))
            return false;

        bool result = world!.TryRemoveGrid(removeIndex);
        SyncFromDefaultWorld();
        return result;
    }

    internal static void NotifyActiveGridChange(VoxelGrid grid)
    {
        if (grid == null)
            return;

        if (grid.World != null)
        {
            grid.World.NotifyActiveGridChange(grid);
            if (ReferenceEquals(grid.World, DefaultWorld))
                SyncFromDefaultWorld();
            return;
        }

        if (!TryGetDefaultWorld(out GridWorld? world))
            return;

        world!.NotifyActiveGridChange(grid);
        SyncFromDefaultWorld();
    }

    /// <summary>
    /// Increments the version of a grid in the default world.
    /// </summary>
    public static void IncrementGridVersion(int index, bool significant = false)
    {
        if (!TryGetDefaultWorld(out GridWorld? world))
            return;

        world!.IncrementGridVersion(index, significant);
        SyncFromDefaultWorld();
    }

    #endregion

    #region Grid Lookup & Querying

    /// <summary>
    /// Retrieves a grid by its index.
    /// </summary>
    public static bool TryGetGrid(int index, out VoxelGrid? outGrid)
    {
        outGrid = null;
        return TryGetDefaultWorld(out GridWorld? world)
            && world!.TryGetGrid(index, out outGrid);
    }

    /// <summary>
    /// Retrieves the grid containing a given world position.
    /// </summary>
    public static bool TryGetGrid(Vector3d position, out VoxelGrid? outGrid)
    {
        outGrid = null;
        return TryGetDefaultWorld(out GridWorld? world)
            && world!.TryGetGrid(position, out outGrid);
    }

    /// <summary>
    /// Retrieves a grid by its unique global voxel identity.
    /// </summary>
    public static bool TryGetGrid(GlobalVoxelIndex globalVoxelIndex, out VoxelGrid? result)
    {
        result = null;
        return TryGetDefaultWorld(out GridWorld? world)
            && world!.TryGetGrid(globalVoxelIndex, out result);
    }

    /// <summary>
    /// Retrieves the grid containing a given world position and the voxel at that position.
    /// </summary>
    public static bool TryGetGridAndVoxel(
        Vector3d position,
        out VoxelGrid? outGrid,
        out Voxel? outVoxel)
    {
        outGrid = null;
        outVoxel = null;
        return TryGetDefaultWorld(out GridWorld? world)
            && world!.TryGetGridAndVoxel(position, out outGrid, out outVoxel);
    }

    /// <summary>
    /// Retrieves the grid containing a given global coordinate and the voxel at that position.
    /// </summary>
    public static bool TryGetGridAndVoxel(
        GlobalVoxelIndex globalVoxelIndex,
        out VoxelGrid? outGrid,
        out Voxel? result)
    {
        outGrid = null;
        result = null;
        return TryGetDefaultWorld(out GridWorld? world)
            && world!.TryGetGridAndVoxel(globalVoxelIndex, out outGrid, out result);
    }

    /// <summary>
    /// Retrieves the voxel from the given position.
    /// </summary>
    public static bool TryGetVoxel(
        Vector3d position,
        out Voxel? result)
    {
        result = null;
        return TryGetDefaultWorld(out GridWorld? world)
            && world!.TryGetVoxel(position, out result);
    }

    /// <summary>
    /// Retrieves the voxel from the given global coordinate.
    /// </summary>
    public static bool TryGetVoxel(
        GlobalVoxelIndex globalVoxelIndex,
        out Voxel? result)
    {
        result = null;
        return TryGetDefaultWorld(out GridWorld? world)
            && world!.TryGetVoxel(globalVoxelIndex, out result);
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Retrieves all spatial hash cell indices that intersect the given bounding volume.
    /// </summary>
    public static IEnumerable<int> GetSpatialGridCells(Vector3d min, Vector3d max)
    {
        return DefaultWorld?.GetSpatialGridCells(min, max) ?? Array.Empty<int>();
    }

    /// <summary>
    /// Finds grids that overlap with the specified target grid.
    /// </summary>
    public static IEnumerable<VoxelGrid> FindOverlappingGrids(VoxelGrid targetGrid)
    {
        if (targetGrid == null)
            return Array.Empty<VoxelGrid>();

        if (targetGrid.World != null)
            return targetGrid.World.FindOverlappingGrids(targetGrid);

        return DefaultWorld?.FindOverlappingGrids(targetGrid) ?? Array.Empty<VoxelGrid>();
    }

    /// <summary>
    /// Computes a spatial hash key for a given position.
    /// </summary>
    public static int GetSpatialGridKey(Vector3d position)
    {
        return DefaultWorld?.GetSpatialGridKey(position) ?? ComputeSpatialGridKey(position, SpatialGridCellSize);
    }

    /// <summary>
    /// Helper function to ceil snap a <see cref="Vector3d"/> to the default voxel size.
    /// </summary>
    public static Vector3d CeilToVoxelSize(Vector3d position)
    {
        return DefaultWorld?.CeilToVoxelSize(position) ?? CeilToVoxelSize(position, VoxelSize);
    }

    /// <summary>
    /// Helper function to floor snap a <see cref="Vector3d"/> to the default voxel size.
    /// </summary>
    public static Vector3d FloorToVoxelSize(Vector3d position)
    {
        return DefaultWorld?.FloorToVoxelSize(position) ?? FloorToVoxelSize(position, VoxelSize);
    }

    /// <summary>
    /// Snaps the given bounds to the default voxel size.
    /// </summary>
    public static (Vector3d min, Vector3d max) SnapBoundsToVoxelSize(
        Vector3d min,
        Vector3d max,
        Fixed64? padding = null)
    {
        if (DefaultWorld != null)
            return DefaultWorld.SnapBoundsToVoxelSize(min, max, padding);

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

    /// <summary>
    /// Converts a 3D offset into a corresponding <see cref="SpatialDirection"/>.
    /// </summary>
    public static SpatialDirection GetNeighborDirectionFromOffset((int x, int y, int z) gridOffset)
    {
        return GridDirectionUtility.GetNeighborDirectionFromOffset(gridOffset);
    }

    #endregion

    #region Private Helpers

    private static void AttachDefaultWorld(GridWorld world)
    {
        world.OnActiveGridAdded += HandleDefaultWorldGridAdded;
        world.OnActiveGridRemoved += HandleDefaultWorldGridRemoved;
        world.OnActiveGridChange += HandleDefaultWorldGridChanged;
        world.OnReset += HandleDefaultWorldReset;
    }

    private static void DetachDefaultWorld(GridWorld world)
    {
        world.OnActiveGridAdded -= HandleDefaultWorldGridAdded;
        world.OnActiveGridRemoved -= HandleDefaultWorldGridRemoved;
        world.OnActiveGridChange -= HandleDefaultWorldGridChanged;
        world.OnReset -= HandleDefaultWorldReset;
    }

    private static void HandleDefaultWorldGridAdded(GridEventInfo eventInfo)
    {
        SyncFromDefaultWorld();
        NotifyActiveGridAdded(eventInfo);
    }

    private static void HandleDefaultWorldGridRemoved(GridEventInfo eventInfo)
    {
        SyncFromDefaultWorld();
        NotifyActiveGridRemoved(eventInfo);
    }

    private static void HandleDefaultWorldGridChanged(GridEventInfo eventInfo)
    {
        SyncFromDefaultWorld();
        NotifyActiveGridChange(eventInfo);
    }

    private static void HandleDefaultWorldReset()
    {
        SyncFromDefaultWorld();
        NotifyReset();
    }

    private static bool TryGetDefaultWorld(out GridWorld? world)
    {
        world = DefaultWorld;
        if (world != null && world.IsActive)
            return true;

        GridForgeLogger.Warn("Global Grid Manager not active. Call `Setup` first.");
        world = null;
        return false;
    }

    private static void SyncFromDefaultWorld()
    {
        if (DefaultWorld == null)
        {
            InitializeInactiveState();
            return;
        }

        VoxelSize = DefaultWorld.VoxelSize;
        SpatialGridCellSize = DefaultWorld.SpatialGridCellSize;
        ActiveGrids = DefaultWorld.ActiveGrids;
        BoundsTracker = DefaultWorld.BoundsTracker;
        SpatialGridHash = DefaultWorld.SpatialGridHash;
        Version = DefaultWorld.Version;
        IsActive = DefaultWorld.IsActive;
    }

    private static void InitializeInactiveState()
    {
        VoxelSize = DefaultVoxelSize;
        SpatialGridCellSize = DefaultSpatialGridCellSize;
        ActiveGrids = new SwiftBucket<VoxelGrid>();
        BoundsTracker = new SwiftDictionary<BoundsKey, ushort>();
        SpatialGridHash = new SwiftDictionary<int, SwiftHashSet<ushort>>();
        Version = 0;
        IsActive = false;
    }

    private static Vector3d CeilToVoxelSize(Vector3d position, Fixed64 voxelSize)
    {
        return new Vector3d(
            (position.x.Abs() / voxelSize).CeilToInt() * voxelSize * position.x.Sign(),
            (position.y.Abs() / voxelSize).CeilToInt() * voxelSize * position.y.Sign(),
            (position.z.Abs() / voxelSize).CeilToInt() * voxelSize * position.z.Sign()
        );
    }

    private static Vector3d FloorToVoxelSize(Vector3d position, Fixed64 voxelSize)
    {
        return new Vector3d(
            (position.x.Abs() / voxelSize).FloorToInt() * voxelSize * position.x.Sign(),
            (position.y.Abs() / voxelSize).FloorToInt() * voxelSize * position.y.Sign(),
            (position.z.Abs() / voxelSize).FloorToInt() * voxelSize * position.z.Sign()
        );
    }

    private static int ComputeSpatialGridKey(Vector3d position, int spatialGridCellSize)
    {
        (int x, int y, int z) = (
            position.x.FloorToInt() / spatialGridCellSize,
            position.y.FloorToInt() / spatialGridCellSize,
            position.z.FloorToInt() / spatialGridCellSize
        );

        return SwiftHashTools.CombineHashCodes(x, y, z);
    }

    private static void NotifyActiveGridAdded(GridEventInfo eventInfo)
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

    private static void NotifyActiveGridRemoved(GridEventInfo eventInfo)
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

    private static void NotifyActiveGridChange(GridEventInfo eventInfo)
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

    private static void NotifyReset()
    {
        Action? handlers = _onReset;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action)handlerDelegates[i])();
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"Reset notification error: {ex.Message}");
            }
        }
    }

    #endregion
}
