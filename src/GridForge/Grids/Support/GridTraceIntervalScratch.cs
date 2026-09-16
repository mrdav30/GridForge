//=======================================================================
// GridTraceIntervalScratch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace GridForge.Grids;

/// <summary>
/// Owns reusable caller-side storage for allocation-free warmed interval traces.
/// </summary>
/// <remarks>
/// Instances retain capacity and are not thread-safe. Reentrant tracing or clearing an active instance
/// throws before caller output is modified. Completed traces release temporary grid references.
/// </remarks>
public sealed class GridTraceIntervalScratch
{
    private bool _isActive;
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
        ThrowIfActive();
        ClearCore();
    }

    internal void Enter()
    {
        ThrowIfActive();
        _isActive = true;
        ClearCore();
    }

    internal void Exit()
    {
        ClearCore();
        _isActive = false;
    }

    private void ThrowIfActive() => SwiftThrowHelper.ThrowIfTrue(
        _isActive, nameof(GridTraceIntervalScratch), "Interval trace scratch is already in use.");

    private void ClearCore()
    {
        CandidateGrids.Clear();
        AddressCandidates.Clear();
    }
}
