//=======================================================================
// GridTraversal.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace GridForge.Utility;

/// <summary>
/// Selects which topology edge measurement a grid traversal should use for padding.
/// </summary>
public enum GridTraversalPaddingMode
{
    /// <summary>
    /// Use the largest three-dimensional cell edge.
    /// </summary>
    MaxCellEdge,

    /// <summary>
    /// Use the largest X/Z-plane cell edge.
    /// </summary>
    PlanarMaxCellEdge
}

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 GetCellEdge(Voxel voxel)
    {
        bool isCurrent = TryResolveGrid(voxel.WorldIndex, out VoxelGrid? grid);
        SwiftThrowHelper.ThrowIfTrue(
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

/// <summary>
/// Provides deterministic helpers for duplicate-safe voxel traversal.
/// </summary>
public static class GridTraversal
{
    /// <summary>
    /// Gets a voxel partition once per exact world-scoped voxel identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetUniquePartition<TPartition>(
        Voxel voxel,
        SwiftHashSet<WorldVoxelIndex> visited,
        out TPartition? partition)
        where TPartition : class, IVoxelPartition
    {
        partition = null;
        return visited.Add(voxel.WorldIndex)
            && voxel.TryGetPartition(out partition);
    }

    /// <summary>
    /// Tests whether a 3D world position lies inside bounds expanded by half of a cell edge.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWorldPositionInPaddedBounds(
        Vector3d min,
        Vector3d max,
        Fixed64 cellEdge,
        Vector3d worldPosition)
    {
        Fixed64 padding = cellEdge * Fixed64.Half;
        return worldPosition.X >= min.X - padding
            && worldPosition.X <= max.X + padding
            && worldPosition.Y >= min.Y - padding
            && worldPosition.Y <= max.Y + padding
            && worldPosition.Z >= min.Z - padding
            && worldPosition.Z <= max.Z + padding;
    }

    /// <summary>
    /// Tests whether a world position's X/Z projection lies inside bounds expanded by half of a cell edge.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPlanarPositionInPaddedBounds(
        Vector2d min,
        Vector2d max,
        Fixed64 cellEdge,
        Vector3d worldPosition)
    {
        Fixed64 padding = cellEdge * Fixed64.Half;
        return worldPosition.X >= min.X - padding
            && worldPosition.X <= max.X + padding
            && worldPosition.Z >= min.Y - padding
            && worldPosition.Z <= max.Y + padding;
    }
}
