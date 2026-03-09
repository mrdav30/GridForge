#if !NET8_0_OR_GREATER
using System;
#endif

using FixedMathSharp;

namespace GridForge.Configuration;

/// <summary>
/// Exact identity key for a grid's snapped bounds.
/// </summary>
#if NET8_0_OR_GREATER
public readonly record struct GridBoundsKey(Vector3d BoundsMin, Vector3d BoundsMax);
#endif
#if !NET8_0_OR_GREATER
public readonly struct GridBoundsKey : IEquatable<GridBoundsKey>
{
    public Vector3d BoundsMin { get; }
    public Vector3d BoundsMax { get; }
    public GridBoundsKey(Vector3d boundsMin, Vector3d boundsMax)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }
    public override bool Equals(object? obj) => obj is GridBoundsKey other && Equals(other);
    public bool Equals(GridBoundsKey other) =>
        BoundsMin.Equals(other.BoundsMin) && BoundsMax.Equals(other.BoundsMax);
    public override int GetHashCode() => HashCode.Combine(BoundsMin, BoundsMax);
}
#endif
