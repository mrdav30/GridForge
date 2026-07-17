//=======================================================================
// ScanCell.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Utility;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GridForge.Grids;

/// <summary>
/// Stores one occupant registration and the generation that owns its bucket slot.
/// </summary>
internal readonly struct OccupantEntry
{
    public readonly IVoxelOccupant Occupant;
    public readonly long Generation;

    public OccupantEntry(IVoxelOccupant occupant, long generation)
    {
        Occupant = occupant;
        Generation = generation;
    }
}

/// <summary>
/// Represents a spatial partition within a grid, managing occupants at a finer granularity than grid voxels.
/// Handles efficient tracking, retrieval, and removal of occupants within a designated scan cell area.
/// </summary>
public class ScanCell
{
    #region Properties

    /// <summary>
    /// The world-local index of the grid this scan cell belongs to.
    /// </summary>
    public ushort GridIndex { get; private set; }

    /// <summary>
    /// The world that owns this scan cell through its parent grid.
    /// </summary>
    public GridWorld? World { get; private set; }

    /// <summary>
    /// A unique identifier for this scan cell in the grid.
    /// </summary>
    public int CellKey { get; private set; }

    /// <summary>
    /// Maps a <see cref="Voxel.WorldIndex"/> to a bucket of associated <see cref="IVoxelOccupant"/> instances.
    /// </summary>
    private SwiftDictionary<WorldVoxelIndex, SwiftBucket<OccupantEntry>>? _voxelOccupants;

    private object? _occupantSyncRoot;

    private static long s_occupantGenerationCounter;

    /// <summary>
    /// The total number of occupants in this scan cell.
    /// </summary>
    public int CellOccupantCount { get; private set; }

    /// <summary>
    /// Indicates whether this scan cell is currently allocated in the grid.
    /// </summary>
    public bool IsAllocated { get; private set; }

    /// <summary>
    /// Determines whether this scan cell is occupied by any occupants.
    /// A scan cell is only considered occupied if it is allocated and contains at least one occupant.
    /// </summary>
    public bool IsOccupied => IsAllocated && CellOccupantCount > 0;

    #endregion

    #region Initialization & Reset

    /// <summary>
    /// Initializes the scan cell with its owning grid and unique cell key.
    /// </summary>
    internal void Initialize(VoxelGrid grid, int cellKey)
    {
        World = grid.World;
        GridIndex = grid.GridIndex;
        CellKey = cellKey;
        IsAllocated = true;
        Volatile.Write(ref _occupantSyncRoot, grid.OccupantSyncRoot);
    }

    /// <summary>
    /// Resets the scan cell, clearing all occupants and returning memory to object pools.
    /// This effectively marks the scan cell as deallocated and removes all references.
    /// </summary>
    internal void Reset()
    {
        object? syncRoot = Volatile.Read(ref _occupantSyncRoot);
        if (syncRoot == null)
            return;

        lock (syncRoot)
        {
            if (!ReferenceEquals(syncRoot, Volatile.Read(ref _occupantSyncRoot)) || !IsAllocated)
                return;

            if (_voxelOccupants != null)
            {
                foreach (var kvp in _voxelOccupants)
                {
                    SwiftBucket<OccupantEntry> bucket = kvp.Value;
                    foreach (OccupantEntry entry in bucket)
                        GridOccupantManager.ForgetTrackedOccupancy(World, entry.Occupant, kvp.Key);

                    Pools.VoxelOccupantBucketPool.Release(bucket);
                }

                Pools.VoxelOccupantDictionaryPool.Release(_voxelOccupants);
                _voxelOccupants = null;
            }

            CellOccupantCount = 0;

            World = null;
            GridIndex = ushort.MaxValue;
            CellKey = byte.MaxValue;

            IsAllocated = false;
            Volatile.Write(ref _occupantSyncRoot, null);
        }
    }

    #endregion

    #region Occupant Management

    /// <summary>
    /// Adds an occupant to this scan cell and tracks its presence.
    /// </summary>
    /// <param name="index">The global index of the voxel where the occupant resides.</param>
    /// <param name="occupant">The occupant instance to add.</param>
    /// <returns>A generation-aware ticket for the occupant's bucket slot.</returns>
    internal OccupantTicket AddOccupant(WorldVoxelIndex index, IVoxelOccupant occupant)
    {
        long generation = RuntimeIdentityAllocator.Allocate(ref s_occupantGenerationCounter);
        _voxelOccupants ??= Pools.VoxelOccupantDictionaryPool.Rent();
        if (!_voxelOccupants.TryGetValue(index, out SwiftBucket<OccupantEntry> bucket))
        {
            bucket = Pools.VoxelOccupantBucketPool.Rent();
            _voxelOccupants[index] = bucket;
        }

        int slot = bucket.Add(new OccupantEntry(occupant, generation));
        CellOccupantCount++;
        return new OccupantTicket(slot, generation);
    }

