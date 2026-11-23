//=======================================================================
// GridChangeStamp.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using SwiftCollections.Utility;

namespace GridForge.Grids;

/// <summary>
/// Identifies the committed order and logical cause of a GridForge world mutation.
/// </summary>
public readonly struct GridChangeStamp : IEquatable<GridChangeStamp>
{
    /// <summary>
    /// The world-local commit order. Zero is reserved for an invalid or default value.
    /// </summary>
    public readonly ulong Sequence;

    /// <summary>
    /// The world-local logical cause shared by notifications emitted for one mutation.
    /// </summary>
    public readonly ulong CauseId;

    /// <summary>
    /// Whether this value identifies a committed world mutation.
    /// </summary>
    /// <value>Whether this value identifies a committed world mutation.</value>
    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Sequence != 0 && CauseId != 0;
    }

    /// <summary>
    /// Initializes a change stamp.
    /// </summary>
    /// <param name="sequence">The world-local commit order. Zero is reserved for an invalid or default value.</param>
    /// <param name="causeId">The world-local logical cause shared by notifications emitted for one mutation.</param>
    public GridChangeStamp(ulong sequence, ulong causeId)
    {
        Sequence = sequence;
        CauseId = causeId;
    }

    /// <summary>Tests whether the supplied value has the same equality components.</summary>
    /// <param name="other">The value to compare.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public bool Equals(GridChangeStamp other) =>
        Sequence == other.Sequence && CauseId == other.CauseId;

    /// <summary>Tests whether the supplied value has the same equality components.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public override bool Equals(object? obj) => obj is GridChangeStamp other && Equals(other);

    /// <summary>Computes a hash of the equality components.</summary>
    /// <returns>A hash for lookup; collisions are possible and equality remains authoritative.</returns>
    public override int GetHashCode() => SwiftHashTools.CombineHashCodes(Sequence, CauseId);

    /// <summary>Formats this value for diagnostics.</summary>
    /// <returns>Diagnostic text; do not use it as a persistence format.</returns>
    public override string ToString() => $"{Sequence}:{CauseId}";

    /// <summary>Tests whether two values are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public static bool operator ==(GridChangeStamp left, GridChangeStamp right) => left.Equals(right);

    /// <summary>Tests whether two values differ.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns>True when any equality component differs; otherwise false.</returns>
    public static bool operator !=(GridChangeStamp left, GridChangeStamp right) => !left.Equals(right);
}
