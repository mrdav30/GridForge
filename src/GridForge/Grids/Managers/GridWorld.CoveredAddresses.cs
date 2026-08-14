//=======================================================================
// GridWorld.CoveredAddresses.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace GridForge.Grids;

public sealed partial class GridWorld
{
    /// <summary>
    /// Begins a bounded covered-address query for a declared number of exact eligible grid generations.
    /// </summary>
    /// <param name="cursor">The caller-owned cursor and preallocated generation storage.</param>
    /// <param name="boundsMin">One world-space corner of the coverage bounds.</param>
    /// <param name="boundsMax">The opposite world-space corner of the coverage bounds.</param>
    /// <param name="eligibleGenerationCount">The exact number of canonical generation inputs that will follow.</param>
    /// <returns>True when the declared count fits the cursor; otherwise false and the cursor is stale.</returns>
    public bool TryBeginCoveredAddresses(
        GridCoveredAddressCursor cursor,
        Vector3d boundsMin,
        Vector3d boundsMax,
        int eligibleGenerationCount) =>
        TryBeginCoveredAddressesCore(
            cursor,
            boundsMin,
            boundsMax,
            eligibleGenerationCount,
            hasConfigurationFilter: false,
            default);

    /// <summary>
    /// Begins a bounded covered-address query filtered to one durable grid configuration before address yield.
    /// </summary>
    /// <param name="cursor">The caller-owned cursor and preallocated generation storage.</param>
    /// <param name="boundsMin">One world-space corner of the coverage bounds.</param>
    /// <param name="boundsMax">The opposite world-space corner of the coverage bounds.</param>
    /// <param name="eligibleGenerationCount">The exact number of canonical generation inputs that will follow.</param>
    /// <param name="configurationFilter">The only configuration eligible to produce addresses.</param>
    /// <returns>True when the declared count fits the cursor; otherwise false and the cursor is stale.</returns>
    public bool TryBeginCoveredAddresses(
        GridCoveredAddressCursor cursor,
        Vector3d boundsMin,
        Vector3d boundsMax,
        int eligibleGenerationCount,
        GridConfigurationKey configurationFilter) =>
        TryBeginCoveredAddressesCore(
            cursor,
            boundsMin,
            boundsMax,
            eligibleGenerationCount,
            hasConfigurationFilter: true,
            configurationFilter);

    /// <summary>
    /// Advances generation binding and covered topology-address enumeration under one maintenance snapshot.
    /// </summary>
    /// <param name="cursor">The cursor previously begun through this world.</param>
    /// <param name="generationInputs">The next strictly ascending remaining generation slice, or empty after binding completes.</param>
    /// <param name="results">Caller-owned output storage.</param>
    /// <param name="lookupProbeLimit">Maximum generation-validation and spatial lookup probes for this chunk.</param>
    /// <param name="addressProbeLimit">Maximum topology-address probes for this chunk.</param>
    /// <param name="outputLimit">Maximum covered addresses to write for this chunk.</param>
    /// <param name="lookupProbesConsumed">Exact lookup probes consumed by this chunk.</param>
    /// <param name="addressProbesConsumed">Exact topology-address probes consumed by this chunk.</param>
    /// <param name="generationInputsConsumed">Generation values consumed from the supplied slice.</param>
    /// <param name="outputCount">Covered addresses written to <paramref name="results"/>.</param>
    /// <returns>The resulting cursor state.</returns>
    /// <remarks>
    /// No address can be emitted until every declared generation has been validated and copied. A stale
    /// result writes no output, clears the run stamp, and requires every prior result from the run to be discarded.
    /// Address enumeration does not resolve physical voxel presence or obstacle state. Hex-prism coverage
    /// includes cells touching the inclusive query bounds at either vertical face.
    /// </remarks>
    public GridCoveredAddressCursorStatus AdvanceCoveredAddresses(
        GridCoveredAddressCursor cursor,
        ReadOnlySpan<GridCoveredAddressGeneration> generationInputs,
        Span<GridCoveredAddress> results,
        int lookupProbeLimit,
        int addressProbeLimit,
        int outputLimit,
        out int lookupProbesConsumed,
        out int addressProbesConsumed,
        out int generationInputsConsumed,
        out int outputCount)
    {
        SwiftThrowHelper.ThrowIfNull(cursor, nameof(cursor));
        SwiftThrowHelper.ThrowIfNegative(lookupProbeLimit, nameof(lookupProbeLimit));
        SwiftThrowHelper.ThrowIfNegative(addressProbeLimit, nameof(addressProbeLimit));
        SwiftThrowHelper.ThrowIfNegative(outputLimit, nameof(outputLimit));
        if (outputLimit > results.Length)
            throw new ArgumentOutOfRangeException(nameof(outputLimit));

        ThrowIfNavigationMaintenanceUnavailable();
        lookupProbesConsumed = 0;
        addressProbesConsumed = 0;
        generationInputsConsumed = 0;
        outputCount = 0;
        while (true)
        {
            if (TryEnterNavigationMaintenanceSnapshot())
            {
                try
                {
                    return AdvanceCoveredAddressesCore(
                        cursor,
                        generationInputs,
                        results,
                        lookupProbeLimit,
                        addressProbeLimit,
                        outputLimit,
                        out lookupProbesConsumed,
                        out addressProbesConsumed,
                        out generationInputsConsumed,
                        out outputCount);
                }
                finally
                {
                    ExitNavigationMaintenanceSnapshot();
                }
            }

            WaitForPublishedChangePrefix();
        }
    }

