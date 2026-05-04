using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GridForge.Grids;

/// <summary>
/// Provides utility methods for managing voxel occupants within a grid.
/// Supports adding, removing, and retrieving occupants with thread-safe operations.
/// </summary>
public static class GridOccupantManager
{
    #region Constants & Events

    /// <summary>
    /// Maximum number of occupants allowed per voxel.
    /// </summary>
    public const byte MaxOccupantCount = byte.MaxValue;

    /// <summary>
    /// Event triggered when an occupant is added.
    /// </summary>
    private static Action<OccupantEventInfo>? _onOccupantAdded;

    /// <inheritdoc cref="_onOccupantAdded"/>
    public static event Action<OccupantEventInfo> OnOccupantAdded
    {
        add => _onOccupantAdded += value;
        remove => _onOccupantAdded -= value;
    }

    /// <summary>
    /// Event triggered when an occupant is removed.
    /// </summary>
    private static Action<OccupantEventInfo>? _onOccupantRemoved;

    /// <inheritdoc cref="_onOccupantRemoved"/>
    public static event Action<OccupantEventInfo> OnOccupantRemoved
    {
        add => _onOccupantRemoved += value;
        remove => _onOccupantRemoved -= value;
    }

    #endregion

    #region Private Fields

    /// <summary>
    /// Tracks occupant registrations independently for each world.
    /// </summary>
    private static readonly ConcurrentDictionary<GridWorld, WorldOccupancyRegistry> _occupancyRegistries = new();

    /// <summary>
    /// Tracks all voxel registrations for a single occupant.
    /// </summary>
    private sealed class OccupancyRecord
    {
        public readonly IVoxelOccupant Occupant;
        public readonly SwiftDictionary<WorldVoxelIndex, int> Tickets = new();

        public OccupancyRecord(IVoxelOccupant occupant)
        {
            Occupant = occupant;
        }
    }

    /// <summary>
    /// Synchronizes one world's tracked occupant registrations.
    /// </summary>
    private sealed class WorldOccupancyRegistry
    {
        public readonly object SyncRoot = new();
        public readonly SwiftDictionary<Guid, OccupancyRecord> Records = new();
    }

    /// <summary>
    /// Immutable snapshot of one tracked occupancy.
    /// </summary>
    private readonly struct TrackedOccupancy
    {
        public readonly WorldVoxelIndex VoxelIndex;
        public readonly int Ticket;

        public TrackedOccupancy(WorldVoxelIndex voxelIndex, int ticket)
        {
            VoxelIndex = voxelIndex;
            Ticket = ticket;
        }
    }

    #endregion

    #region Occupant Management

