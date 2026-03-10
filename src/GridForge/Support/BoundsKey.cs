#if !NET8_0_OR_GREATER
using System;
using SwiftCollections;
#endif

using FixedMathSharp;

namespace GridForge;

/// <summary>
/// Exact identity key for a grid's snapped bounds.
/// </summary>
#if NET8_0_OR_GREATER
public readonly record struct BoundsKey(Vector3d BoundsMin, Vector3d BoundsMax);
#endif

#if !NET8_0_OR_GREATER
public readonly struct BoundsKey : IEquatable<BoundsKey>
{
    /// <summary>
    /// The minimum bounds of the key.
    /// </summary>
    public Vector3d BoundsMin { get; }

    /// <summary>
    /// The maximum bounds of the key.
    /// </summary>
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

    /// <inheritdoc />
    public override bool Equals(object obj)
    {
        return obj is BoundsKey other && Equals(other);
    }

    /// <inheritdoc />
    public bool Equals(BoundsKey other) =>
        BoundsMin.Equals(other.BoundsMin) && BoundsMax.Equals(other.BoundsMax);

    /// <inheritdoc />
    public override int GetHashCode() => SwiftHashTools.CombineHashCodes(BoundsMin, BoundsMax);
}
#endif
