//=======================================================================
// GridNavigationBodyTrace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>Reports the outcome of a bounded swept navigation-body trace.</summary>
public enum GridNavigationBodyTraceStatus : byte
{
    /// <summary>The complete physical prism union was written.</summary>
    Complete,
    /// <summary>The complete canonical set was written, but at least one required cell is physically absent.</summary>
    IncompletePhysicalCoverage,
    /// <summary>The inputs or an exact topology prism could not be represented.</summary>
    InvalidOrUnrepresentableGeometry,
    /// <summary>The candidate-address ceiling was exhausted.</summary>
    AddressLimitExceeded,
    /// <summary>The complete required and dependency-evidence output ceiling was exhausted.</summary>
    OutputLimitExceeded,
    /// <summary>The combined grid and address work ceiling was exhausted.</summary>
    CandidateWorkLimitExceeded
}

/// <summary>Describes how one published trace cell participates in coverage validation.</summary>
public enum GridNavigationBodyTraceCellRole : byte
{
    /// <summary>The cell identity contributes directly to the required physical coverage.</summary>
    RequiredCoverage,
    /// <summary>
    /// The missing cell is an OR alternative whose mutation invalidates the negative proof without
    /// making the cell an additional physical requirement.
    /// </summary>
    PhysicalAlternativeDependency
}

/// <summary>Identifies one topology cell and its physical generation evidence.</summary>
public readonly struct GridNavigationBodyTraceCell
{
    /// <summary>The exact world, grid-generation, and topology-local address.</summary>
    public WorldVoxelIndex Cell { get; }

    /// <summary>The normalized grid binding key, independent of its recyclable runtime slot.</summary>
    public GridConfigurationKey ConfigurationKey { get; }

    /// <summary>Whether physical storage contained this address during the trace.</summary>
    public bool IsPhysicallyPresent { get; }

    /// <summary>The last committed sequence applied to the owning grid generation.</summary>
    public ulong GridHighWaterSequence { get; }

    /// <summary>How this identity participates in the physical coverage proof.</summary>
    public GridNavigationBodyTraceCellRole Role { get; }

    internal GridNavigationBodyTraceCell(
        WorldVoxelIndex cell,
        GridConfigurationKey configurationKey,
        bool isPhysicallyPresent,
        ulong gridHighWaterSequence,
        GridNavigationBodyTraceCellRole role)
    {
        Cell = cell;
        ConfigurationKey = configurationKey;
        IsPhysicallyPresent = isPhysicallyPresent;
        GridHighWaterSequence = gridHighWaterSequence;
        Role = role;
    }
}

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
