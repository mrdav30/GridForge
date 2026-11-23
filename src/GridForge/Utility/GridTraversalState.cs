//=======================================================================
// GridTraversalState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Utility;

/// <summary>
/// Tracks per-grid traversal padding while suppressing duplicate voxel visits.
/// </summary>
public struct GridTraversalState
{
    private readonly GridWorld _world;
    private readonly GridTraversalPaddingMode _paddingMode;
    private ushort _currentGridIndex;
    private long _currentGridSpawnToken;
    private VoxelGrid? _currentGrid;
    private Fixed64 _cellEdge;
    private bool _hasCachedGrid;

    /// <summary>
    /// Initializes traversal state for one world and padding mode.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="paddingMode">Selects the full 3D or planar maximum cell edge used for padding.</param>
    /// <remarks>The world reference is retained without a null check. Use an explicit constructor with a nonnull world; default state has no usable world.</remarks>
    public GridTraversalState(GridWorld world, GridTraversalPaddingMode paddingMode)
    {
        _world = world;
        _paddingMode = paddingMode;
        _currentGridIndex = 0;
        _currentGridSpawnToken = 0;
        _currentGrid = null;
        _cellEdge = Fixed64.Zero;
        _hasCachedGrid = false;
    }

    /// <summary>
    /// Visits a voxel only once and returns the selected cell-edge measurement for its grid.
    /// </summary>
    /// <returns>True when the voxel belongs to an active grid generation and was not already visited; otherwise false.</returns>
    /// <param name="voxel">The physical voxel to inspect. The reference is borrowed and must not cross grid removal or pooled reuse.</param>
    /// <param name="visited">Caller-owned identity set used for duplicate suppression. Existing entries are preserved and new visits are added.</param>
    /// <param name="cellEdge">The selected edge in world units on success; zero on failure.</param>
    /// <remarks>Existing visited entries are preserved. A newly added identity is removed again if its grid generation cannot be resolved.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryVisitUnique(Voxel voxel, SwiftHashSet<WorldVoxelIndex> visited, out Fixed64 cellEdge)
    {
        cellEdge = Fixed64.Zero;
        WorldVoxelIndex voxelIndex = voxel.WorldIndex;
        if (!visited.Add(voxelIndex))
            return false;

        if (!TryResolveGrid(voxelIndex, out VoxelGrid? grid))
        {
            visited.Remove(voxelIndex);
            return false;
        }

        cellEdge = GetCellEdge(grid!);
        return true;
    }

    /// <summary>
    /// Gets the selected cell-edge measurement for a voxel's grid, caching repeated grid lookups.
    /// </summary>
    /// <exception cref="InvalidOperationException">The voxel does not belong to an active grid generation in this traversal's world.</exception>
    /// <param name="voxel">The physical voxel to inspect. The reference is borrowed and must not cross grid removal or pooled reuse.</param>
    /// <returns>The selected topology cell edge in fixed-point world units.</returns>
    /// <remarks>The world reference must come from the explicit constructor. The throwing form rejects stale grid generations; use TryVisitUnique when rejection should be reported as false.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 GetCellEdge(Voxel voxel)
    {
        bool isCurrent = TryResolveGrid(voxel.WorldIndex, out VoxelGrid? grid);
        ChronicleGuard.ThrowIfTrue(
            !isCurrent,
            nameof(voxel),
            "The voxel does not belong to an active grid generation in this traversal's world.");

        return GetCellEdge(grid!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 GetCellEdge(VoxelGrid grid)
    {
        if (_hasCachedGrid
            && grid.GridIndex == _currentGridIndex
            && grid.SpawnToken == _currentGridSpawnToken)
        {
            return _cellEdge;
        }

        _currentGridIndex = grid.GridIndex;
        _currentGridSpawnToken = grid.SpawnToken;
        _currentGrid = grid;
        _hasCachedGrid = true;
        _cellEdge = _paddingMode == GridTraversalPaddingMode.PlanarMaxCellEdge
            ? GridTopologyMetricUtility.GetPlanarMaxCellEdge(grid)
            : GridTopologyMetricUtility.GetMaxCellEdge(grid);
        return _cellEdge;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryResolveGrid(WorldVoxelIndex voxelIndex, out VoxelGrid? grid)
    {
        grid = _currentGrid;
        if (_hasCachedGrid
            && voxelIndex.WorldSpawnToken == _world.SpawnToken
            && voxelIndex.GridIndex == _currentGridIndex
            && voxelIndex.GridSpawnToken == _currentGridSpawnToken
            && grid != null
            && grid.IsActive
            && ReferenceEquals(grid.World, _world)
            && grid.GridIndex == _currentGridIndex
            && grid.SpawnToken == _currentGridSpawnToken)
        {
            return true;
        }

        return _world.TryGetGrid(voxelIndex, out grid);
    }
}
