//=======================================================================
// GridNavigationBodyTraceScratch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;
using GridForge.Grids.Topology;
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
        SwiftThrowHelper.ThrowIfNegative(gridCapacity, nameof(gridCapacity));
        SwiftThrowHelper.ThrowIfNegative(addressCapacity, nameof(addressCapacity));

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

internal readonly struct GridNavigationBodyTraceCandidate
{
    internal GridNavigationBodyTraceCandidate(
        VoxelGrid grid,
        VoxelIndex index,
        GridCellPrism prism,
        bool hasPositiveOverlap,
        bool isClosure,
        bool isPhysicallyPresent = false,
        ulong gridHighWaterSequence = 0UL,
        bool isVisited = false)
    {
        Grid = grid;
        Index = index;
        Prism = prism;
        HasPositiveOverlap = hasPositiveOverlap;
        IsClosure = isClosure;
        IsPhysicallyPresent = isPhysicallyPresent;
        GridHighWaterSequence = gridHighWaterSequence;
        IsVisited = isVisited;
    }

    internal VoxelGrid Grid { get; }

    internal VoxelIndex Index { get; }

    internal GridCellPrism Prism { get; }

    internal bool HasPositiveOverlap { get; }

    internal bool IsClosure { get; }

    internal bool IsPhysicallyPresent { get; }

    internal ulong GridHighWaterSequence { get; }

    internal bool IsVisited { get; }

    internal GridNavigationBodyTraceCandidate WithPhysicalEvidence(
        bool isPhysicallyPresent,
        ulong gridHighWaterSequence) =>
        new(
            Grid,
            Index,
            Prism,
            HasPositiveOverlap,
            IsClosure,
            isPhysicallyPresent,
            gridHighWaterSequence,
            IsVisited);

    internal GridNavigationBodyTraceCandidate WithVisited() =>
        new(
            Grid,
            Index,
            Prism,
            HasPositiveOverlap,
            IsClosure,
            IsPhysicallyPresent,
            GridHighWaterSequence,
            isVisited: true);
}
