//=======================================================================
// GridCoveredAddressCursor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace GridForge.Grids.Topology;

/// <summary>Describes the state of a resumable covered-address query.</summary>
public enum GridCoveredAddressCursorStatus : byte
{
    /// <summary>The bound revision or an eligible grid generation changed; discard the run.</summary>
    Stale = 0,

    /// <summary>More generation-input, lookup, address, or output work is required.</summary>
    More = 1,

    /// <summary>Every eligible covered topology address was examined.</summary>
    Complete = 2
}

/// <summary>Stores caller-owned bounded progress for a value-only covered-address query.</summary>
/// <remarks>
/// Construct with the maximum eligible generation count, begin through <see cref="GridWorld"/>, then
/// supply the next canonical generation-input slice to each advance call until it is consumed. Once all
/// declared generations are bound, pass an empty input span. The cursor retains no live grid or voxel.
/// </remarks>
public sealed class GridCoveredAddressCursor
{
    internal readonly struct BoundGeneration
    {
        internal BoundGeneration(
            GridCoveredAddressGeneration generation,
            VoxelIndex minimum,
            VoxelIndex maximum,
            bool hasRange)
        {
            Generation = generation;
            Minimum = minimum;
            Maximum = maximum;
            HasRange = hasRange;
        }

        internal GridCoveredAddressGeneration Generation { get; }
        internal VoxelIndex Minimum { get; }
        internal VoxelIndex Maximum { get; }
        internal bool HasRange { get; }
    }

    internal readonly BoundGeneration[] Generations;
    internal long WorldSpawnToken;
    internal uint WorldVersion;
    internal ulong WorldChangeSequence;
    internal Vector3d QueryMinimum;
    internal Vector3d QueryMaximum;
    internal GridConfigurationKey FilterConfigurationKey;
    internal GridCoveredAddressGeneration LastBoundGeneration;
    internal GridCoveredAddress PendingOutput;
    internal VoxelIndex CurrentAddress;
    internal int ExpectedGenerationCount;
    internal int BoundGenerationCount;
    internal int RangeGenerationCount;
    internal int GenerationOrdinal;
    internal bool HasConfigurationFilter;
    internal bool HasLastBoundGeneration;
    internal bool HasPendingOutput;
    internal bool HasCurrentAddress;
    internal GridCoveredAddressCursorStatus CurrentStatus;

    /// <summary>Initializes reusable storage for at most <paramref name="generationCapacity"/> eligible grids.</summary>
    public GridCoveredAddressCursor(int generationCapacity)
    {
        SwiftThrowHelper.ThrowIfNegative(generationCapacity, nameof(generationCapacity));
        Generations = generationCapacity == 0
            ? Array.Empty<BoundGeneration>()
            : new BoundGeneration[generationCapacity];
    }

    /// <summary>The maximum eligible generation count accepted by this cursor.</summary>
    public int GenerationCapacity => Generations.Length;

    /// <summary>The current query state.</summary>
    public GridCoveredAddressCursorStatus Status => CurrentStatus;

    /// <summary>The cumulative number of generation validation and spatial lookup probes.</summary>
    public ulong LookupProbeOrdinal { get; internal set; }

    /// <summary>The cumulative number of topology-address probes.</summary>
    public ulong AddressProbeOrdinal { get; internal set; }

    /// <summary>The cumulative number of covered addresses emitted.</summary>
    public ulong OutputOrdinal { get; internal set; }

    /// <summary>The exact committed world revision bound by this cursor.</summary>
    public GridCoveredAddressRunStamp RunStamp =>
        CurrentStatus == GridCoveredAddressCursorStatus.Stale
            ? default
            : new GridCoveredAddressRunStamp(
                WorldSpawnToken,
                WorldVersion,
                WorldChangeSequence);

    internal bool Begin(
        long worldSpawnToken,
        uint worldVersion,
        ulong worldChangeSequence,
        Vector3d queryMinimum,
        Vector3d queryMaximum,
        int expectedGenerationCount,
        bool hasConfigurationFilter,
        GridConfigurationKey filterConfigurationKey)
    {
        if (expectedGenerationCount < 0 || expectedGenerationCount > Generations.Length)
        {
            MarkStale();
            return false;
        }

        WorldSpawnToken = worldSpawnToken;
        WorldVersion = worldVersion;
        WorldChangeSequence = worldChangeSequence;
        QueryMinimum = queryMinimum;
        QueryMaximum = queryMaximum;
        ExpectedGenerationCount = expectedGenerationCount;
        BoundGenerationCount = 0;
        RangeGenerationCount = 0;
        GenerationOrdinal = 0;
        HasConfigurationFilter = hasConfigurationFilter;
        FilterConfigurationKey = filterConfigurationKey;
        LastBoundGeneration = default;
        PendingOutput = default;
        CurrentAddress = default;
        HasPendingOutput = false;
        HasCurrentAddress = false;
        HasLastBoundGeneration = false;
        LookupProbeOrdinal = 0;
        AddressProbeOrdinal = 0;
        OutputOrdinal = 0;
        CurrentStatus = expectedGenerationCount == 0
            ? GridCoveredAddressCursorStatus.Complete
            : GridCoveredAddressCursorStatus.More;
        return true;
    }

    internal GridCoveredAddressCursorStatus MarkStale()
    {
        WorldSpawnToken = 0;
        WorldVersion = 0;
        WorldChangeSequence = 0;
        QueryMinimum = default;
        QueryMaximum = default;
        FilterConfigurationKey = default;
        LastBoundGeneration = default;
        PendingOutput = default;
        CurrentAddress = default;
        ExpectedGenerationCount = 0;
        BoundGenerationCount = 0;
        RangeGenerationCount = 0;
        GenerationOrdinal = 0;
        HasConfigurationFilter = false;
        HasLastBoundGeneration = false;
        HasPendingOutput = false;
        HasCurrentAddress = false;
        LookupProbeOrdinal = 0;
        AddressProbeOrdinal = 0;
        OutputOrdinal = 0;
        CurrentStatus = GridCoveredAddressCursorStatus.Stale;
        return CurrentStatus;
    }
}