    private bool TryBeginCoveredAddressesCore(
        GridCoveredAddressCursor cursor,
        Vector3d boundsMin,
        Vector3d boundsMax,
        int eligibleGenerationCount,
        bool hasConfigurationFilter,
        GridConfigurationKey configurationFilter)
    {
        SwiftThrowHelper.ThrowIfNull(cursor, nameof(cursor));
        ThrowIfNavigationMaintenanceUnavailable();

        Vector3d queryMinimum = new Vector3d(
            FixedMath.Min(boundsMin.X, boundsMax.X),
            FixedMath.Min(boundsMin.Y, boundsMax.Y),
            FixedMath.Min(boundsMin.Z, boundsMax.Z));
        Vector3d queryMaximum = new Vector3d(
            FixedMath.Max(boundsMin.X, boundsMax.X),
            FixedMath.Max(boundsMin.Y, boundsMax.Y),
            FixedMath.Max(boundsMin.Z, boundsMax.Z));

        while (true)
        {
            if (TryEnterNavigationMaintenanceSnapshot())
            {
                try
                {
                    return cursor.Begin(
                        SpawnToken,
                        Version,
                        _changeSequence,
                        queryMinimum,
                        queryMaximum,
                        eligibleGenerationCount,
                        hasConfigurationFilter,
                        configurationFilter);
                }
                finally
                {
                    ExitNavigationMaintenanceSnapshot();
                }
            }

            WaitForPublishedChangePrefix();
        }
    }

