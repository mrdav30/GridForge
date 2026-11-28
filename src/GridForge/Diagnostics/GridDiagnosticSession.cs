//=======================================================================
// GridDiagnosticSession.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Utility;

namespace GridForge.Diagnostics;

/// <summary>
/// Captures dirty diagnostic grid, cell, and sparse address changes for one
/// active <see cref="GridWorld"/> through notifications owned by that world.
/// </summary>
/// <remarks>Exact obstacle and occupant dirt is recorded after the global manager callbacks
/// and before voxel-local callbacks. Obstacle changes also produce a later broad grid
/// notification while the grid generation remains live. Ordinary reset preserves the
/// subscription. Deactivation releases the world's subscriptions.</remarks>
public sealed class GridDiagnosticSession : IDisposable
{
    private readonly GridWorld _world;
    private readonly long _worldSpawnToken;
    private readonly SwiftList<GridDiagnosticChange> _changes = new();
    private readonly SwiftDictionary<GridDiagnosticChange, int> _changeIndexes =
        new(SwiftDictionary<GridDiagnosticChange, int>.DefaultCapacity, DirtyLocationComparer.Instance);
    private readonly object _syncRoot = new();
    private bool _worldResetPending;
    private bool _disposed;

    /// <summary>
    /// Creates a diagnostic dirty-tracking session for the supplied active
    /// world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <exception cref="System.ArgumentNullException">The world is null.</exception>
    /// <exception cref="System.InvalidOperationException">The world is inactive.</exception>
    public GridDiagnosticSession(GridWorld world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        lock (world.ChangeSyncRoot)
        {
            if (!world.IsActive)
                throw new InvalidOperationException("Diagnostic sessions require an active GridWorld.");

            _world = world;
            _worldSpawnToken = world.SpawnToken;
            Subscribe();
        }
    }

