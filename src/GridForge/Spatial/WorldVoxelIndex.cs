//=======================================================================
// WorldVoxelIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using GridForge.Grids;
using SwiftCollections.Utility;

namespace GridForge.Spatial;

/// <summary>
/// Represents the world-scoped runtime identity of a voxel.
/// Used for uniquely identifying voxels across multiple grids and world instances.
/// </summary>
/// <remarks>All tokens, the reusable grid slot, and the topology-local coordinate participate in equality. Construction does not validate liveness. Use the owning world to resolve the value; removed or reused grids invalidate old identities. Runtime tokens are not persistent content IDs.</remarks>
public struct WorldVoxelIndex : IEquatable<WorldVoxelIndex>
{
    #region Properties

    /// <summary>
    /// The process-unique 64-bit runtime allocation token of the owning <see cref="GridWorld"/> instance.
    /// Used to reject stale or cross-world identities safely.
    /// </summary>
    public long WorldSpawnToken;

    /// <summary>
    /// The world-local slot index of the grid in <see cref="GridWorld.ActiveGrids"/>.
    /// </summary>
    public ushort GridIndex;

    /// <summary>
    /// The 64-bit world-local allocation generation assigned to the grid during initialization.
    /// Used to ensure the correct instance is at the assigned <see cref="GridIndex"/>.
    /// </summary>
    public long GridSpawnToken;

    /// <summary>
    /// The local coordinate of a voxel within the grid at <see cref="GridIndex"/>.
    /// </summary>
    public VoxelIndex VoxelIndex;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of <see cref="WorldVoxelIndex"/>, linking a voxel to a specific world and grid instance.
    /// </summary>
    /// <param name="worldSpawnToken">The owning world allocation token, copied without validation.</param>
    /// <param name="gridIndex">The world-local reusable grid slot, copied without validation.</param>
    /// <param name="gridSpawnToken">The grid allocation generation, copied without validation.</param>
    /// <param name="coord">The topology-local coordinate, copied by value.</param>
    public WorldVoxelIndex(
        long worldSpawnToken,
        ushort gridIndex,
        long gridSpawnToken,
        VoxelIndex coord)
    {
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        VoxelIndex = coord;
    }

    #endregion

    #region operators

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are equal; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(WorldVoxelIndex left, WorldVoxelIndex right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are different; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(WorldVoxelIndex left, WorldVoxelIndex right)
    {
        return !(left == right);
    }

    #endregion

    #region Overrides

    /// <summary>
    /// Returns a string representation of the world-scoped voxel identity.
    /// </summary>
    /// <returns>The stored components formatted for diagnostics; this text is not a persistence format.</returns>
    public override readonly string ToString()
    {
        return $"World: {WorldSpawnToken}; Grid: {GridIndex}; Coordinate: {VoxelIndex}";
    }

    /// <summary>Computes a hash of the value components used by equality.</summary>
    /// <returns>A hash for lookup; distinct values can share a hash.</returns>
    public override readonly int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(
            WorldSpawnToken.GetHashCode(),
            GridSpawnToken.GetHashCode(),
            GridIndex,
            VoxelIndex.GetHashCode());

    /// <summary>Tests whether another value has the same identity components.</summary>
    /// <param name="other">The value to compare; default values are compared by their stored components.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public readonly bool Equals(WorldVoxelIndex other)
    {
        return WorldSpawnToken == other.WorldSpawnToken
            && GridIndex == other.GridIndex
            && GridSpawnToken == other.GridSpawnToken
            && VoxelIndex.Equals(other.VoxelIndex);
    }

    /// <summary>Tests whether an object has the same type and value.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public override readonly bool Equals(object? obj) => obj is WorldVoxelIndex other && Equals(other);

    #endregion
}