    private GridCoveredAddressCursorStatus AdvanceCoveredAddressesCore(
        GridCoveredAddressCursor cursor,
        ReadOnlySpan<GridCoveredAddressGeneration> generationInputs,
        Span<GridCoveredAddress> results,
        int lookupProbeLimit,
        int addressProbeLimit,
        int outputLimit,
        out int lookupProbesConsumed,
        out int addressProbesConsumed,
        out int generationInputsConsumed,
        out int outputCount)
    {
        lookupProbesConsumed = 0;
        addressProbesConsumed = 0;
        generationInputsConsumed = 0;
        outputCount = 0;

        if (!IsCoveredAddressCursorCurrent(cursor))
            return cursor.MarkStale();

        int remainingInputs = cursor.ExpectedGenerationCount - cursor.BoundGenerationCount;
        if (generationInputs.Length > remainingInputs)
            return cursor.MarkStale();
        if (generationInputs.IsEmpty && remainingInputs != 0)
            return cursor.MarkStale();

        while (generationInputsConsumed < generationInputs.Length)
        {
            if (lookupProbesConsumed == lookupProbeLimit)
                return GridCoveredAddressCursorStatus.More;

            GridCoveredAddressGeneration generation = generationInputs[generationInputsConsumed++];
            ConsumeLookupProbe(cursor, ref lookupProbesConsumed);
            if ((cursor.HasLastBoundGeneration
                    && cursor.LastBoundGeneration.CompareTo(generation) >= 0)
                || !TryBindCoveredAddressGeneration(cursor, generation, out GridCoveredAddressCursor.BoundGeneration bound))
            {
                return cursor.MarkStale();
            }

            cursor.LastBoundGeneration = generation;
            cursor.HasLastBoundGeneration = true;
            cursor.BoundGenerationCount++;
            if (bound.HasRange)
                cursor.Generations[cursor.RangeGenerationCount++] = bound;
        }

        if (cursor.BoundGenerationCount < cursor.ExpectedGenerationCount)
            return GridCoveredAddressCursorStatus.More;

        if (cursor.CurrentStatus == GridCoveredAddressCursorStatus.Complete)
            return cursor.CurrentStatus;

        while (true)
        {
            if (cursor.HasPendingOutput)
            {
                if (outputCount == outputLimit)
                    return GridCoveredAddressCursorStatus.More;

                results[outputCount++] = cursor.PendingOutput;
                cursor.PendingOutput = default;
                cursor.HasPendingOutput = false;
                if (cursor.OutputOrdinal != ulong.MaxValue)
                    cursor.OutputOrdinal++;
                if (outputCount == outputLimit)
                {
                    if (cursor.GenerationOrdinal >= cursor.RangeGenerationCount)
                    {
                        cursor.CurrentStatus = GridCoveredAddressCursorStatus.Complete;
                        return cursor.CurrentStatus;
                    }

                    return GridCoveredAddressCursorStatus.More;
                }
            }

            if (!cursor.HasCurrentAddress)
            {
                if (cursor.GenerationOrdinal >= cursor.RangeGenerationCount)
                {
                    cursor.CurrentStatus = GridCoveredAddressCursorStatus.Complete;
                    return cursor.CurrentStatus;
                }

                cursor.CurrentAddress = cursor.Generations[cursor.GenerationOrdinal].Minimum;
                cursor.HasCurrentAddress = true;
            }

            if (addressProbesConsumed == addressProbeLimit)
                return GridCoveredAddressCursorStatus.More;

            GridCoveredAddressCursor.BoundGeneration current =
                cursor.Generations[cursor.GenerationOrdinal];
            VoxelIndex address = cursor.CurrentAddress;
            cursor.HasCurrentAddress = AdvanceCoveredAddress(
                ref cursor.CurrentAddress,
                current.Minimum,
                current.Maximum);
            if (!cursor.HasCurrentAddress)
                cursor.GenerationOrdinal++;

            ConsumeAddressProbe(cursor, ref addressProbesConsumed);
            if (IsCoveredAddress(cursor, current.Generation, address))
            {
                cursor.PendingOutput = new GridCoveredAddress(current.Generation, address);
                cursor.HasPendingOutput = true;
            }
        }
    }

