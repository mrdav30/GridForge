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
    public long WorldSpawnToken { get; }

    /// <summary>The world-local structural generation.</summary>
    public uint WorldVersion { get; }

    /// <summary>The committed world-local change sequence.</summary>
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

    /// <inheritdoc />
    public bool Equals(GridCoveredAddressRunStamp other) =>
        WorldSpawnToken == other.WorldSpawnToken
        && WorldVersion == other.WorldVersion
        && ChangeSequence == other.ChangeSequence;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is GridCoveredAddressRunStamp other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(
            WorldSpawnToken.GetHashCode(),
            WorldVersion.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, ChangeSequence.GetHashCode());
    }

    /// <inheritdoc />
    public static bool operator ==(
        GridCoveredAddressRunStamp left,
        GridCoveredAddressRunStamp right) => left.Equals(right);

    /// <inheritdoc />
    public static bool operator !=(
        GridCoveredAddressRunStamp left,
        GridCoveredAddressRunStamp right) => !left.Equals(right);
}
