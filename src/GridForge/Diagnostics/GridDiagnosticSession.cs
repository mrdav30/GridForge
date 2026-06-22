//=======================================================================
// GridDiagnosticSession.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Utility;
using System;

namespace GridForge.Diagnostics;

/// <summary>
/// Captures dirty diagnostic grid, cell, and sparse address changes for one
/// active <see cref="GridWorld"/>.
/// </summary>
public sealed class GridDiagnosticSession : IDisposable
{
    private readonly GridWorld _world;
    private readonly int _worldSpawnToken;
    private readonly SwiftList<GridDiagnosticChange> _changes = new();
    private readonly SwiftDictionary<GridDiagnosticChangeKey, int> _changeIndexes = new();
    private readonly object _syncRoot = new();
    private bool _worldResetPending;
    private bool _disposed;

    /// <summary>
    /// Creates a diagnostic dirty-tracking session for the supplied active
    /// world.
    /// </summary>
    public GridDiagnosticSession(GridWorld world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        if (!world.IsActive)
            throw new InvalidOperationException("Diagnostic sessions require an active GridWorld.");

        _world = world;
        _worldSpawnToken = world.SpawnToken;
        Subscribe();
    }

    /// <summary>
    /// Clears and fills caller-owned storage with coalesced dirty changes.
    /// </summary>
    public int GetDirtyChangesInto(SwiftList<GridDiagnosticChange> results)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));

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

    /// <inheritdoc/>
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
        GridObstacleManager.OnObstacleAdded += HandleObstacleChanged;
        GridObstacleManager.OnObstacleRemoved += HandleObstacleChanged;
        GridObstacleManager.OnObstaclesCleared += HandleObstaclesCleared;
        GridOccupantManager.OnOccupantAdded += HandleOccupantChanged;
        GridOccupantManager.OnOccupantRemoved += HandleOccupantChanged;
    }

    private void Unsubscribe()
    {
        _world.OnActiveGridAdded -= HandleActiveGridAdded;
        _world.OnActiveGridRemoved -= HandleActiveGridRemoved;
        _world.OnActiveGridChange -= HandleActiveGridChanged;
        _world.OnReset -= HandleWorldReset;
        GridObstacleManager.OnObstacleAdded -= HandleObstacleChanged;
        GridObstacleManager.OnObstacleRemoved -= HandleObstacleChanged;
        GridObstacleManager.OnObstaclesCleared -= HandleObstaclesCleared;
        GridOccupantManager.OnOccupantAdded -= HandleOccupantChanged;
        GridOccupantManager.OnOccupantRemoved -= HandleOccupantChanged;
    }

    private void HandleActiveGridAdded(GridEventInfo eventInfo)
    {
        if (!CanRecord(eventInfo.WorldSpawnToken))
            return;

        RecordGridChange(eventInfo, GridDiagnosticChangeKind.GridAdded);
    }

    private void HandleActiveGridRemoved(GridEventInfo eventInfo)
    {
        if (!CanRecord(eventInfo.WorldSpawnToken))
            return;

        RecordGridChange(eventInfo, GridDiagnosticChangeKind.GridRemoved);
    }

    private void HandleActiveGridChanged(GridEventInfo eventInfo)
    {
        if (!CanRecord(eventInfo.WorldSpawnToken)
            || HasPendingWorldReset())
        {
            return;
        }

        switch (eventInfo.ChangeKind)
        {
            case GridEventKind.SparseVoxelAdded:
                RecordSparseVoxelChange(eventInfo, GridDiagnosticChangeKind.SparseVoxelAdded);
                break;
            case GridEventKind.SparseVoxelRemoved:
                RecordSparseVoxelChange(eventInfo, GridDiagnosticChangeKind.SparseVoxelRemoved);
                break;
            default:
                RecordGridChange(eventInfo, GridDiagnosticChangeKind.GridChanged);
                break;
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
        GridDiagnosticChangeKey key = GridDiagnosticChangeKey.FromChange(change);

        lock (_syncRoot)
        {
            _changes.Clear();
            _changeIndexes.Clear();
            _worldResetPending = true;
            _changeIndexes.Add(key, 0);
            _changes.Add(change);
        }
    }

    private void HandleObstacleChanged(ObstacleEventInfo eventInfo)
    {
        if (!CanRecordCell(eventInfo.VoxelIndex))
            return;

        RecordCellChange(eventInfo.VoxelIndex, GridDiagnosticChangeKind.ObstacleChanged);
    }

    private void HandleObstaclesCleared(ObstacleClearEventInfo eventInfo)
    {
        if (!CanRecordCell(eventInfo.VoxelIndex))
            return;

        RecordCellChange(eventInfo.VoxelIndex, GridDiagnosticChangeKind.ObstacleChanged);
    }

    private void HandleOccupantChanged(OccupantEventInfo eventInfo)
    {
        if (!CanRecordCell(eventInfo.VoxelIndex))
            return;

        RecordCellChange(eventInfo.VoxelIndex, GridDiagnosticChangeKind.OccupantChanged);
    }

    private bool CanRecord(int worldSpawnToken) =>
        !_disposed
        && worldSpawnToken == _worldSpawnToken;

    private bool CanRecordCell(WorldVoxelIndex worldIndex) =>
        CanRecord(worldIndex.WorldSpawnToken)
        && !HasPendingWorldReset()
        && _world.TryGetGrid(worldIndex, out _);

    private bool HasPendingWorldReset()
    {
        lock (_syncRoot)
            return _worldResetPending;
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

        RecordSparseAddressRangeChange(eventInfo);
    }

    private void RecordSparseAddressRangeChange(GridEventInfo eventInfo)
    {
        Vector3d boundsMin = eventInfo.AffectedBoundsMin;
        Vector3d boundsMax = eventInfo.AffectedBoundsMax;
        if (_world.TryGetGrid(eventInfo.GridIndex, out VoxelGrid? grid))
        {
            TopologyVoxelAabb bounds = TopologyVoxelAabb.FromIndex(grid!, eventInfo.VoxelIndex);
            boundsMin = bounds.Min;
            boundsMax = bounds.Max;
        }

        RecordChange(new GridDiagnosticChange(
            GridDiagnosticChangeKind.SparseAddressChanged,
            eventInfo.WorldSpawnToken,
            eventInfo.GridIndex,
            eventInfo.GridSpawnToken,
            default,
            eventInfo.VoxelIndex,
            boundsMin,
            boundsMax));
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
        GridDiagnosticChangeKey key = GridDiagnosticChangeKey.FromChange(change);
        lock (_syncRoot)
        {
            if (_changeIndexes.TryGetValue(key, out int index))
            {
                GridDiagnosticChange existing = _changes[index];
                _changes[index] = existing.WithKind(existing.Kind | change.Kind);
                return;
            }

            _changeIndexes.Add(key, _changes.Count);
            _changes.Add(change);
        }
    }

    private readonly struct GridDiagnosticChangeKey : IEquatable<GridDiagnosticChangeKey>
    {
        private readonly GridDiagnosticChangeScope _scope;
        private readonly int _worldSpawnToken;
        private readonly ushort _gridIndex;
        private readonly int _gridSpawnToken;
        private readonly WorldVoxelIndex _worldIndex;
        private readonly VoxelIndex _voxelIndex;
        private readonly Vector3d _boundsMin;
        private readonly Vector3d _boundsMax;

        private GridDiagnosticChangeKey(
            GridDiagnosticChangeScope scope,
            GridDiagnosticChange change)
        {
            _scope = scope;
            _worldSpawnToken = change.WorldSpawnToken;
            _gridIndex = change.GridIndex;
            _gridSpawnToken = change.GridSpawnToken;
            _worldIndex = change.WorldIndex;
            _voxelIndex = change.VoxelIndex;
            _boundsMin = change.BoundsMin;
            _boundsMax = change.BoundsMax;
        }

        public static GridDiagnosticChangeKey FromChange(GridDiagnosticChange change)
        {
            GridDiagnosticChangeScope scope = GetScope(change);
            return new GridDiagnosticChangeKey(scope, change);
        }

        public bool Equals(GridDiagnosticChangeKey other) =>
            _scope == other._scope
            && _worldSpawnToken == other._worldSpawnToken
            && _gridIndex == other._gridIndex
            && _gridSpawnToken == other._gridSpawnToken
            && _worldIndex.Equals(other._worldIndex)
            && _voxelIndex.Equals(other._voxelIndex)
            && _boundsMin.Equals(other._boundsMin)
            && _boundsMax.Equals(other._boundsMax);

        public override bool Equals(object? obj) => obj is GridDiagnosticChangeKey other && Equals(other);

        public override int GetHashCode()
        {
            int hash = SwiftHashTools.CombineHashCodes((int)_scope, _worldSpawnToken);
            hash = SwiftHashTools.CombineHashCodes(hash, _gridIndex);
            hash = SwiftHashTools.CombineHashCodes(hash, _gridSpawnToken);
            hash = SwiftHashTools.CombineHashCodes(hash, _worldIndex.GetHashCode());
            hash = SwiftHashTools.CombineHashCodes(hash, _voxelIndex.GetHashCode());
            hash = SwiftHashTools.CombineHashCodes(hash, _boundsMin.GetHashCode());
            return SwiftHashTools.CombineHashCodes(hash, _boundsMax.GetHashCode());
        }

        private static GridDiagnosticChangeScope GetScope(GridDiagnosticChange change)
        {
            if ((change.Kind & GridDiagnosticChangeKind.WorldReset) != 0)
                return GridDiagnosticChangeScope.World;

            if (change.WorldIndex.WorldSpawnToken != 0 || change.WorldIndex.VoxelIndex.IsAllocated)
                return GridDiagnosticChangeScope.Cell;

            return (change.Kind & GridDiagnosticChangeKind.SparseAddressChanged) != 0
                ? GridDiagnosticChangeScope.Range
                : GridDiagnosticChangeScope.Grid;
        }
    }

    private enum GridDiagnosticChangeScope
    {
        World = 0,
        Grid = 1,
        Cell = 2,
        Range = 3
    }
}
