//=======================================================================
// GridTraceInterval.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Reports completion or a deterministic caller-supplied trace ceiling.
/// </summary>
public enum GridTraceIntervalStatus : byte
{
    /// <summary>The complete trace was written.</summary>
    Complete,
    /// <summary>The candidate-address ceiling was exhausted.</summary>
    AddressCandidateLimitExceeded,
    /// <summary>The output interval ceiling was exceeded.</summary>
    OutputLimitExceeded,
    /// <summary>A candidate grid cell could not be represented exactly.</summary>
    UnrepresentableGeometry,
    /// <summary>The candidate-grid ceiling was exhausted.</summary>
    GridCandidateLimitExceeded,
    /// <summary>The combined candidate-grid and candidate-address work ceiling was exhausted.</summary>
    CandidateWorkLimitExceeded
}

/// <summary>
/// Describes the exact closed parameter interval where a segment intersects one grid address.
/// </summary>
public readonly struct GridTraceInterval
{
    /// <summary>The exact world, grid-generation, and topology-local address.</summary>
    public WorldVoxelIndex Cell { get; }

    /// <summary>The normalized grid binding key, independent of the recyclable runtime slot.</summary>
    public GridConfigurationKey ConfigurationKey { get; }

    /// <summary>Whether physical storage currently contains the addressed voxel.</summary>
    public bool IsPhysicallyPresent { get; }

    /// <summary>The last committed sequence applied to the traced grid generation.</summary>
    public ulong GridHighWaterSequence { get; }

    /// <summary>The first inclusive segment parameter in the cell prism.</summary>
    public Fixed64 TEnter { get; }

    /// <summary>The last inclusive segment parameter in the cell prism.</summary>
    public Fixed64 TExit { get; }

    /// <summary>
    /// Stable group for peers whose interval interiors overlap, or point peers at one exact parameter.
    /// </summary>
    /// <remarks>
    /// Closed intervals that merely hand off at one endpoint remain successive groups. Group membership
    /// expresses simultaneous geometric coverage only; it does not imply voxel adjacency.
    /// </remarks>
    public int TieGroupId { get; }

    /// <summary>The canonical identity order within <see cref="TieGroupId"/>.</summary>
    public int TieOrder { get; }

    internal GridTraceInterval(
        WorldVoxelIndex cell,
        GridConfigurationKey configurationKey,
        bool isPhysicallyPresent,
        ulong gridHighWaterSequence,
        Fixed64 tEnter,
        Fixed64 tExit,
        int tieGroupId = -1,
        int tieOrder = -1)
    {
        Cell = cell;
        ConfigurationKey = configurationKey;
        IsPhysicallyPresent = isPhysicallyPresent;
        GridHighWaterSequence = gridHighWaterSequence;
        TEnter = tEnter;
        TExit = tExit;
        TieGroupId = tieGroupId;
        TieOrder = tieOrder;
    }

    internal GridTraceInterval WithTie(int tieGroupId, int tieOrder) =>
        new(
            Cell,
            ConfigurationKey,
            IsPhysicallyPresent,
            GridHighWaterSequence,
            TEnter,
            TExit,
            tieGroupId,
            tieOrder);
}

/// <summary>
/// Summarizes one bounded ordered trace.
/// </summary>
public readonly struct GridTraceIntervalReport
{
    /// <summary>The completion status.</summary>
    public GridTraceIntervalStatus Status { get; }

    /// <summary>The number of candidate grids discovered.</summary>
    public int GridCandidateCount { get; }

    /// <summary>The number of unique candidate addresses enumerated.</summary>
    public int AddressCandidateCount { get; }

    /// <summary>The number of intervals written.</summary>
    public int IntervalCount { get; }

    /// <summary>The number of simultaneous-coverage groups.</summary>
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
