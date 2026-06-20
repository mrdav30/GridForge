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
        _cellEdge = Fixed64.Zero;
        _hasCachedGrid = false;
    }

    /// <summary>
    /// Visits a voxel only once and returns the selected cell-edge measurement for its grid.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryVisitUnique(Voxel voxel, SwiftHashSet<int> visited, out Fixed64 cellEdge)
    {
        cellEdge = Fixed64.Zero;
        if (!visited.Add(voxel.SpawnToken))
            return false;

        cellEdge = GetCellEdge(voxel);
        return true;
    }

    /// <summary>
    /// Gets the selected cell-edge measurement for a voxel's grid, caching repeated grid lookups.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 GetCellEdge(Voxel voxel)
    {
        if (_hasCachedGrid && voxel.GridIndex == _currentGridIndex)
            return _cellEdge;

        _currentGridIndex = voxel.GridIndex;
        _hasCachedGrid = true;
        VoxelGrid grid = _world.ActiveGrids[_currentGridIndex];
        _cellEdge = _paddingMode == GridTraversalPaddingMode.PlanarMaxCellEdge
            ? GridTopologyMetricUtility.GetPlanarMaxCellEdge(grid)
            : GridTopologyMetricUtility.GetMaxCellEdge(grid);
        return _cellEdge;
    }
}

/// <summary>
/// Provides deterministic helpers for duplicate-safe voxel traversal.
/// </summary>
public static class GridTraversal
{
    /// <summary>
    /// Gets a voxel partition once per voxel spawn token.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetUniquePartition<TPartition>(
        Voxel voxel,
        SwiftHashSet<int> visited,
        out TPartition? partition)
        where TPartition : class, IVoxelPartition
    {
        partition = null;
        return visited.Add(voxel.SpawnToken)
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
