//=======================================================================
// GridTraceIntervalScratch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Grids;

/// <summary>
/// Owns reusable caller-side storage for allocation-free warmed interval traces.
/// </summary>
/// <remarks>Instances retain capacity and are not thread-safe.</remarks>
public sealed class GridTraceIntervalScratch
{
    internal SwiftList<ushort> CandidateGrids { get; }

    internal SwiftList<GridTraceAddressCandidate> AddressCandidates { get; }

    /// <summary>Creates trace scratch with optional expected grid and address counts.</summary>
    public GridTraceIntervalScratch(int gridCapacity = 0, int addressCapacity = 0)
    {
        SwiftThrowHelper.ThrowIfNegative(gridCapacity, nameof(gridCapacity));
        SwiftThrowHelper.ThrowIfNegative(addressCapacity, nameof(addressCapacity));

        CandidateGrids = new SwiftList<ushort>(gridCapacity);
        AddressCandidates = new SwiftList<GridTraceAddressCandidate>(addressCapacity);
    }

    /// <summary>Clears temporary values while retaining capacity.</summary>
    public void Clear()
    {
        CandidateGrids.Clear();
        AddressCandidates.Clear();
    }
}

internal readonly struct GridTraceAddressCandidate
{
    public readonly VoxelGrid Grid;
    public readonly VoxelIndex Index;
    public readonly bool IsPhysicallyPresent;

    public GridTraceAddressCandidate(
        VoxelGrid grid,
        VoxelIndex index,
        bool isPhysicallyPresent)
    {
        Grid = grid;
        Index = index;
        IsPhysicallyPresent = isPhysicallyPresent;
    }

    public GridTraceAddressCandidate WithPhysicalPresence(bool isPhysicallyPresent) =>
        new(Grid, Index, isPhysicallyPresent);
}