    private bool TryBindCoveredAddressGeneration(
        GridCoveredAddressCursor cursor,
        GridCoveredAddressGeneration generation,
        out GridCoveredAddressCursor.BoundGeneration bound)
    {
        bound = default;
        if (!BoundsTracker.TryGetValue(generation.ConfigurationKey, out ushort gridIndex)
            || gridIndex != generation.GridIndex
            || !ActiveGrids.IsAllocated(gridIndex))
        {
            return false;
        }

        VoxelGrid grid = ActiveGrids[gridIndex];
        if (grid.SpawnToken != generation.GridSpawnToken
            || grid.ChangeHighWaterSequence != generation.GridHighWaterSequence
            || grid.Configuration.ToGridKey() != generation.ConfigurationKey)
        {
            return false;
        }

        bool eligible = !cursor.HasConfigurationFilter
            || cursor.FilterConfigurationKey == generation.ConfigurationKey;
        VoxelIndex minimum = default;
        VoxelIndex maximum = default;
        Vector3d queryMinimum = cursor.QueryMinimum;
        Vector3d queryMaximum = cursor.QueryMaximum;
        if (eligible && grid.Topology.Kind == GridTopologyKind.HexPrism)
        {
            Fixed64 halfLayerHeight = generation.ConfigurationKey.TopologyMetrics.LayerHeight
                * Fixed64.Half;
            queryMinimum = new Vector3d(
                queryMinimum.X,
                queryMinimum.Y - halfLayerHeight,
                queryMinimum.Z);
            queryMaximum = new Vector3d(
                queryMaximum.X,
                queryMaximum.Y + halfLayerHeight,
                queryMaximum.Z);
        }

        bool hasRange = eligible && TopologyVoxelRangeUtility.TryGetCandidateRange(
            grid,
            queryMinimum,
            queryMaximum,
            out minimum,
            out maximum);
        bound = new GridCoveredAddressCursor.BoundGeneration(
            generation,
            hasRange ? minimum : default,
            hasRange ? maximum : default,
            hasRange);
        return true;
    }

    private bool IsCoveredAddressCursorCurrent(GridCoveredAddressCursor cursor) =>
        cursor.CurrentStatus != GridCoveredAddressCursorStatus.Stale
        && cursor.WorldSpawnToken == SpawnToken
        && cursor.WorldVersion == Version
        && cursor.WorldChangeSequence == _changeSequence;

    private bool IsCoveredAddress(
        GridCoveredAddressCursor cursor,
        GridCoveredAddressGeneration generation,
        VoxelIndex address)
    {
        if (generation.ConfigurationKey.TopologyKind != GridTopologyKind.HexPrism)
            return true;

        VoxelGrid grid = ActiveGrids[generation.GridIndex];
        Vector3d center = grid.GetWorldPosition(address);
        Fixed64 radius = generation.ConfigurationKey.TopologyMetrics.CellRadius;
        Fixed64 halfLayerHeight = generation.ConfigurationKey.TopologyMetrics.LayerHeight
            * Fixed64.Half;
        return center.X >= cursor.QueryMinimum.X - radius
            && center.X <= cursor.QueryMaximum.X + radius
            && center.Y >= cursor.QueryMinimum.Y - halfLayerHeight
            && center.Y <= cursor.QueryMaximum.Y + halfLayerHeight
            && center.Z >= cursor.QueryMinimum.Z - radius
            && center.Z <= cursor.QueryMaximum.Z + radius;
    }

    private static bool AdvanceCoveredAddress(
        ref VoxelIndex address,
        VoxelIndex minimum,
        VoxelIndex maximum)
    {
        if (address.z < maximum.z)
        {
            address.z++;
            return true;
        }

        address.z = minimum.z;
        if (address.y < maximum.y)
        {
            address.y++;
            return true;
        }

        address.y = minimum.y;
        if (address.x < maximum.x)
        {
            address.x++;
            return true;
        }

        return false;
    }

    private static void ConsumeLookupProbe(
        GridCoveredAddressCursor cursor,
        ref int lookupProbesConsumed)
    {
        lookupProbesConsumed++;
        if (cursor.LookupProbeOrdinal != ulong.MaxValue)
            cursor.LookupProbeOrdinal++;
    }

    private static void ConsumeAddressProbe(
        GridCoveredAddressCursor cursor,
        ref int addressProbesConsumed)
    {
        addressProbesConsumed++;
        if (cursor.AddressProbeOrdinal != ulong.MaxValue)
            cursor.AddressProbeOrdinal++;
    }
}