    /// <summary>
    /// Attempts to register the occupant with the current voxel it is on in the supplied world.
    /// </summary>
    public static bool TryRegister(GridWorld world, IVoxelOccupant occupant)
    {
        return occupant != null
            && world != null
            && world.TryGetGridAndVoxel(occupant.Position, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryAddVoxelOccupant(voxel!, occupant);
    }

    /// <summary>
    /// Returns a snapshot of the voxel indices currently tracked for the occupant in the supplied world.
    /// </summary>
    /// <param name="world">The world whose occupancy registry should be queried.</param>
    /// <param name="occupant">The occupant whose tracked voxel indices should be returned.</param>
    /// <returns>A deterministic snapshot of the occupant's currently tracked voxel indices.</returns>
    public static IEnumerable<WorldVoxelIndex> GetOccupiedIndices(GridWorld world, IVoxelOccupant occupant)
    {
        TrackedOccupancy[] occupancies = GetTrackedOccupanciesSnapshot(world, occupant);
        if (occupancies.Length == 0)
            return Array.Empty<WorldVoxelIndex>();

        WorldVoxelIndex[] indices = new WorldVoxelIndex[occupancies.Length];
        for (int i = 0; i < occupancies.Length; i++)
            indices[i] = occupancies[i].VoxelIndex;

        return indices;
    }

    /// <summary>
    /// Attempts to retrieve the scan-cell ticket for the occupant at a specific voxel in the supplied world.
    /// </summary>
    /// <param name="world">The world whose occupancy registry should be queried.</param>
    /// <param name="occupant">The occupant whose registration should be queried.</param>
    /// <param name="index">The tracked voxel index to look up.</param>
    /// <param name="ticket">The tracked scan-cell ticket for the occupant at that voxel.</param>
    /// <returns>True if the occupant is registered to the voxel, otherwise false.</returns>
    public static bool TryGetOccupancyTicket(
        GridWorld world,
        IVoxelOccupant occupant,
        WorldVoxelIndex index,
        out int ticket)
    {
        ticket = -1;
        if (world == null || occupant == null || !TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
            return false;

        lock (registry!.SyncRoot)
            return TryGetTrackedRecordUnsafe(world, occupant, out OccupancyRecord? record)
                && record!.Tickets.TryGetValue(index, out ticket);
    }

    /// <summary>
    /// Attempts to add an occupant from the given world-scoped voxel identity in the supplied world.
    /// </summary>
    public static bool TryAddVoxelOccupant(
        GridWorld world,
        WorldVoxelIndex index,
        IVoxelOccupant occupant)
    {
        return occupant != null
            && world != null
            && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryAddVoxelOccupant(voxel!, occupant);
    }

    /// <summary>
    /// Attempts to add an occupant at the given world position.
    /// </summary>
    public static bool TryAddVoxelOccupant(this VoxelGrid grid, IVoxelOccupant occupant)
    {
        return occupant != null
            && grid.TryGetVoxel(occupant.Position, out Voxel? voxel)
            && grid.TryAddVoxelOccupant(voxel!, occupant);
    }

    /// <summary>
    /// Attempts to add an occupant at the specified voxel index.
    /// </summary>
    public static bool TryAddVoxelOccupant(
        this VoxelGrid grid,
        VoxelIndex voxelIndex,
        IVoxelOccupant occupant)
    {
        return occupant != null
            && grid.TryGetVoxel(voxelIndex, out Voxel? target)
            && grid.TryAddVoxelOccupant(target!, occupant);
    }

    /// <summary>
    /// Adds an occupant to the grid.
    /// </summary>
    public static bool TryAddVoxelOccupant(
        this VoxelGrid grid,
        Voxel targetVoxel,
        IVoxelOccupant occupant)
    {
        if (occupant == null || grid.World == null)
            return false;

        if (!targetVoxel.HasVacancy || !grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell? scanCell))
            return false;

        int ticket;
        byte occupantCount;

        lock (grid.OccupantSyncRoot)
        {
            ticket = scanCell!.AddOccupant(targetVoxel.WorldIndex, occupant);
            if (!TryTrackOccupancy(grid.World, occupant, targetVoxel.WorldIndex, ticket))
            {
                scanCell.TryRemoveOccupant(targetVoxel.WorldIndex, ticket);
                return false;
            }

            grid.ActiveScanCells ??= SwiftHashSetPool<int>.Shared.Rent();
            if (!grid.ActiveScanCells.Contains(targetVoxel.ScanCellKey))
                grid.ActiveScanCells.Add(targetVoxel.ScanCellKey);

            targetVoxel.OccupantCount++;
            occupantCount = targetVoxel.OccupantCount;
        }

        NotifyOccupantAdded(targetVoxel, occupant, ticket, occupantCount);

        return true;
    }

    /// <summary>
    /// Attempts to de-register the occupant from every voxel currently tracked in the supplied world.
    /// </summary>
    public static bool TryDeregister(GridWorld world, IVoxelOccupant occupant)
    {
        TrackedOccupancy[] occupancies = GetTrackedOccupanciesSnapshot(world, occupant);
        if (occupancies.Length == 0)
            return false;

        bool removedAny = false;

        for (int i = 0; i < occupancies.Length; i++)
        {
            WorldVoxelIndex voxelIndex = occupancies[i].VoxelIndex;
            if (!world.TryGetGridAndVoxel(voxelIndex, out VoxelGrid? grid, out Voxel? voxel))
            {
                removedAny |= ForgetTrackedOccupancy(world, occupant, voxelIndex);
                continue;
            }

            removedAny |= grid!.TryRemoveVoxelOccupant(voxel!, occupant);
        }

        return removedAny;
    }

    /// <summary>
    /// Attempts to remove an occupant from the given world-scoped voxel identity in the supplied world.
    /// </summary>
    public static bool TryRemoveVoxelOccupant(
        GridWorld world,
        WorldVoxelIndex index,
        IVoxelOccupant occupant)
    {
        return occupant != null
            && world != null
            && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryRemoveVoxelOccupant(voxel!, occupant);
    }

    /// <summary>
    /// Attempts to remove an occupant from the given world position.
    /// </summary>
    public static bool TryRemoveVoxelOccupant(
        this VoxelGrid grid,
        IVoxelOccupant occupant)
    {
        if (grid.World == null)
            return false;

        TrackedOccupancy[] occupancies = GetTrackedOccupanciesSnapshot(grid.World, occupant, grid.GridIndex);
        if (occupancies.Length == 0)
            return false;

        bool removedAny = false;

        for (int i = 0; i < occupancies.Length; i++)
        {
            WorldVoxelIndex voxelIndex = occupancies[i].VoxelIndex;
            if (voxelIndex.WorldSpawnToken != grid.World.SpawnToken
                || voxelIndex.GridSpawnToken != grid.SpawnToken
                || !grid.TryGetVoxel(voxelIndex.VoxelIndex, out Voxel? voxel))
            {
                removedAny |= ForgetTrackedOccupancy(grid.World, occupant, voxelIndex);
                continue;
            }

            removedAny |= TryRemoveVoxelOccupant(grid, voxel!, occupant);
        }

        return removedAny;
    }

    /// <summary>
    /// Attempts to remove an occupant at the specified voxel coordinates.
    /// </summary>
    public static bool TryRemoveVoxelOccupant(
        this VoxelGrid grid,
        VoxelIndex voxelIndex,
        IVoxelOccupant occupant)
    {
        return occupant != null
            && grid.TryGetVoxel(voxelIndex, out Voxel? targetVoxel)
            && grid.TryRemoveVoxelOccupant(targetVoxel!, occupant);
    }

    /// <summary>
    /// Removes an occupant from this grid.
    /// </summary>
    public static bool TryRemoveVoxelOccupant(
        this VoxelGrid grid,
        Voxel targetVoxel,
        IVoxelOccupant occupant)
    {
        if (occupant == null || grid.World == null)
            return false;

        if (!TryGetOccupancyTicket(grid.World, occupant, targetVoxel.WorldIndex, out int ticket))
        {
            GridForgeLogger.Channel.Warn($"Occupant {occupant.GlobalId} is not registered to voxel {targetVoxel.WorldIndex}.");
            return false;
        }

        if (!targetVoxel.IsOccupied || grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell? scanCell) != true)
            return false;

        bool success = false;
        byte occupantCount = targetVoxel.OccupantCount;

        lock (grid.OccupantSyncRoot)
        {
            success = scanCell!.TryRemoveOccupant(targetVoxel.WorldIndex, ticket);
            if (success)
            {
                ForgetTrackedOccupancy(grid.World, occupant, targetVoxel.WorldIndex);

                if (!scanCell.IsOccupied && grid.ActiveScanCells != null)
                {
                    grid.ActiveScanCells.Remove(targetVoxel.ScanCellKey);
                    if (!grid.IsOccupied)
                    {
                        GridForgeLogger.Channel.Info($"Releasing unused active scan cells collection.");
                        SwiftHashSetPool<int>.Shared.Release(grid.ActiveScanCells);
                        grid.ActiveScanCells = null;
                    }
                }

                targetVoxel.OccupantCount--;
                occupantCount = targetVoxel.OccupantCount;
            }
        }

        if (success)
            NotifyOccupantRemoved(targetVoxel, occupant, ticket, occupantCount);

        return success;
    }

