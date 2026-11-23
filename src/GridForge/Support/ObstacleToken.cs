//=======================================================================
// ObstacleToken.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace GridForge;

/// <summary>
/// Opaque process-unique identity for one obstacle registration lifetime.
/// </summary>
/// <remarks>The default value is invalid. A nondefault value can become stale after removal, reset, or pooled reuse; validity alone does not prove liveness. Do not serialize this runtime registration identity as a persistent content ID.</remarks>
public readonly struct ObstacleToken : IEquatable<ObstacleToken>
{
    private readonly long _value;

    internal ObstacleToken(long value)
    {
        _value = value;
    }

    /// <summary>
    /// Indicates whether this token identifies an allocated obstacle registration.
    /// </summary>
    /// <value><see langword="true"/> when the stored token components are nondefault. This does not query whether the registration is still live.</value>
    public bool IsValid => _value != 0;

    /// <summary>Tests whether another value has the same identity components.</summary>
    /// <param name="other">The value to compare; default values are compared by their stored components.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public bool Equals(ObstacleToken other) => _value == other._value;

    /// <summary>Tests whether an object has the same type and value.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is ObstacleToken other && Equals(other);

    /// <summary>Computes a hash of the value components used by equality.</summary>
    /// <returns>A hash for lookup; distinct values can share a hash.</returns>
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>
    /// Returns the runtime token value for diagnostics.
    /// </summary>
    /// <returns>The stored components formatted for diagnostics; this text is not a persistence format.</returns>
    public override string ToString() => _value.ToString();

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ObstacleToken left, ObstacleToken right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are different; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ObstacleToken left, ObstacleToken right) => !left.Equals(right);
}
