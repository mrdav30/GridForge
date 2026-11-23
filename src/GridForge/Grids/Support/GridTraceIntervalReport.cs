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
/// <remarks>Interpret only reports returned by a completed API invocation under its success contract. A default value has a zero Complete status and IsComplete is true, but it carries no proof that a trace ran.</remarks>
public readonly struct GridTraceIntervalReport
{
    /// <summary>The completion status.</summary>
    /// <value>The completion status.</value>
    public GridTraceIntervalStatus Status { get; }

    /// <summary>The number of candidate grids discovered.</summary>
    /// <value>The number of candidate grids discovered.</value>
    public int GridCandidateCount { get; }

    /// <summary>The number of candidate addresses charged at original-range admission, including omitted planar misses.</summary>
    /// <value>The number of candidate addresses charged at original-range admission, including omitted planar misses.</value>
    public int AddressCandidateCount { get; }

    /// <summary>The number of intervals written.</summary>
    /// <value>The number of intervals written.</value>
    public int IntervalCount { get; }

    /// <summary>The number of simultaneous-coverage groups; zero for an explicit partial stop.</summary>
    /// <value>The number of simultaneous-coverage groups; zero for an explicit partial stop.</value>
    public int TieGroupCount { get; }

    /// <summary>Whether all parameters from zero through one are covered by grid addresses.</summary>
    /// <value>Whether all parameters from zero through one are covered by grid addresses.</value>
    public bool HasContinuousAddressCoverage { get; }

    /// <summary>Whether all parameters from zero through one are covered by physically present voxels.</summary>
    /// <value>Whether all parameters from zero through one are covered by physically present voxels.</value>
    public bool HasContinuousPhysicalCoverage { get; }

    /// <summary>Whether the complete trace was written.</summary>
    /// <value>Whether the complete trace was written.</value>
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