    #endregion

    #region Internal Helpers

    internal static void ForgetTrackedOccupancies(
        GridWorld? world,
        IEnumerable<IVoxelOccupant> occupants,
        WorldVoxelIndex index)
    {
        if (world == null)
            return;

        foreach (IVoxelOccupant occupant in occupants)
            ForgetTrackedOccupancy(world, occupant, index);
    }

    internal static void ClearTrackedOccupancies(GridWorld world)
    {
        if (world == null || !TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
            return;

        lock (registry!.SyncRoot)
            registry.Records.Clear();
    }

    internal static void ReleaseTrackedOccupancies(GridWorld world)
    {
        if (world == null || !_occupancyRegistries.TryRemove(world, out WorldOccupancyRegistry? registry))
            return;

        lock (registry.SyncRoot)
            registry.Records.Clear();
    }

    #endregion

    #region Private Methods

    private static bool TryTrackOccupancy(
        GridWorld world,
        IVoxelOccupant occupant,
        WorldVoxelIndex index,
        int ticket)
    {
        if (world == null)
            return false;

        WorldOccupancyRegistry registry = GetWorldRegistry(world);
        lock (registry.SyncRoot)
        {
            if (registry.Records.TryGetValue(occupant.GlobalId, out OccupancyRecord record))
            {
                if (!ReferenceEquals(record.Occupant, occupant))
                {
                    GridForgeLogger.Channel.Warn($"Occupant id collision detected for {occupant.GlobalId} in world {world.SpawnToken}.");
                    return false;
                }
            }
            else
            {
                record = new OccupancyRecord(occupant);
                registry.Records[occupant.GlobalId] = record;
            }

            if (record.Tickets.ContainsKey(index))
            {
                GridForgeLogger.Channel.Warn($"Occupant {occupant.GlobalId} is already registered to voxel {index}.");
                return false;
            }

            record.Tickets[index] = ticket;
            return true;
        }
    }

    private static bool ForgetTrackedOccupancy(
        GridWorld world,
        IVoxelOccupant occupant,
        WorldVoxelIndex index)
    {
        if (world == null || occupant == null || !TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
            return false;

        lock (registry!.SyncRoot)
        {
            if (!TryGetTrackedRecordUnsafe(world, occupant, out OccupancyRecord? record) || record!.Tickets.ContainsKey(index) != true)
                return false;

            record.Tickets.Remove(index);
            if (record.Tickets.Count == 0)
                registry.Records.Remove(occupant.GlobalId);

            return true;
        }
    }

    private static bool TryGetTrackedRecordUnsafe(
        GridWorld world,
        IVoxelOccupant occupant,
        out OccupancyRecord? record)
    {
        record = null;
        if (world == null
            || occupant == null
            || !TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry)
            || !registry!.Records.TryGetValue(occupant.GlobalId, out record))
        {
            return false;
        }

        return ReferenceEquals(record.Occupant, occupant);
    }

    private static TrackedOccupancy[] GetTrackedOccupanciesSnapshot(
        GridWorld world,
        IVoxelOccupant occupant,
        ushort? gridIndex = null)
    {
        if (world == null || occupant == null || !TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
            return Array.Empty<TrackedOccupancy>();

        lock (registry!.SyncRoot)
        {
            if (!TryGetTrackedRecordUnsafe(world, occupant, out OccupancyRecord? record) || record!.Tickets.Count == 0)
                return Array.Empty<TrackedOccupancy>();

            TrackedOccupancy[] occupancies = new TrackedOccupancy[record.Tickets.Count];
            int count = 0;

            foreach (WorldVoxelIndex index in record.Tickets.Keys)
            {
                if (gridIndex.HasValue && index.GridIndex != gridIndex.Value)
                    continue;

                occupancies[count++] = new TrackedOccupancy(index, record.Tickets[index]);
            }

            if (count == 0)
                return Array.Empty<TrackedOccupancy>();

            if (count != occupancies.Length)
                Array.Resize(ref occupancies, count);

            Array.Sort(occupancies, CompareTrackedOccupancies);
            return occupancies;
        }
    }

    private static int CompareTrackedOccupancies(TrackedOccupancy left, TrackedOccupancy right)
    {
        int result = left.VoxelIndex.WorldSpawnToken.CompareTo(right.VoxelIndex.WorldSpawnToken);
        if (result != 0)
            return result;

        result = left.VoxelIndex.GridIndex.CompareTo(right.VoxelIndex.GridIndex);
        if (result != 0)
            return result;

        result = left.VoxelIndex.VoxelIndex.x.CompareTo(right.VoxelIndex.VoxelIndex.x);
        if (result != 0)
            return result;

        result = left.VoxelIndex.VoxelIndex.y.CompareTo(right.VoxelIndex.VoxelIndex.y);
        if (result != 0)
            return result;

        result = left.VoxelIndex.VoxelIndex.z.CompareTo(right.VoxelIndex.VoxelIndex.z);
        if (result != 0)
            return result;

        result = left.VoxelIndex.GridSpawnToken.CompareTo(right.VoxelIndex.GridSpawnToken);
        if (result != 0)
            return result;

        return left.Ticket.CompareTo(right.Ticket);
    }

    /// <summary>
    /// Notifies listeners that an occupant was added.
    /// </summary>
    private static void NotifyOccupantAdded(
        Voxel targetVoxel,
        IVoxelOccupant occupant,
        int ticket,
        byte occupantCount)
    {
        OccupantEventInfo eventInfo = new(targetVoxel.WorldIndex, occupant, ticket, occupantCount);
        Action<OccupantEventInfo>? handlers = _onOccupantAdded;
        if (handlers != null)
        {
            var handlerDelegates = handlers.GetInvocationList();
            for (int i = 0; i < handlerDelegates.Length; i++)
            {
                try
                {
                    ((Action<OccupantEventInfo>)handlerDelegates[i])(eventInfo);
                }
                catch (Exception ex)
                {
                    GridForgeLogger.Channel.Error($"[Voxel {targetVoxel.WorldIndex}] Occupant add error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyOccupantAdded(eventInfo);
    }

    /// <summary>
    /// Notifies listeners that an occupant was removed.
    /// </summary>
    private static void NotifyOccupantRemoved(
        Voxel targetVoxel,
        IVoxelOccupant occupant,
        int ticket,
        byte occupantCount)
    {
        OccupantEventInfo eventInfo = new(targetVoxel.WorldIndex, occupant, ticket, occupantCount);
        Action<OccupantEventInfo>? handlers = _onOccupantRemoved;
        if (handlers != null)
        {
            var handlerDelegates = handlers.GetInvocationList();
            for (int i = 0; i < handlerDelegates.Length; i++)
            {
                try
                {
                    ((Action<OccupantEventInfo>)handlerDelegates[i])(eventInfo);
                }
                catch (Exception ex)
                {
                    GridForgeLogger.Channel.Error($"[Voxel {targetVoxel.WorldIndex}] Occupant remove error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyOccupantRemoved(eventInfo);
    }

    private static WorldOccupancyRegistry GetWorldRegistry(GridWorld world)
    {
        return _occupancyRegistries.GetOrAdd(world, static _ => new WorldOccupancyRegistry());
    }

    private static bool TryGetWorldRegistry(GridWorld world, out WorldOccupancyRegistry? registry)
    {
        registry = null;
        return world != null && _occupancyRegistries.TryGetValue(world, out registry);
    }

    #endregion
}
