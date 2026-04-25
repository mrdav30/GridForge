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
    /// Per-grid locks to ensure thread safety when modifying occupant data.
    /// </summary>
    private static readonly ConcurrentDictionary<ushort, object> _gridLocks = new();

    /// <summary>
    /// Synchronizes access to the global occupant registry.
    /// </summary>
    private static readonly object _occupancyRegistryLock = new();

    /// <summary>
    /// Tracks all active occupant registrations owned by GridForge.
    /// </summary>
    private static readonly SwiftDictionary<Guid, OccupancyRecord> _occupancyRegistry = new();

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
    /// Attempts to register the occupant with the current voxel it is on.
    /// </summary>
    public static bool TryRegister(IVoxelOccupant occupant)
    {
        if (!GlobalGridManager.TryGetGridAndVoxel(occupant.Position, out VoxelGrid? grid, out Voxel? voxel))
            return false;

        return grid!.TryAddVoxelOccupant(voxel!, occupant);
    }

    /// <summary>
    /// Returns a snapshot of the voxel indices currently tracked for the occupant.
    /// </summary>
    /// <param name="occupant">The occupant whose tracked voxel indices should be returned.</param>
    /// <returns>A deterministic snapshot of the occupant's currently tracked voxel indices.</returns>
    public static IEnumerable<WorldVoxelIndex> GetOccupiedIndices(IVoxelOccupant occupant)
    {
        TrackedOccupancy[] occupancies = GetTrackedOccupanciesSnapshot(occupant);
        if (occupancies.Length == 0)
            return Array.Empty<WorldVoxelIndex>();

        WorldVoxelIndex[] indices = new WorldVoxelIndex[occupancies.Length];
        for (int i = 0; i < occupancies.Length; i++)
            indices[i] = occupancies[i].VoxelIndex;

        return indices;
    }

    /// <summary>
    /// Attempts to retrieve the scan-cell ticket for the occupant at a specific voxel.
    /// </summary>
    /// <param name="occupant">The occupant whose registration should be queried.</param>
    /// <param name="index">The tracked voxel index to look up.</param>
    /// <param name="ticket">The tracked scan-cell ticket for the occupant at that voxel.</param>
    /// <returns>True if the occupant is registered to the voxel, otherwise false.</returns>
    public static bool TryGetOccupancyTicket(
        IVoxelOccupant occupant,
        WorldVoxelIndex index,
        out int ticket)
    {
        ticket = -1;
        if (occupant == null)
            return false;

        lock (_occupancyRegistryLock)
            return TryGetTrackedRecordUnsafe(occupant, out OccupancyRecord? record)
                && record!.Tickets.TryGetValue(index, out ticket);
    }

    /// <summary>
    /// Attempts to add an occupant from the given world-scoped voxel identity.
    /// </summary>
    public static bool TryAddVoxelOccupant(
        WorldVoxelIndex index,
        IVoxelOccupant occupant)
    {
        return occupant != null
            && GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryAddVoxelOccupant(voxel!, occupant);
    }

    /// <summary>
    /// Attempts to add an occupant at the given world position.
    /// </summary>
    public static bool TryAddVoxelOccupant(this VoxelGrid grid, IVoxelOccupant occupant)
    {
        return occupant != null
            && grid.TryGetVoxel(occupant.Position, out Voxel? voxel)
            && grid!.TryAddVoxelOccupant(voxel!, occupant);
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
            && grid!.TryAddVoxelOccupant(target!, occupant);
    }

    /// <summary>
    /// Adds an occupant to the grid.
    /// </summary>
    public static bool TryAddVoxelOccupant(
        this VoxelGrid grid,
        Voxel targetVoxel,
        IVoxelOccupant occupant)
    {
        if (occupant == null)
            return false;

        if (!targetVoxel.HasVacancy || !grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell? scanCell))
            return false;

        object gridLock = _gridLocks.GetOrAdd(grid.GridIndex, _ => new object());
        int ticket = -1;
        byte occupantCount;

        lock (gridLock)
        {
            ticket = scanCell!.AddOccupant(targetVoxel.WorldIndex, occupant);
            if (!TryTrackOccupancy(occupant, targetVoxel.WorldIndex, ticket))
            {
                scanCell!.TryRemoveOccupant(targetVoxel.WorldIndex, ticket);
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
    /// Attempts to de-register the occupant from every voxel currently tracked by GridForge.
    /// </summary>
    public static bool TryDeregister(IVoxelOccupant occupant)
    {
        TrackedOccupancy[] occupancies = GetTrackedOccupanciesSnapshot(occupant);
        if (occupancies.Length == 0)
            return false;

        bool removedAny = false;

        for (int i = 0; i < occupancies.Length; i++)
        {
            WorldVoxelIndex voxelIndex = occupancies[i].VoxelIndex;
            if (!GlobalGridManager.TryGetGridAndVoxel(voxelIndex, out VoxelGrid? grid, out Voxel? voxel))
            {
                removedAny |= ForgetTrackedOccupancy(occupant, voxelIndex);
                continue;
            }

            removedAny |= grid!.TryRemoveVoxelOccupant(voxel!, occupant);
        }

        return removedAny;
    }

    /// <summary>
    /// Attempts to remove an occupant from the given world-scoped voxel identity.
    /// </summary>
    public static bool TryRemoveVoxelOccupant(
        WorldVoxelIndex index,
        IVoxelOccupant occupant)
    {
        return occupant != null
            && GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryRemoveVoxelOccupant(voxel!, occupant);
    }

    /// <summary>
    /// Attempts to remove an occupant from the given world position.
    /// </summary>
    public static bool TryRemoveVoxelOccupant(
        this VoxelGrid grid,
        IVoxelOccupant occupant)
    {
        TrackedOccupancy[] occupancies = GetTrackedOccupanciesSnapshot(occupant, grid.GridIndex);
        if (occupancies.Length == 0)
            return false;

        bool removedAny = false;

        for (int i = 0; i < occupancies.Length; i++)
        {
            WorldVoxelIndex voxelIndex = occupancies[i].VoxelIndex;
            if (voxelIndex.GridSpawnToken != grid.SpawnToken || !grid.TryGetVoxel(voxelIndex.VoxelIndex, out Voxel? voxel))
            {
                removedAny |= ForgetTrackedOccupancy(occupant, voxelIndex);
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
            && grid!.TryRemoveVoxelOccupant(targetVoxel!, occupant);
    }

    /// <summary>
    /// Removes an occupant from this grid.
    /// </summary>
    public static bool TryRemoveVoxelOccupant(
        this VoxelGrid grid,
        Voxel targetVoxel,
        IVoxelOccupant occupant)
    {
        if (occupant == null)
            return false;

        if (!TryGetOccupancyTicket(occupant, targetVoxel.WorldIndex, out int ticket))
        {
            GridForgeLogger.Warn($"Occupant {occupant.GlobalId} is not registered to voxel {targetVoxel.WorldIndex}.");
            return false;
        }

        if (!targetVoxel.IsOccupied || grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell? scanCell) != true)
            return false;

        bool success = false;
        byte occupantCount = targetVoxel.OccupantCount;

        object gridLock = _gridLocks.GetOrAdd(grid.GridIndex, _ => new object());

        lock (gridLock)
        {
            success = scanCell!.TryRemoveOccupant(targetVoxel.WorldIndex, ticket);
            if (success)
            {
                ForgetTrackedOccupancy(occupant, targetVoxel.WorldIndex);

                if (!scanCell.IsOccupied && grid.ActiveScanCells != null)
                {
                    grid.ActiveScanCells.Remove(targetVoxel.ScanCellKey);
                    if (!grid.IsOccupied)
                    {
                        GridForgeLogger.Info($"Releasing unused active scan cells collection.");
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

    internal static void ForgetTrackedOccupancies(IEnumerable<IVoxelOccupant> occupants, WorldVoxelIndex index)
    {
        foreach (IVoxelOccupant occupant in occupants)
            ForgetTrackedOccupancy(occupant, index);
    }

    internal static void ClearTrackedOccupancies()
    {
        lock (_occupancyRegistryLock)
            _occupancyRegistry.Clear();
    }

    #endregion

    #region Private Methods

    private static bool TryTrackOccupancy(
        IVoxelOccupant occupant,
        WorldVoxelIndex index,
        int ticket)
    {
        lock (_occupancyRegistryLock)
        {
            if (_occupancyRegistry.TryGetValue(occupant.GlobalId, out OccupancyRecord record))
            {
                if (!ReferenceEquals(record.Occupant, occupant))
                {
                    GridForgeLogger.Warn($"Occupant id collision detected for {occupant.GlobalId}.");
                    return false;
                }
            }
            else
            {
                record = new OccupancyRecord(occupant);
                _occupancyRegistry[occupant.GlobalId] = record;
            }

            if (record.Tickets.ContainsKey(index))
            {
                GridForgeLogger.Warn($"Occupant {occupant.GlobalId} is already registered to voxel {index}.");
                return false;
            }

            record.Tickets[index] = ticket;
            return true;
        }
    }

    private static bool ForgetTrackedOccupancy(
        IVoxelOccupant occupant,
        WorldVoxelIndex index)
    {
        if (occupant == null)
            return false;

        lock (_occupancyRegistryLock)
        {
            if (!TryGetTrackedRecordUnsafe(occupant, out OccupancyRecord? record) || record!.Tickets.ContainsKey(index) != true)
                return false;

            record.Tickets.Remove(index);
            if (record.Tickets.Count == 0)
                _occupancyRegistry.Remove(occupant.GlobalId);

            return true;
        }
    }

    private static bool TryGetTrackedRecordUnsafe(
        IVoxelOccupant occupant,
        out OccupancyRecord? record)
    {
        record = null;
        if (occupant == null || !_occupancyRegistry.TryGetValue(occupant.GlobalId, out record))
            return false;

        return ReferenceEquals(record.Occupant, occupant);
    }

    private static TrackedOccupancy[] GetTrackedOccupanciesSnapshot(
        IVoxelOccupant occupant,
        ushort? gridIndex = null)
    {
        if (occupant == null)
            return Array.Empty<TrackedOccupancy>();

        lock (_occupancyRegistryLock)
        {
            if (!TryGetTrackedRecordUnsafe(occupant, out OccupancyRecord? record) || record!.Tickets.Count == 0)
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
                    GridForgeLogger.Error($"[Voxel {targetVoxel.WorldIndex}] Occupant add error: {ex.Message}");
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
                    GridForgeLogger.Error($"[Voxel {targetVoxel.WorldIndex}] Occupant remove error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyOccupantRemoved(eventInfo);
    }

    #endregion
}
