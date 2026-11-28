//=======================================================================
// GridOccupantManager.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using GridForge.Diagnostics;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;

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

    /// <summary>Raised when an occupant registration is added.</summary>
    /// <remarks>Handlers run synchronously after the state change. Handler failures are logged and remaining subscribers are still invoked. Static manager/blocker events can contain changes from any world; use the event identity to filter.</remarks>
    public static event Action<OccupantEventInfo> OnOccupantAdded
    {
        add => _onOccupantAdded += value;
        remove => _onOccupantAdded -= value;
    }

    /// <summary>
    /// Event triggered when an occupant is removed.
    /// </summary>
    private static Action<OccupantEventInfo>? _onOccupantRemoved;

    /// <summary>Raised when an occupant registration is removed.</summary>
    /// <remarks>Handlers run synchronously after the state change. Handler failures are logged and remaining subscribers are still invoked. Static manager/blocker events can contain changes from any world; use the event identity to filter.</remarks>
    public static event Action<OccupantEventInfo> OnOccupantRemoved
    {
        add => _onOccupantRemoved += value;
        remove => _onOccupantRemoved -= value;
    }

    #endregion

    #region Private Fields

    /// <summary>
    /// Immutable snapshot of one tracked occupancy.
    /// </summary>
    private readonly struct TrackedOccupancy
    {
        public readonly WorldVoxelIndex VoxelIndex;
        public readonly OccupantTicket Ticket;

        public TrackedOccupancy(WorldVoxelIndex voxelIndex, OccupantTicket ticket)
        {
            VoxelIndex = voxelIndex;
            Ticket = ticket;
        }
    }

    private enum OccupancyDiagnostic
    {
        None,
        Duplicate,
        IdCollision,
        Missing,
        ReleasedActiveCells
    }

    #endregion

    #region Occupant Management

    /// <summary>
    /// Attempts to register the occupant with the current voxel it is on in the supplied world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True when a new registration was created at the occupant position; false for invalid context, blocked/full cells, or duplicate registration.</returns>
    public static bool TryRegister(GridWorld world, IVoxelOccupant occupant)
    {
        return occupant != null
            && world != null
            && world.TryGetGridAndVoxel(occupant.Position, out VoxelGrid? grid, out Voxel? voxel)
            && TryChangeOccupant(world, grid!, voxel!, voxel!.WorldIndex, occupant, add: true);
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
        out OccupantTicket ticket)
    {
        ticket = default;
        if (world == null || occupant == null || !TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
            return false;

        Guid id = occupant.GlobalId;
        lock (registry!.SyncRoot)
            return TryGetTrackedRecordUnsafe(registry, id, occupant, out OccupancyRecord? record)
                && record!.TryGetTicket(index, out ticket);
    }

    /// <summary>
    /// Attempts to add an occupant from the given world-scoped voxel identity in the supplied world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="index">The exact world/grid generation and topology-local voxel identity to resolve.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True when the registration was created; false for invalid context, blocked/full cells, or duplicate registration.</returns>
    public static bool TryAddVoxelOccupant(
        GridWorld world,
        WorldVoxelIndex index,
        IVoxelOccupant occupant)
    {
        return occupant != null
            && world != null
            && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && TryChangeOccupant(world, grid!, voxel!, index, occupant, add: true);
    }

    /// <summary>
    /// Attempts to add an occupant at the given world position.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True when the registration was created; false for invalid context, blocked/full cells, or duplicate registration.</returns>
    public static bool TryAddVoxelOccupant(this VoxelGrid grid, IVoxelOccupant occupant)
    {
        GridWorld? world = grid.World;
        long generation = grid.SpawnToken;
        if (occupant == null || !grid.TryGetVoxel(occupant.Position, out Voxel? voxel))
            return false;
        WorldVoxelIndex index = voxel!.WorldIndex;
        index.GridSpawnToken = generation;
        return TryChangeOccupant(world, grid, voxel, index, occupant, add: true);
    }

    /// <summary>
    /// Attempts to add an occupant at the specified voxel index.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="voxelIndex">The topology-local X/Y/Z or Q/layer/R address.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True when the registration was created; false for invalid context, blocked/full cells, or duplicate registration.</returns>
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
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="targetVoxel">A current physical voxel belonging to the supplied grid.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True when the registration was created; false for invalid context, blocked/full cells, or duplicate registration.</returns>
    /// <remarks>The receiver must own the exact physical voxel at the time of mutation. Resolve a saved
    /// <see cref="WorldVoxelIndex"/> in its world before reusing an address across grid or sparse-voxel changes.</remarks>
    public static bool TryAddVoxelOccupant(
        this VoxelGrid grid,
        Voxel targetVoxel,
        IVoxelOccupant occupant) => targetVoxel != null
            && TryChangeOccupant(grid.World, grid, targetVoxel, targetVoxel.WorldIndex, occupant, add: true);

    /// <summary>
    /// Attempts to de-register the occupant from every voxel currently tracked in the supplied world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True if any tracked registration was removed or stale tracking entry forgotten; false when none could be removed. Earlier successful removals remain applied.</returns>
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
                removedAny |= ForgetTrackedOccupancy(world, occupant, voxelIndex, occupancies[i].Ticket);
                continue;
            }

            removedAny |= TryChangeOccupant(world, grid!, voxel!, voxelIndex, occupant, add: false, occupancies[i].Ticket);
        }

        return removedAny;
    }

    /// <summary>
    /// Attempts to remove an occupant from the given world-scoped voxel identity in the supplied world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="index">The exact world/grid generation and topology-local voxel identity to resolve.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True if a live registration was removed or a stale tracking entry was forgotten; otherwise false. Successful earlier removals remain applied.</returns>
    public static bool TryRemoveVoxelOccupant(
        GridWorld world,
        WorldVoxelIndex index,
        IVoxelOccupant occupant)
    {
        return occupant != null
            && world != null
            && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && TryChangeOccupant(world, grid!, voxel!, index, occupant, add: false);
    }

    /// <summary>Removes all registrations tracked for this occupant in the supplied grid, independently of its current position.</summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True if a live registration was removed or a stale tracking entry was forgotten; otherwise false. Successful earlier removals remain applied.</returns>
    public static bool TryRemoveVoxelOccupant(
        this VoxelGrid grid,
        IVoxelOccupant occupant)
    {
        GridWorld? world = grid.World;
        if (world == null)
            return false;
        long generation = grid.SpawnToken;
        TrackedOccupancy[] occupancies = GetTrackedOccupanciesSnapshot(world, occupant, grid.GridIndex);
        return grid.SpawnToken == generation && RemoveTrackedOccupancies(world, grid, occupant, occupancies);
    }

    /// <summary>
    /// Attempts to remove an occupant at the specified voxel coordinates.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="voxelIndex">The topology-local X/Y/Z or Q/layer/R address.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True if a live registration was removed or a stale tracking entry was forgotten; otherwise false. Successful earlier removals remain applied.</returns>
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
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="targetVoxel">A current physical voxel belonging to the supplied grid.</param>
    /// <param name="occupant">The occupant instance. Successful registration retains this reference until removal or world/grid reset.</param>
    /// <returns>True when this occupant's live registration was removed from the supplied physical voxel.</returns>
    /// <remarks>The receiver must own the exact physical voxel at the time of mutation. Rejected input
    /// preserves the existing registrations and produces no occupant notification.</remarks>
    public static bool TryRemoveVoxelOccupant(
        this VoxelGrid grid,
        Voxel targetVoxel,
        IVoxelOccupant occupant) => targetVoxel != null
            && TryChangeOccupant(grid.World, grid, targetVoxel, targetVoxel.WorldIndex, occupant, add: false);

    private static bool TryChangeOccupant(GridWorld? world, VoxelGrid grid, Voxel voxel, WorldVoxelIndex index,
        IVoxelOccupant occupant, bool add, OccupantTicket expectedTicket = default)
    {
        if (world == null || occupant == null)
            return false;

        ulong materialization = voxel.MaterializationSequence;
        // A host getter can reenter and recycle both the grid and the voxel.
        Guid id = occupant.GlobalId;
        OccupantEventInfo change;
        bool changed;
        OccupancyDiagnostic diagnostic = OccupancyDiagnostic.None;
        // Share admission with grid teardown, sparse mutation and obstacle updates.
        // The occupant lock still protects scan-cell readers and precedes the registry lock.
        lock (world.ChangeSyncRoot)
        {
            world.ActiveGrids.TryGetValue(grid.GridIndex, out VoxelGrid registered);
            if (!ReferenceEquals(registered, grid) || !grid.OwnsPhysicalVoxel(voxel)
                || voxel.WorldIndex != index || voxel.MaterializationSequence != materialization)
                return false;

            lock (grid.OccupantSyncRoot)
            {
                // Every live physical voxel has a scan cell until this storage mutation lock is released.
                grid.ScanCells!.TryGetValue(voxel.ScanCellKey, out ScanCell scanCell);
                OccupantTicket ticket;
                changed = add
                    ? TryAddOccupantToScanCell(world, grid, voxel, id, occupant, scanCell, out ticket, ref diagnostic)
                    : TryRemoveOccupantFromScanCell(world, grid, voxel, id, occupant, scanCell, expectedTicket, out ticket, ref diagnostic);
                change = new OccupantEventInfo(index, occupant, ticket, voxel.OccupantCount);
            }
        }
        if (diagnostic != OccupancyDiagnostic.None)
            LogOccupancyDiagnostic(diagnostic, id, index);
        if (changed)
            NotifyOccupantChanged(world, voxel, change, materialization, add);
        return changed;
    }

    #endregion

    #region Internal Helpers

    internal static bool ForgetTrackedOccupancy(
        GridWorld? world,
        IVoxelOccupant occupant,
        WorldVoxelIndex index,
        OccupantTicket expectedTicket = default)
    {
        if (world == null || occupant == null || !TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
            return false;

        Guid id = occupant.GlobalId;
        lock (registry!.SyncRoot)
            return ForgetTrackedOccupancyUnsafe(id, occupant, index, registry, expectedTicket);
    }

    internal static void ClearTrackedOccupancies(GridWorld world)
    {
        if (!TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
            return;

        lock (registry!.SyncRoot)
            registry.Clear();
    }

    internal static void ReleaseTrackedOccupancies(GridWorld world)
    {
        // Detach before clearing: callers that already captured this registry retain
        // its lock, but subsequent lookups must not discover the released registry.
        WorldOccupancyRegistry? registry = Interlocked.Exchange(ref world.OccupancyRegistry, null);
        if (registry == null)
            return;

        lock (registry.SyncRoot)
            registry.Clear();
    }

    #endregion

    #region Private Methods

    private static bool TryAddOccupantToScanCell(
        GridWorld world,
        VoxelGrid grid,
        Voxel targetVoxel,
        Guid id,
        IVoxelOccupant occupant,
        ScanCell scanCell,
        out OccupantTicket ticket,
        ref OccupancyDiagnostic diagnostic)
    {
        ticket = default;
        if (!targetVoxel.HasVacancy)
            return false;
        ticket = scanCell.AddOccupant(targetVoxel.WorldIndex, occupant);
        if (!TryTrackOccupancy(world, id, occupant, targetVoxel.WorldIndex, ticket, ref diagnostic))
        {
            scanCell.TryRemoveOccupant(targetVoxel.WorldIndex, ticket);
            return false;
        }
        if (scanCell.CellOccupantCount == 1)
        {
            grid.ActiveScanCells ??= SwiftHashSetPool<int>.Shared.Rent();
            grid.ActiveScanCells.Add(targetVoxel.ScanCellKey);
        }
        targetVoxel.OccupantCount++;
        return true;
    }

    private static bool RemoveTrackedOccupancies(
        GridWorld world,
        VoxelGrid grid,
        IVoxelOccupant occupant,
        TrackedOccupancy[] occupancies)
    {
        bool removedAny = false;

        for (int i = 0; i < occupancies.Length; i++)
        {
            WorldVoxelIndex voxelIndex = occupancies[i].VoxelIndex;
            if (!TryGetCurrentTrackedVoxel(grid, voxelIndex, out Voxel? voxel))
            {
                removedAny |= ForgetTrackedOccupancy(world, occupant, voxelIndex, occupancies[i].Ticket);
                continue;
            }

            removedAny |= TryChangeOccupant(world, grid, voxel!, voxelIndex, occupant, add: false, occupancies[i].Ticket);
        }

        return removedAny;
    }

    private static bool TryGetCurrentTrackedVoxel(
        VoxelGrid grid,
        WorldVoxelIndex voxelIndex,
        out Voxel? voxel)
    {
        voxel = null;
        return grid.World != null
            && voxelIndex.WorldSpawnToken == grid.World.SpawnToken
            && voxelIndex.GridSpawnToken == grid.SpawnToken
            && grid.TryGetVoxel(voxelIndex.VoxelIndex, out voxel);
    }

    private static bool TryTrackOccupancy(
        GridWorld world,
        Guid id,
        IVoxelOccupant occupant,
        WorldVoxelIndex index,
        OccupantTicket ticket,
        ref OccupancyDiagnostic diagnostic)
    {
        WorldOccupancyRegistry registry = GetWorldRegistry(world);
        lock (registry.SyncRoot)
        {
            if (!registry.Records.TryGetValue(id, out OccupancyRecord record))
            {
                registry.Records.Add(id, new OccupancyRecord(occupant, index, ticket));
                return true;
            }
            if (!ReferenceEquals(record.Occupant, occupant))
            {
                diagnostic = OccupancyDiagnostic.IdCollision;
                return false;
            }

            if (!record.TryAddTicket(registry, index, ticket))
            {
                diagnostic = OccupancyDiagnostic.Duplicate;
                return false;
            }

            return true;
        }
    }

    private static bool ForgetTrackedOccupancyUnsafe(
        Guid id, IVoxelOccupant occupant, WorldVoxelIndex index, WorldOccupancyRegistry registry, OccupantTicket expectedTicket)
    {
        if (!TryGetTrackedRecordUnsafe(registry, id, occupant, out OccupancyRecord? record)
            || !record!.TryGetTicket(index, out OccupantTicket ticket)
            || (expectedTicket.IsValid && ticket != expectedTicket))
        {
            return false;
        }

        registry.RemoveTicket(id, record, index);

        return true;
    }

    private static bool TryGetTrackedRecordUnsafe(
        WorldOccupancyRegistry registry,
        Guid id,
        IVoxelOccupant occupant,
        out OccupancyRecord? record)
    {
        record = null;
        if (!registry.Records.TryGetValue(id, out record))
            return false;

        return ReferenceEquals(record.Occupant, occupant);
    }

    private static TrackedOccupancy[] GetTrackedOccupanciesSnapshot(
        GridWorld world,
        IVoxelOccupant occupant,
        ushort? gridIndex = null)
    {
        if (world == null || occupant == null || !TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
            return Array.Empty<TrackedOccupancy>();

        Guid id = occupant.GlobalId;
        lock (registry!.SyncRoot)
        {
            if (!TryGetTrackedRecordUnsafe(registry, id, occupant, out OccupancyRecord? record))
                return Array.Empty<TrackedOccupancy>();

            if (record!.MultipleTickets == null)
            {
                if (gridIndex.HasValue && gridIndex.Value != record.FirstIndex.GridIndex)
                    return Array.Empty<TrackedOccupancy>();
                return new[] { new TrackedOccupancy(record.FirstIndex, record.FirstTicket) };
            }

            TrackedOccupancy[] occupancies = new TrackedOccupancy[record.MultipleTickets.Count];
            int count = CopyTrackedOccupancies(record.MultipleTickets, gridIndex, occupancies);
            return FinalizeTrackedOccupancySnapshot(occupancies, count);
        }
    }

    private static bool TryRemoveOccupantFromScanCell(
        GridWorld world, VoxelGrid grid, Voxel voxel, Guid id, IVoxelOccupant occupant, ScanCell scanCell,
        OccupantTicket expectedTicket, out OccupantTicket ticket, ref OccupancyDiagnostic diagnostic)
    {
        ticket = default;
        if (!TryGetWorldRegistry(world, out WorldOccupancyRegistry? registry))
        {
            diagnostic = OccupancyDiagnostic.Missing;
            return false;
        }
        lock (registry!.SyncRoot)
        {
            if (!TryGetTrackedRecordUnsafe(registry, id, occupant, out OccupancyRecord? record)
                || !record!.TryGetTicket(voxel.WorldIndex, out ticket))
            {
                diagnostic = OccupancyDiagnostic.Missing;
                return false;
            }

            if ((expectedTicket.IsValid && ticket != expectedTicket) || !voxel.IsOccupied
                || !scanCell.TryRemoveOccupant(voxel.WorldIndex, ticket))
                return false;

            registry.RemoveTicket(id, record, voxel.WorldIndex);
        }
        ReleaseActiveScanCellIfEmpty(grid, voxel, scanCell, ref diagnostic);
        voxel.OccupantCount--;
        return true;
    }

    private static void ReleaseActiveScanCellIfEmpty(
        VoxelGrid grid, Voxel targetVoxel, ScanCell scanCell, ref OccupancyDiagnostic diagnostic)
    {
        if (scanCell.IsOccupied || grid.ActiveScanCells == null)
            return;

        grid.ActiveScanCells.Remove(targetVoxel.ScanCellKey);
        if (grid.IsOccupied)
            return;

        SwiftHashSetPool<int>.Shared.Release(grid.ActiveScanCells);
        grid.ActiveScanCells = null;
        diagnostic = OccupancyDiagnostic.ReleasedActiveCells;
    }

    private static void LogOccupancyDiagnostic(OccupancyDiagnostic diagnostic, Guid id, WorldVoxelIndex index)
    {
        switch (diagnostic)
        {
            case OccupancyDiagnostic.Duplicate:
                GridForgeLogger.Channel.Warn($"Occupant {id} is already registered to voxel {index}.");
                break;
            case OccupancyDiagnostic.IdCollision:
                GridForgeLogger.Channel.Warn($"Occupant id collision detected for {id} in world {index.WorldSpawnToken}.");
                break;
            case OccupancyDiagnostic.Missing:
                GridForgeLogger.Channel.Warn($"Occupant {id} is not registered to voxel {index}.");
                break;
            default: // The remaining non-None outcome is ReleasedActiveCells.
                GridForgeLogger.Channel.Info($"Releasing unused active scan cells collection.");
                break;
        }
    }

    private static int CopyTrackedOccupancies(
        SwiftDictionary<WorldVoxelIndex, OccupantTicket> tickets,
        ushort? gridIndex,
        TrackedOccupancy[] occupancies)
    {
        int count = 0;

        foreach (KeyValuePair<WorldVoxelIndex, OccupantTicket> pair in tickets)
        {
            if (gridIndex.HasValue && pair.Key.GridIndex != gridIndex.Value)
                continue;

            occupancies[count++] = new TrackedOccupancy(pair.Key, pair.Value);
        }

        return count;
    }

    private static TrackedOccupancy[] FinalizeTrackedOccupancySnapshot(
        TrackedOccupancy[] occupancies,
        int count)
    {
        if (count == 0)
            return Array.Empty<TrackedOccupancy>();

        if (count != occupancies.Length)
            Array.Resize(ref occupancies, count);

        Array.Sort(occupancies, CompareTrackedOccupancies);
        return occupancies;
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

        result = left.Ticket.Slot.CompareTo(right.Ticket.Slot);
        return result != 0
            ? result
            : left.Ticket.Generation.CompareTo(right.Ticket.Generation);
    }

    /// <summary>
    /// Publishes a completed occupant mutation in manager, world-diagnostic and voxel order.
    /// </summary>
    private static void NotifyOccupantChanged(
        GridWorld world,
        Voxel targetVoxel,
        OccupantEventInfo eventInfo,
        ulong materialization,
        bool add)
    {
        Action<OccupantEventInfo>? handlers = add ? _onOccupantAdded : _onOccupantRemoved;
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
                    GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Occupant {(add ? "add" : "remove")} error: {ex.Message}");
                }
            }
        }

        world.NotifyDiagnosticCellChanged(eventInfo.VoxelIndex, GridDiagnosticChangeKind.OccupantChanged);
        if (add)
            targetVoxel.NotifyOccupantAdded(eventInfo, materialization);
        else
            targetVoxel.NotifyOccupantRemoved(eventInfo, materialization);
    }

    private static WorldOccupancyRegistry GetWorldRegistry(GridWorld world)
    {
        WorldOccupancyRegistry? registry = Volatile.Read(ref world.OccupancyRegistry);
        if (registry != null)
            return registry;

        return PublishWorldRegistry(world, new WorldOccupancyRegistry());
    }

    private static WorldOccupancyRegistry PublishWorldRegistry(
        GridWorld world,
        WorldOccupancyRegistry candidate)
    {
        // Return the captured winner, not a reread that can observe concurrent detachment.
        return Interlocked.CompareExchange(ref world.OccupancyRegistry, candidate, null) ?? candidate;
    }

    private static bool TryGetWorldRegistry(GridWorld world, out WorldOccupancyRegistry? registry)
    {
        registry = Volatile.Read(ref world.OccupancyRegistry);
        return registry != null;
    }

    #endregion
}