    /// <summary>
    /// Removes an occupant from this scan cell.
    /// </summary>
    /// <param name="index">The global index of the voxel the occupant was assigned to.</param>
    /// <param name="ticket">The ticket assigned to the occupant instance from this scancell.</param>
    /// <returns>True if the occupant was successfully removed; otherwise, false.</returns>
    internal bool TryRemoveOccupant(
        WorldVoxelIndex index,
        OccupantTicket ticket)
    {
        if (!IsOccupied)
            return false;

        if (!_voxelOccupants!.TryGetValue(index, out var bucket))
            return false;

        if (!ticket.IsValid
            || !bucket.TryGetValue(ticket.Slot, out OccupantEntry entry)
            || entry.Generation != ticket.Generation
            || !bucket.TryRemoveAt(ticket.Slot))
        {
            return false;
        }

        // If the occupant was the last in its bucket, remove the entire bucket
        if (bucket.Count == 0)
        {
            _voxelOccupants.Remove(index);
            Pools.VoxelOccupantBucketPool.Release(bucket);
        }

        CellOccupantCount--;

        return true;
    }

    #endregion

    #region Occupant Retrieval

    /// <summary>
    /// Retrieves all occupants associated with this ScanCell.
    /// </summary>
    /// <returns>An enumerable of occupants within this scan cell.</returns>
    public IEnumerable<IVoxelOccupant> GetOccupants()
    {
        if (_voxelOccupants == null)
            yield break;

        foreach (SwiftBucket<OccupantEntry> bucket in _voxelOccupants.Values)
        {
            foreach (OccupantEntry entry in bucket)
                yield return entry.Occupant;
        }
    }

