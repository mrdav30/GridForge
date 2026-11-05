//=======================================================================
// GridNavigationBodyTraceScratch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace GridForge.Grids;

/// <summary>Owns reusable caller-side storage for allocation-free warmed navigation-body traces.</summary>
/// <remarks>Instances retain capacity and are not thread-safe.</remarks>
public sealed class GridNavigationBodyTraceScratch
{
    internal SwiftList<ushort> CandidateGrids { get; }

    internal SwiftList<GridNavigationBodyTraceCandidate> AddressCandidates { get; }

    internal SwiftList<int> UnionMembers { get; }

    /// <summary>Creates scratch with expected grid and address counts.</summary>
    public GridNavigationBodyTraceScratch(int gridCapacity = 0, int addressCapacity = 0)
    {
        ChronicleGuard.ThrowIfNegative(gridCapacity, nameof(gridCapacity));
        ChronicleGuard.ThrowIfNegative(addressCapacity, nameof(addressCapacity));

        CandidateGrids = new SwiftList<ushort>(gridCapacity);
        AddressCandidates = new SwiftList<GridNavigationBodyTraceCandidate>(addressCapacity);
        UnionMembers = new SwiftList<int>(addressCapacity);
    }

    /// <summary>Clears temporary values while retaining capacity.</summary>
    public void Clear()
    {
        CandidateGrids.Clear();
        AddressCandidates.Clear();
        UnionMembers.Clear();
    }
}
