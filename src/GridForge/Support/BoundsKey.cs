//=======================================================================
// BoundsKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

#if !NET10_0_OR_GREATER
using System;
using SwiftCollections.Utility;
#endif

using FixedMathSharp;

namespace GridForge;

#if NET10_0_OR_GREATER
/// <summary>
/// Exact value key for the supplied bounds.
/// </summary>
/// <param name="BoundsMin">The minimum world-space corner, copied without sorting or snapping.</param>
/// <param name="BoundsMax">The maximum world-space corner, copied without sorting or snapping.</param>
/// <remarks>This value compares the supplied corners exactly; it does not normalize them. The net10.0 target provides synthesized record equality, operators, deconstruction, and formatting.</remarks>
public readonly record struct BoundsKey(Vector3d BoundsMin, Vector3d BoundsMax);
#endif

#if !NET10_0_OR_GREATER
/// <summary>Exact value key for the supplied world-space bounds, without normalization.</summary>
public readonly struct BoundsKey : IEquatable<BoundsKey>
{
    /// <summary>
    /// The minimum bounds of the key.
    /// </summary>
    /// <value>The supplied minimum world-space corner, without normalization.</value>
    public Vector3d BoundsMin { get; }

    /// <summary>
    /// The maximum bounds of the key.
    /// </summary>
    /// <value>The supplied maximum world-space corner, without normalization.</value>
    public Vector3d BoundsMax { get; }

    /// <summary>
    /// Initializes a new instance of the BoundsKey class with the specified minimum and maximum coordinates.
    /// </summary>
    /// <remarks>
    /// Use this constructor to define the spatial limits of a bounding box by specifying its lower
    /// and upper corners.
    /// </remarks>
    /// <param name="boundsMin">The minimum corner of the bounding box, represented as a Vector3d.</param>
    /// <param name="boundsMax">The maximum corner of the bounding box, represented as a Vector3d.</param>
    public BoundsKey(Vector3d boundsMin, Vector3d boundsMax)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    /// <summary>Tests whether an object has the same type and value.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object obj)
    {
        return obj is BoundsKey other && Equals(other);
    }

    /// <summary>Tests whether another value has the same identity components.</summary>
    /// <param name="other">The value to compare; default values are compared by their stored components.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public bool Equals(BoundsKey other) =>
        BoundsMin.Equals(other.BoundsMin) && BoundsMax.Equals(other.BoundsMax);

    /// <summary>Computes a hash of the value components used by equality.</summary>
    /// <returns>A hash for lookup; distinct values can share a hash.</returns>
    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(BoundsMin.GetHashCode(), BoundsMax.GetHashCode());
}
#endif