    /// <summary>
    /// Clears and fills caller-owned storage with coalesced dirty changes.
    /// </summary>
    /// <param name="results">Caller-owned output storage. The call clears existing elements before filling it; the collection is not retained.</param>
    /// <returns>The number of sorted, coalesced changes copied. Reading does not drain them; call ClearDirtyChanges to acknowledge them.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/> is null.</exception>
    /// <remarks>Copies values into caller-owned storage without clearing the pending session changes. The copied values survive subsequent session clearing; resolving their identities can fail after world mutation.</remarks>
    public int GetDirtyChangesInto(SwiftList<GridDiagnosticChange> results)
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));

        results.Clear();
        lock (_syncRoot)
        {
            for (int i = 0; i < _changes.Count; i++)
                results.Add(_changes[i]);
        }

        if (results.Count > 1)
            results.SortInPlace();

        return results.Count;
    }

    /// <summary>
    /// Clears all dirty changes captured so far.
    /// </summary>
    public void ClearDirtyChanges()
    {
        lock (_syncRoot)
        {
            _changes.Clear();
            _changeIndexes.Clear();
            _worldResetPending = false;
        }
    }

    /// <summary>Unsubscribes from world notifications and clears captured dirty changes.</summary>
    /// <remarks>Repeated disposal is harmless. Other operations must not race with disposal.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        Unsubscribe();
        ClearDirtyChanges();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void Subscribe()
    {
        _world.OnActiveGridAdded += HandleActiveGridAdded;
        _world.OnActiveGridRemoved += HandleActiveGridRemoved;
        _world.OnActiveGridChange += HandleActiveGridChanged;
        _world.OnReset += HandleWorldReset;
        _world.DiagnosticCellChanged += HandleCellChanged;
    }

    private void Unsubscribe()
    {
        _world.OnActiveGridAdded -= HandleActiveGridAdded;
        _world.OnActiveGridRemoved -= HandleActiveGridRemoved;
        _world.OnActiveGridChange -= HandleActiveGridChanged;
        _world.OnReset -= HandleWorldReset;
        _world.DiagnosticCellChanged -= HandleCellChanged;
    }

    private void HandleActiveGridAdded(GridEventInfo eventInfo)
    {
        lock (_world.ChangeSyncRoot)
        {
            if (!CanRecordGrid(eventInfo.WorldSpawnToken, eventInfo.GridIndex, eventInfo.GridSpawnToken, out _))
                return;
            lock (_syncRoot)
                RecordGridChange(eventInfo, GridDiagnosticChangeKind.GridAdded);
        }
    }

    private void HandleActiveGridRemoved(GridEventInfo eventInfo)
    {
        if (!CanRecord(eventInfo.WorldSpawnToken))
            return;

        lock (_syncRoot)
            RecordGridChange(eventInfo, GridDiagnosticChangeKind.GridRemoved);
    }

    private void HandleActiveGridChanged(GridEventInfo eventInfo)
    {
        lock (_world.ChangeSyncRoot)
        {
            if (!CanRecordGrid(eventInfo.WorldSpawnToken, eventInfo.GridIndex, eventInfo.GridSpawnToken, out VoxelGrid? grid))
                return;
            lock (_syncRoot)
            {
                if (_worldResetPending)
                    return;
                switch (eventInfo.ChangeKind)
                {
                    case GridEventKind.SparseVoxelAdded:
                        RecordSparseVoxelChange(eventInfo, grid!, GridDiagnosticChangeKind.SparseVoxelAdded);
                        break;
                    case GridEventKind.SparseVoxelRemoved:
                        RecordSparseVoxelChange(eventInfo, grid!, GridDiagnosticChangeKind.SparseVoxelRemoved);
                        break;
                    default:
                        RecordGridChange(eventInfo, GridDiagnosticChangeKind.GridChanged);
                        break;
                }
            }
        }
    }

    private void HandleWorldReset()
    {
        if (!CanRecord(_worldSpawnToken))
            return;

        GridDiagnosticChange change = new(
            GridDiagnosticChangeKind.WorldReset,
            _worldSpawnToken,
            ushort.MaxValue,
            0,
            default,
            default,
            default,
            default);

        lock (_syncRoot)
        {
            _changes.Clear();
            _changeIndexes.Clear();
            _worldResetPending = true;
            _changeIndexes.Add(change, 0);
            _changes.Add(change);
        }
    }

    private void HandleCellChanged(WorldVoxelIndex index, GridDiagnosticChangeKind kind)
    {
        lock (_world.ChangeSyncRoot)
        {
            if (!CanRecordGrid(index.WorldSpawnToken, index.GridIndex, index.GridSpawnToken, out _))
                return;
            lock (_syncRoot)
            {
                if (!_worldResetPending)
                    RecordCellChange(index, kind);
            }
        }
    }

    private bool CanRecord(long worldSpawnToken) =>
        !_disposed
        && worldSpawnToken == _worldSpawnToken;

    private bool CanRecordGrid(long worldToken, ushort gridIndex, long gridToken, out VoxelGrid? grid)
    {
        grid = null;
        return CanRecord(worldToken)
            && _world.ActiveGrids.TryGetValue(gridIndex, out grid)
            && grid.SpawnToken == gridToken;
    }

    private void RecordGridChange(
        GridEventInfo eventInfo,
        GridDiagnosticChangeKind kind)
    {
        RecordChange(new GridDiagnosticChange(
            kind,
            eventInfo.WorldSpawnToken,
            eventInfo.GridIndex,
            eventInfo.GridSpawnToken,
            default,
            default,
            eventInfo.BoundsMin,
            eventInfo.BoundsMax));
    }

    private void RecordSparseVoxelChange(
        GridEventInfo eventInfo,
        VoxelGrid grid,
        GridDiagnosticChangeKind kind)
    {
        WorldVoxelIndex worldIndex = new(
            eventInfo.WorldSpawnToken,
            eventInfo.GridIndex,
            eventInfo.GridSpawnToken,
            eventInfo.VoxelIndex);

        RecordChange(new GridDiagnosticChange(
            kind,
            eventInfo.WorldSpawnToken,
            eventInfo.GridIndex,
            eventInfo.GridSpawnToken,
            worldIndex,
            eventInfo.VoxelIndex,
            eventInfo.AffectedBoundsMin,
            eventInfo.AffectedBoundsMax));

        RecordSparseAddressRangeChange(eventInfo, grid);
    }

    private void RecordSparseAddressRangeChange(GridEventInfo eventInfo, VoxelGrid grid)
    {
        TopologyVoxelAabb bounds = TopologyVoxelAabb.FromIndex(grid, eventInfo.VoxelIndex);

        RecordChange(new GridDiagnosticChange(
            GridDiagnosticChangeKind.SparseAddressChanged,
            eventInfo.WorldSpawnToken,
            eventInfo.GridIndex,
            eventInfo.GridSpawnToken,
            default,
            eventInfo.VoxelIndex,
            bounds.Min,
            bounds.Max));
    }

    private void RecordCellChange(
        WorldVoxelIndex worldIndex,
        GridDiagnosticChangeKind kind)
    {
        RecordChange(new GridDiagnosticChange(
            kind,
            worldIndex.WorldSpawnToken,
            worldIndex.GridIndex,
            worldIndex.GridSpawnToken,
            worldIndex,
            worldIndex.VoxelIndex,
            default,
            default));
    }

    private void RecordChange(GridDiagnosticChange change)
    {
        if (_changeIndexes.TryGetValue(change, out int index))
        {
            GridDiagnosticChange existing = _changes[index];
            GridDiagnosticChangeKind combined = existing.Kind | change.Kind;
            if (combined != existing.Kind)
                _changes[index] = existing.WithKind(combined);
            return;
        }

        _changeIndexes.Add(change, _changes.Count);
        _changes.Add(change);
    }

    private sealed class DirtyLocationComparer : IEqualityComparer<GridDiagnosticChange>
    {
        public static readonly DirtyLocationComparer Instance = new();

        public bool Equals(GridDiagnosticChange left, GridDiagnosticChange right) =>
            left.EqualsLocation(right) && left.ScopeOrder == right.ScopeOrder;

        // Each session observes one world. Scope, grid generation and cell distribute
        // entries without hashing both bounds on every notification.
        public int GetHashCode(GridDiagnosticChange change) =>
            SwiftHashTools.CombineHashCodes(change.ScopeOrder, change.GridSpawnToken.GetHashCode(), change.VoxelIndex.GetHashCode());
    }
}
