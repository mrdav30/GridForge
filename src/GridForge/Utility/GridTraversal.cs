//=======================================================================
// GridTraversal.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Utility;





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
