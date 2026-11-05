//=======================================================================
// GridNavigationBodyTraceCandidate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace GridForge.Grids;

internal readonly struct GridNavigationBodyTraceCandidate
{
    internal GridNavigationBodyTraceCandidate(
        VoxelGrid grid,
        VoxelIndex index,
        GridCellPrism prism,
        bool hasPositiveOverlap,
        bool isClosure,
        bool isPhysicallyPresent = false,
        ulong gridLastChangeSequence = 0UL,
        bool isVisited = false)
    {
        Grid = grid;
        Index = index;
        Prism = prism;
        HasPositiveOverlap = hasPositiveOverlap;
        IsClosure = isClosure;
        IsPhysicallyPresent = isPhysicallyPresent;
        GridLastChangeSequence = gridLastChangeSequence;
        IsVisited = isVisited;
    }

    internal VoxelGrid Grid { get; }

    internal VoxelIndex Index { get; }

    internal GridCellPrism Prism { get; }

    internal bool HasPositiveOverlap { get; }

    internal bool IsClosure { get; }

    internal bool IsPhysicallyPresent { get; }

    internal ulong GridLastChangeSequence { get; }

    internal bool IsVisited { get; }

    internal GridNavigationBodyTraceCandidate WithPhysicalEvidence(
        bool isPhysicallyPresent,
        ulong gridLastChangeSequence) =>
        new(
            Grid,
            Index,
            Prism,
            HasPositiveOverlap,
            IsClosure,
            isPhysicallyPresent,
            gridLastChangeSequence,
            IsVisited);

    internal GridNavigationBodyTraceCandidate WithVisited() =>
        new(
            Grid,
            Index,
            Prism,
            HasPositiveOverlap,
            IsClosure,
            IsPhysicallyPresent,
            GridLastChangeSequence,
            isVisited: true);
}
