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
    public bool IsValid => _value != 0;

    /// <inheritdoc />
    public bool Equals(ObstacleToken other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ObstacleToken other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>
    /// Returns the runtime token value for diagnostics.
    /// </summary>
    public override string ToString() => _value.ToString();

    /// <summary>
    /// Compares two obstacle registration tokens for equality.
    /// </summary>
    public static bool operator ==(ObstacleToken left, ObstacleToken right) => left.Equals(right);

    /// <summary>
    /// Compares two obstacle registration tokens for inequality.
    /// </summary>
    public static bool operator !=(ObstacleToken left, ObstacleToken right) => !left.Equals(right);
}