    /// <summary>
    /// Retrieves occupants whose group Ids match a given condition.
    /// </summary>
    public IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupConditional = null)
    {
        if (_voxelOccupants == null)
            yield break;

        // Loop through each voxel's bucket and filter by the cluster condition
        foreach (var bucket in _voxelOccupants.Values)
        {
            foreach (OccupantEntry entry in bucket)
            {
                IVoxelOccupant occupant = entry.Occupant;
                if (occupantCondition != null && !occupantCondition(occupant))
                    continue;

                if (groupConditional != null && !groupConditional(occupant.OccupantGroupId))
                    continue;

                yield return occupant;
            }
        }
    }

    /// <summary>
    /// Appends occupants within the squared radius to caller-owned storage without allocating an iterator.
    /// </summary>
    internal void AddOccupantsWithinRadiusTo(
        SwiftList<IVoxelOccupant> results,
        Vector3d position,
        Fixed64 squaredRadius,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        if (_voxelOccupants == null)
            return;

        foreach (var kvp in _voxelOccupants)
        {
            SwiftBucket<OccupantEntry> bucket = kvp.Value;
            foreach (OccupantEntry entry in bucket)
            {
                IVoxelOccupant occupant = entry.Occupant;
                if (OccupantPassesFilters(occupant, occupantCondition, groupCondition)
                    && IsWithinSquaredRadius(occupant, position, squaredRadius))
                {
                    results.Add(occupant);
                }
            }
        }
    }

    /// <summary>
    /// Appends occupants within the XZ squared radius on the selected local Y voxel layer.
    /// </summary>
    internal void AddOccupantsWithinRadius2dTo(
        SwiftList<IVoxelOccupant> results,
        Vector3d position,
        int localLayerY,
        Fixed64 squaredRadius,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        if (_voxelOccupants == null)
            return;

        foreach (var kvp in _voxelOccupants)
        {
            if (kvp.Key.VoxelIndex.y != localLayerY)
                continue;

            SwiftBucket<OccupantEntry> bucket = kvp.Value;
            foreach (OccupantEntry entry in bucket)
            {
                IVoxelOccupant occupant = entry.Occupant;
                if (OccupantPassesFilters(occupant, occupantCondition, groupCondition)
                    && GridPlane2d.DistanceSquaredXZ(occupant.Position, position) <= squaredRadius)
                {
                    results.Add(occupant);
                }
            }
        }
    }

    /// <summary>
    /// Appends typed occupants within the squared radius to caller-owned storage without LINQ.
    /// </summary>
    internal void AddOccupantsWithinRadiusTo<T>(
        SwiftList<T> results,
        Vector3d position,
        Fixed64 squaredRadius,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        if (_voxelOccupants == null)
            return;

        foreach (var kvp in _voxelOccupants)
        {
            SwiftBucket<OccupantEntry> bucket = kvp.Value;
            foreach (OccupantEntry entry in bucket)
            {
                IVoxelOccupant occupant = entry.Occupant;
                if (!OccupantPassesFilters(occupant, occupantCondition, groupCondition))
                    continue;

                if (TryGetTypedOccupantWithinRadius(occupant, position, squaredRadius, out T typedOccupant))
                    results.Add(typedOccupant);
            }
        }
    }

    /// <summary>
    /// Appends typed occupants within the XZ squared radius on the selected local Y voxel layer.
    /// </summary>
    internal void AddOccupantsWithinRadius2dTo<T>(
        SwiftList<T> results,
        Vector3d position,
        int localLayerY,
        Fixed64 squaredRadius,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        if (_voxelOccupants == null)
            return;

        foreach (var kvp in _voxelOccupants)
        {
            if (kvp.Key.VoxelIndex.y != localLayerY)
                continue;

            SwiftBucket<OccupantEntry> bucket = kvp.Value;
            foreach (OccupantEntry entry in bucket)
            {
                IVoxelOccupant occupant = entry.Occupant;
                if (!OccupantPassesFilters(occupant, occupantCondition, groupCondition))
                    continue;

                if (occupant is T typedOccupant
                    && GridPlane2d.DistanceSquaredXZ(occupant.Position, position) <= squaredRadius)
                {
                    results.Add(typedOccupant);
                }
            }
        }
    }

    private static bool OccupantPassesFilters(
        IVoxelOccupant occupant,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition)
    {
        return (occupantCondition == null || occupantCondition(occupant))
            && (groupCondition == null || groupCondition(occupant.OccupantGroupId));
    }

    private static bool IsWithinSquaredRadius(
        IVoxelOccupant occupant,
        Vector3d position,
        Fixed64 squaredRadius)
    {
        return (occupant.Position - position).MagnitudeSquared <= squaredRadius;
    }

    private static bool TryGetTypedOccupantWithinRadius<T>(
        IVoxelOccupant occupant,
        Vector3d position,
        Fixed64 squaredRadius,
        out T typedOccupant) where T : IVoxelOccupant
    {
        typedOccupant = default!;
        if (occupant is not T candidate || !IsWithinSquaredRadius(occupant, position, squaredRadius))
            return false;

        typedOccupant = candidate;
        return true;
    }

    /// <summary>
    /// Retrieves all occupants associated with a specific voxel spawn token within this scan cell.
    /// </summary>
    /// <param name="index">The global index of the voxel.</param>
    /// <returns>An enumerable collection of occupants assigned to the voxel.</returns>
    public IEnumerable<IVoxelOccupant> GetOccupantsFor(WorldVoxelIndex index)
    {
        if (_voxelOccupants == null || !_voxelOccupants.TryGetValue(index, out SwiftBucket<OccupantEntry> voxelOccupants))
            yield break;

        foreach (OccupantEntry entry in voxelOccupants)
            yield return entry.Occupant;
    }

    /// <summary>
    /// Attempts to retrieve a specific occupant in this scan cell using a voxel's spawn key and occupant ticket.
    /// </summary>
    /// <param name="index">The global index of the voxel the occupant belongs to.</param>
    /// <param name="occupantTicket">The unique ticket identifying the occupant.</param>
    /// <param name="voxelOccupant">The retrieved occupant if found.</param>
    /// <returns>True if the occupant was found, otherwise false.</returns>
    public bool TryGetOccupantAt(
        WorldVoxelIndex index,
        OccupantTicket occupantTicket,
        out IVoxelOccupant? voxelOccupant)
    {
        voxelOccupant = null;
        object? syncRoot = Volatile.Read(ref _occupantSyncRoot);
        if (syncRoot == null)
            return false;

        lock (syncRoot)
        {
            if (!ReferenceEquals(syncRoot, Volatile.Read(ref _occupantSyncRoot))
                || _voxelOccupants == null
                || !_voxelOccupants.TryGetValue(index, out SwiftBucket<OccupantEntry> voxelOccupants)
                || !occupantTicket.IsValid
                || !voxelOccupants.TryGetValue(occupantTicket.Slot, out OccupantEntry entry)
                || entry.Generation != occupantTicket.Generation)
            {
                return false;
            }

            voxelOccupant = entry.Occupant;
            return true;
        }
    }

    #endregion

    /// <inheritdoc/>
    public override int GetHashCode() => SwiftHashTools.CombineHashCodes(GridIndex, CellKey);
}
