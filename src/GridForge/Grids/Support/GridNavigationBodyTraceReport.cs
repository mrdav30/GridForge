//=======================================================================
// GridNavigationBodyTraceReport.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Topology;

namespace GridForge.Grids;

/// <summary>Summarizes one bounded swept navigation-body trace.</summary>
/// <remarks>Interpret only reports returned by a completed API invocation under its success contract. A default value has a zero Complete status and IsComplete is true, but it carries no proof that a trace ran.</remarks>
public readonly struct GridNavigationBodyTraceReport
{
    /// <summary>The completion status.</summary>
    /// <value>The completion status.</value>
    public GridNavigationBodyTraceStatus Status { get; }

    /// <summary>The number of candidate grids examined.</summary>
    /// <value>The number of candidate grids examined.</value>
    public int GridCandidateCount { get; }

    /// <summary>The number of candidate addresses examined.</summary>
    /// <value>The number of candidate addresses examined.</value>
    public int AddressCandidateCount { get; }

    /// <summary>The exact combined grid and address work completed.</summary>
    /// <value>The exact combined grid and address work completed.</value>
    public long CandidateWorkCount { get; }

    /// <summary>The number of required and dependency-evidence cells written.</summary>
    /// <value>The number of required and dependency-evidence cells written.</value>
    public int CellCount { get; }

    /// <summary>The exact committed world revision observed by the trace.</summary>
    /// <value>The exact committed world revision observed by the trace.</value>
    public GridCoveredAddressRunStamp RunStamp { get; }

    /// <summary>Whether the complete physical prism union was written.</summary>
    /// <value>Whether the complete physical prism union was written.</value>
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
