//=======================================================================
// GridTraceIntervalReport.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids;

/// <summary>
/// Summarizes one bounded trace, canonically ordered only on complete output.
/// </summary>
public readonly struct GridTraceIntervalReport
{
    /// <summary>The completion status.</summary>
    public GridTraceIntervalStatus Status { get; }

    /// <summary>The number of candidate grids discovered.</summary>
    public int GridCandidateCount { get; }

    /// <summary>The number of candidate addresses charged at original-range admission, including omitted planar misses.</summary>
    public int AddressCandidateCount { get; }

    /// <summary>The number of intervals written.</summary>
    public int IntervalCount { get; }

    /// <summary>The number of simultaneous-coverage groups; zero for an explicit partial stop.</summary>
    public int TieGroupCount { get; }

    /// <summary>Whether all parameters from zero through one are covered by grid addresses.</summary>
    public bool HasContinuousAddressCoverage { get; }

    /// <summary>Whether all parameters from zero through one are covered by physically present voxels.</summary>
    public bool HasContinuousPhysicalCoverage { get; }

    /// <summary>Whether the complete trace was written.</summary>
    public bool IsComplete => Status == GridTraceIntervalStatus.Complete;

    internal GridTraceIntervalReport(
        GridTraceIntervalStatus status,
        int gridCandidateCount,
        int candidateCount,
        int intervalCount,
        int tieGroupCount,
        bool hasContinuousAddressCoverage,
        bool hasContinuousPhysicalCoverage)
    {
        Status = status;
        GridCandidateCount = gridCandidateCount;
        AddressCandidateCount = candidateCount;
        IntervalCount = intervalCount;
        TieGroupCount = tieGroupCount;
        HasContinuousAddressCoverage = hasContinuousAddressCoverage;
        HasContinuousPhysicalCoverage = hasContinuousPhysicalCoverage;
    }
}
