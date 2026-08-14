//=======================================================================
// GridBoundaryContactCursor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;
using GridForge.Spatial;

namespace GridForge.Grids.Topology;

/// <summary>
/// Describes the state of a resumable exact boundary-contact query.
/// </summary>
public enum GridBoundaryContactCursorStatus : byte
{
    /// <summary>The bound world or grid generation changed and all prior output must be discarded.</summary>
    Stale = 0,

    /// <summary>More bounded candidate work is required.</summary>
    More = 1,

    /// <summary>All bound spatial pairs and topology addresses were examined.</summary>
    Complete = 2
}

/// <summary>
/// Stores caller-owned scalar progress for a bounded exact boundary-contact query.
/// </summary>
/// <remarks>
/// Create and reuse one instance, begin it through <see cref="GridWorld.BeginBoundaryContacts"/>,
/// and advance it through a <c>GridWorld.AdvanceBoundaryContacts(...)</c> overload. The cursor retains no
/// live grid or voxel reference. If it becomes <see cref="GridBoundaryContactCursorStatus.Stale"/>,
/// discard every contact returned since the last begin and restart the cursor.
/// </remarks>
public sealed class GridBoundaryContactCursor
{
    internal enum TraversalStage : byte
    {
        Pair,
        Source,
        Target
    }

    internal long WorldSpawnToken;
    internal uint WorldVersion;
    internal ulong WorldChangeSequence;
    internal int PairSourceSummaryWordIndex;
    internal ulong PairSourceSummaryWord;
    internal int PairSourceWordIndex;
    internal ulong PairSourceWord;
    internal ushort PairSourceGridIndex;
    internal int PairTargetOrdinal;
    internal ushort SourceGridIndex;
    internal ushort TargetGridIndex;
    internal long SourceGridSpawnToken;
    internal long TargetGridSpawnToken;
    internal ulong SourceGridHighWaterSequence;
    internal ulong TargetGridHighWaterSequence;
    internal GridConfigurationKey SourceConfigurationKey;
    internal GridConfigurationKey TargetConfigurationKey;
    internal GridCellPrism SourcePrism;
    internal VoxelIndex SourceMinimum;
    internal VoxelIndex SourceMaximum;
    internal VoxelIndex SourceAddress;
    internal VoxelIndex TargetMinimum;
    internal VoxelIndex TargetMaximum;
    internal VoxelIndex TargetAddress;
    internal VoxelContactManifold PendingContact;
    internal ushort FilterGridIndex;
    internal long FilterGridSpawnToken;
    internal ulong FilterGridHighWaterSequence;
    internal int FilteredPairPhase;
    internal int FilteredPairRowCount;
    internal int FilteredPairRowOrdinal;
    internal ushort PendingFilteredGridIndex;
    internal bool HasPairSource;
    internal bool HasSourceRange;
    internal bool HasPendingContact;
    internal bool IsFiltered;
    internal bool HasFilteredPairRow;
    internal bool HasPendingFilteredPair;
    internal TraversalStage Stage;
    internal GridBoundaryContactCursorStatus CurrentStatus;

    /// <summary>The current query state.</summary>
    public GridBoundaryContactCursorStatus Status => CurrentStatus;

    /// <summary>The cumulative number of canonical pair, source-address, and target probes.</summary>
    public ulong CandidateOrdinal { get; internal set; }

    /// <summary>The exact committed world revision bound by this cursor.</summary>
    public GridBoundaryContactRunStamp RunStamp =>
        CurrentStatus == GridBoundaryContactCursorStatus.Stale
            ? default
            : new GridBoundaryContactRunStamp(
                WorldSpawnToken,
                WorldVersion,
                WorldChangeSequence);

    internal void Begin(long worldSpawnToken, uint worldVersion, ulong worldChangeSequence)
    {
        WorldSpawnToken = worldSpawnToken;
        WorldVersion = worldVersion;
        WorldChangeSequence = worldChangeSequence;
        PairSourceSummaryWordIndex = 0;
        PairSourceSummaryWord = 0;
        PairSourceWordIndex = 0;
        PairSourceWord = 0;
        PairTargetOrdinal = 0;
        CandidateOrdinal = 0;
        HasPairSource = false;
        HasSourceRange = false;
        HasPendingContact = false;
        Stage = TraversalStage.Pair;
        CurrentStatus = GridBoundaryContactCursorStatus.More;
        ClearFilterBinding();
        ClearParticipantBinding();
    }

    internal void BeginFiltered(
        long worldSpawnToken,
        uint worldVersion,
        ulong worldChangeSequence,
        ushort gridIndex,
        long gridSpawnToken,
        ulong gridHighWaterSequence)
    {
        Begin(worldSpawnToken, worldVersion, worldChangeSequence);
        FilterGridIndex = gridIndex;
        FilterGridSpawnToken = gridSpawnToken;
        FilterGridHighWaterSequence = gridHighWaterSequence;
        IsFiltered = true;
    }

    internal GridBoundaryContactCursorStatus MarkStale()
    {
        WorldSpawnToken = 0;
        WorldVersion = 0;
        WorldChangeSequence = 0;
        PairSourceSummaryWordIndex = 0;
        PairSourceSummaryWord = 0;
        PairSourceWordIndex = 0;
        PairSourceWord = 0;
        PairTargetOrdinal = 0;
        CandidateOrdinal = 0;
        HasPairSource = false;
        HasSourceRange = false;
        HasPendingContact = false;
        Stage = TraversalStage.Pair;
        CurrentStatus = GridBoundaryContactCursorStatus.Stale;
        ClearFilterBinding();
        ClearParticipantBinding();
        return CurrentStatus;
    }

    internal void ClearPairProgress()
    {
        HasSourceRange = false;
        HasPendingContact = false;
        Stage = TraversalStage.Pair;
        SourcePrism = default;
        TargetMinimum = default;
        TargetMaximum = default;
        TargetAddress = default;
        PendingContact = default;
    }

    private void ClearParticipantBinding()
    {
        SourceGridIndex = 0;
        TargetGridIndex = 0;
        SourceGridSpawnToken = 0;
        TargetGridSpawnToken = 0;
        SourceGridHighWaterSequence = 0;
        TargetGridHighWaterSequence = 0;
        SourceConfigurationKey = default;
        TargetConfigurationKey = default;
        SourcePrism = default;
        SourceMinimum = default;
        SourceMaximum = default;
        SourceAddress = default;
        TargetMinimum = default;
        TargetMaximum = default;
        TargetAddress = default;
        PendingContact = default;
    }

    private void ClearFilterBinding()
    {
        FilterGridIndex = 0;
        FilterGridSpawnToken = 0;
        FilterGridHighWaterSequence = 0;
        FilteredPairPhase = 0;
        FilteredPairRowCount = 0;
        FilteredPairRowOrdinal = 0;
        PendingFilteredGridIndex = 0;
        IsFiltered = false;
        HasFilteredPairRow = false;
        HasPendingFilteredPair = false;
    }
}
