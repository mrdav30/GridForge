//=======================================================================
// VoxelIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using SwiftCollections.Utility;

namespace GridForge.Spatial;

/// <summary>
/// Represents the topology-local coordinates of a voxel within a single grid.
/// Rectangular-prism grids interpret the fields as X, Y, and Z. Hex-prism
/// grids interpret them as axial Q, vertical layer, and axial R.
/// </summary>
/// <remarks>Coordinates are stored without grid bounds validation. Equality, hashing, and ordering use only X/Q, Y/layer, and Z/R, so a constructed origin equals <c>default(VoxelIndex)</c> even though their allocation flags differ. The two-argument constructor sets Z/R to zero; it does not map X/Z planar coordinates.</remarks>
public struct VoxelIndex : IEquatable<VoxelIndex>, IComparable<VoxelIndex>
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

    /// <summary>Indicates whether an explicit coordinate constructor was used.</summary>
    /// <value><see langword="true"/> after an explicit coordinate constructor; <see langword="false"/> for <c>default(VoxelIndex)</c>. This flag is excluded from equality, hashing, and ordering.</value>
    public bool IsAllocated { get; private set; }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of <see cref="VoxelIndex"/> with an X and Y coordinate.
    /// Defaults Z to zero.
    /// </summary>
    /// <param name="xCord">The local X or axial Q coordinate. Any 32-bit signed value is stored without bounds validation.</param>
    /// <param name="yCord">The local Y coordinate or vertical layer. This is not a planar Z coordinate.</param>
    public VoxelIndex(int xCord, int yCord) : this(xCord, yCord, 0) { }

    /// <summary>
    /// Initializes a new instance of <see cref="VoxelIndex"/> with X, Y, and Z coordinates.
    /// </summary>
    /// <param name="xCord">The local X or axial Q coordinate. Any 32-bit signed value is stored without bounds validation.</param>
    /// <param name="yCord">The local Y coordinate or vertical layer. This is not a planar Z coordinate.</param>
    /// <param name="zCord">The local Z or axial R coordinate.</param>
    public VoxelIndex(int xCord, int yCord, int zCord)
    {
        x = xCord;
        y = yCord;
        z = zCord;

        IsAllocated = true;
    }

    #endregion

    #region operators

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are equal; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(VoxelIndex left, VoxelIndex right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are different; otherwise <see langword="false"/>.</returns>
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
    /// <returns>The stored components formatted for diagnostics; this text is not a persistence format.</returns>
    public readonly override string ToString()
    {
        return string.Format("({0}, {1}, {2})", x, y, z);
    }

    /// <summary>Computes a hash of the value components used by equality.</summary>
    /// <returns>A hash for lookup; distinct values can share a hash.</returns>
    public override readonly int GetHashCode() => SwiftHashTools.CombineHashCodes(x, y, z);

    /// <summary>Tests whether another value has the same identity components.</summary>
    /// <param name="other">The value to compare; default values are compared by their stored components.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public readonly bool Equals(VoxelIndex other)
    {
        return x == other.x
            && y == other.y
            && z == other.z;
    }

    /// <summary>Compares coordinates lexicographically by X/Q, Y/layer, then Z/R.</summary>
    /// <param name="other">The topology-local coordinate to compare.</param>
    /// <returns>A negative value, zero, or a positive value when this coordinate precedes, equals, or follows the other coordinate.</returns>
    /// <remarks>The allocation flag does not participate. Coordinates are compared without subtraction, including at <see cref="int.MinValue"/> and <see cref="int.MaxValue"/>.</remarks>
    public readonly int CompareTo(VoxelIndex other)
    {
        int result = x.CompareTo(other.x);
        if (result != 0)
            return result;

        result = y.CompareTo(other.y);
        return result != 0 ? result : z.CompareTo(other.z);
    }

    /// <summary>Tests whether an object has the same type and value.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public override readonly bool Equals(object? obj) => obj is VoxelIndex other && Equals(other);

    #endregion
}
