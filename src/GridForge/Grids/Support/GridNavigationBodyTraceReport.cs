//=======================================================================
// GridNavigationBodyTraceReport.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Topology;

namespace GridForge.Grids;

/// <summary>Summarizes one bounded swept navigation-body trace.</summary>
public readonly struct GridNavigationBodyTraceReport
{
    /// <summary>The completion status.</summary>
    public GridNavigationBodyTraceStatus Status { get; }

    /// <summary>The number of candidate grids examined.</summary>
    public int GridCandidateCount { get; }

    /// <summary>The number of candidate addresses examined.</summary>
    public int AddressCandidateCount { get; }

    /// <summary>The exact combined grid and address work completed.</summary>
    public long CandidateWorkCount { get; }

    /// <summary>The number of required and dependency-evidence cells written.</summary>
    public int CellCount { get; }

    /// <summary>The exact committed world revision observed by the trace.</summary>
    public GridCoveredAddressRunStamp RunStamp { get; }

    /// <summary>Whether the complete physical prism union was written.</summary>
    public bool IsComplete => Status == GridNavigationBodyTraceStatus.Complete;

    internal GridNavigationBodyTraceReport(
        GridNavigationBodyTraceStatus status,
        int gridCandidateCount,
        int addressCandidateCount,
        long candidateWorkCount,
        int cellCount,
        GridCoveredAddressRunStamp runStamp)
    {
        Status = status;
        GridCandidateCount = gridCandidateCount;
        AddressCandidateCount = addressCandidateCount;
        CandidateWorkCount = candidateWorkCount;
        CellCount = cellCount;
        RunStamp = runStamp;
    }
}
