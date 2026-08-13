//=======================================================================
// GridChangeStamp.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
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
    /// The world-local commit order. Zero denotes an unstamped compatibility payload.
    /// </summary>
    public readonly ulong Sequence;

    /// <summary>
    /// The world-local logical cause shared by notifications emitted for one mutation.
    /// </summary>
    public readonly ulong CauseId;

    /// <summary>
    /// Whether this value identifies a committed world mutation.
    /// </summary>
    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Sequence != 0 && CauseId != 0;
    }

    /// <summary>
    /// Initializes a change stamp.
    /// </summary>
    public GridChangeStamp(ulong sequence, ulong causeId)
    {
        Sequence = sequence;
        CauseId = causeId;
    }

    /// <inheritdoc />
    public bool Equals(GridChangeStamp other) =>
        Sequence == other.Sequence && CauseId == other.CauseId;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GridChangeStamp other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => SwiftHashTools.CombineHashCodes(Sequence, CauseId);

    /// <inheritdoc />
    public override string ToString() => $"{Sequence}:{CauseId}";

    /// <inheritdoc />
    public static bool operator ==(GridChangeStamp left, GridChangeStamp right) => left.Equals(right);

    /// <inheritdoc />
    public static bool operator !=(GridChangeStamp left, GridChangeStamp right) => !left.Equals(right);
}
