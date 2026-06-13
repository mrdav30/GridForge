//=======================================================================
// VoxelIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Utility;
using System;
using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

/// <summary>
/// Represents the topology-local coordinates of a voxel within a single grid.
/// Rectangular-prism grids interpret the fields as X, Y, and Z. Hex-prism
/// grids interpret them as axial Q, vertical layer, and axial R.
/// </summary>
public struct VoxelIndex : IEquatable<VoxelIndex>
{
    #region Properties

    /// <summary>
    /// The local X coordinate for rectangular-prism grids, or axial Q coordinate for hex-prism grids.
    /// </summary>
    public int x;

    /// <summary>
    /// The local Y coordinate for rectangular-prism grids, or vertical layer for hex-prism grids.
    /// </summary>
    public int y;

    /// <summary>
    /// The local Z coordinate for rectangular-prism grids, or axial R coordinate for hex-prism grids.
    /// </summary>
    public int z;

    /// <summary>
    /// Flag to determine is the struct instance was constructed or is default
    /// </summary>
    public bool IsAllocated { get; private set; }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of <see cref="VoxelIndex"/> with an X and Y coordinate.
    /// Defaults Z to zero.
    /// </summary>
    public VoxelIndex(int xCord, int yCord) : this(xCord, yCord, 0) { }

    /// <summary>
    /// Initializes a new instance of <see cref="VoxelIndex"/> with X, Y, and Z coordinates.
    /// </summary>
    public VoxelIndex(int xCord, int yCord, int zCord)
    {
        x = xCord;
        y = yCord;
        z = zCord;

        IsAllocated = true;
    }

    #endregion

    #region operators

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(VoxelIndex left, VoxelIndex right)
    {
        return left.Equals(right);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(VoxelIndex left, VoxelIndex right)
    {
        return !(left == right);
    }

    #endregion

    #region Overrides

    /// <summary>
    /// Returns a string representation of the coordinates.
    /// </summary>
    public readonly override string ToString()
    {
        return string.Format("({0}, {1}, {2})", x, y, z);
    }

    /// <summary>
    /// Computes a hash code for the coordinates, ensuring uniqueness in hashing collections.
    /// Uses <see cref="SwiftHashTools"/> to generate a stable and consistent hash.
    /// </summary>
    public override readonly int GetHashCode() => SwiftHashTools.CombineHashCodes(x, y, z);

    /// <inheritdoc/>
    public readonly bool Equals(VoxelIndex other)
    {
        return x == other.x
            && y == other.y
            && z == other.z;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is VoxelIndex other && Equals(other);

    #endregion
}
