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
/// <remarks>The default value is invalid. A nondefault value can become stale after removal, reset, or pooled reuse; validity alone does not prove liveness. Do not serialize this runtime registration identity as a persistent content ID.</remarks>
public readonly struct OccupantTicket : IEquatable<OccupantTicket>
{
    /// <summary>
    /// The O(1) lookup slot within the voxel's occupant bucket.
    /// </summary>
    /// <value>The bucket slot; it is meaningful only when <see cref="IsValid"/> is true and the registration still exists.</value>
    public int Slot { get; }

    /// <summary>
    /// The process-unique generation assigned to this registration lifetime.
    /// </summary>
    /// <value>The registration generation; zero for a default invalid ticket.</value>
    public long Generation { get; }

    internal OccupantTicket(int slot, long generation)
    {
        Slot = slot;
        Generation = generation;
    }

    /// <summary>
    /// Indicates whether this ticket identifies an allocated occupant registration.
    /// </summary>
    /// <value><see langword="true"/> when the stored token components are nondefault. This does not query whether the registration is still live.</value>
    public bool IsValid => Slot >= 0 && Generation > 0;

    /// <summary>Tests whether another value has the same identity components.</summary>
    /// <param name="other">The value to compare; default values are compared by their stored components.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public bool Equals(OccupantTicket other) => Slot == other.Slot && Generation == other.Generation;

    /// <summary>Tests whether an object has the same type and value.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is OccupantTicket other && Equals(other);

    /// <summary>Computes a hash of the value components used by equality.</summary>
    /// <returns>A hash for lookup; distinct values can share a hash.</returns>
    public override int GetHashCode() => SwiftHashTools.CombineHashCodes(Slot, Generation.GetHashCode());

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(OccupantTicket left, OccupantTicket right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are different; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(OccupantTicket left, OccupantTicket right) => !left.Equals(right);
}
