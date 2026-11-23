//=======================================================================
// GridCoveredAddressRunStamp.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace GridForge.Grids.Topology;

/// <summary>Identifies the exact committed world revision bound by a covered-address cursor.</summary>
public readonly struct GridCoveredAddressRunStamp : IEquatable<GridCoveredAddressRunStamp>
{
    /// <summary>The process-unique world allocation identity.</summary>
    /// <value>The process-unique world allocation identity.</value>
    public long WorldSpawnToken { get; }

    /// <summary>The world-local structural generation.</summary>
    /// <value>The world-local structural generation.</value>
    public uint WorldVersion { get; }

    /// <summary>The committed world-local change sequence.</summary>
    /// <value>The committed world-local change sequence.</value>
    public ulong ChangeSequence { get; }

    internal GridCoveredAddressRunStamp(
        long worldSpawnToken,
        uint worldVersion,
        ulong changeSequence)
    {
        WorldSpawnToken = worldSpawnToken;
        WorldVersion = worldVersion;
        ChangeSequence = changeSequence;
    }

    /// <summary>Tests whether the supplied value has the same equality components.</summary>
    /// <param name="other">The value to compare.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public bool Equals(GridCoveredAddressRunStamp other) =>
        WorldSpawnToken == other.WorldSpawnToken
        && WorldVersion == other.WorldVersion
        && ChangeSequence == other.ChangeSequence;

    /// <summary>Tests whether the supplied value has the same equality components.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public override bool Equals(object? obj) =>
        obj is GridCoveredAddressRunStamp other && Equals(other);

    /// <summary>Computes a hash of the equality components.</summary>
    /// <returns>A hash for lookup; collisions are possible and equality remains authoritative.</returns>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(
            WorldSpawnToken.GetHashCode(),
            WorldVersion.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, ChangeSequence.GetHashCode());
    }

    /// <summary>Tests whether two values are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public static bool operator ==(
        GridCoveredAddressRunStamp left,
        GridCoveredAddressRunStamp right) => left.Equals(right);

    /// <summary>Tests whether two values differ.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns>True when any equality component differs; otherwise false.</returns>
    public static bool operator !=(
        GridCoveredAddressRunStamp left,
        GridCoveredAddressRunStamp right) => !left.Equals(right);
}
