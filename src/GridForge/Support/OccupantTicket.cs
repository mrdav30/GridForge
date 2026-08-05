//=======================================================================
// OccupantTicket.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace GridForge;

/// <summary>
/// Identifies one occupant registration in a scan-cell bucket.
/// </summary>
public readonly struct OccupantTicket : IEquatable<OccupantTicket>
{
    /// <summary>
    /// The O(1) lookup slot within the voxel's occupant bucket.
    /// </summary>
    public int Slot { get; }

    /// <summary>
    /// The process-unique generation assigned to this registration lifetime.
    /// </summary>
    public long Generation { get; }

    internal OccupantTicket(int slot, long generation)
    {
        Slot = slot;
        Generation = generation;
    }

    /// <summary>
    /// Indicates whether this ticket identifies an allocated occupant registration.
    /// </summary>
    public bool IsValid => Slot >= 0 && Generation > 0;

    /// <inheritdoc />
    public bool Equals(OccupantTicket other) => Slot == other.Slot && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is OccupantTicket other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => SwiftHashTools.CombineHashCodes(Slot, Generation.GetHashCode());

    /// <summary>
    /// Compares two occupant tickets for equality.
    /// </summary>
    public static bool operator ==(OccupantTicket left, OccupantTicket right) => left.Equals(right);

    /// <summary>
    /// Compares two occupant tickets for inequality.
    /// </summary>
    public static bool operator !=(OccupantTicket left, OccupantTicket right) => !left.Equals(right);
}
