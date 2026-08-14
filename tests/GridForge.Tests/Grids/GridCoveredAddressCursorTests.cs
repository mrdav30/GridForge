using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridCoveredAddressCursorTests
{
    [Fact]
    public void Advance_ShouldValidateThenYieldCanonicalCoveredAddressesWithinSeparateBudgets()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        VoxelGrid laterKey = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(10, 0, 0),
            new Vector3d(10, 0, 0));
        VoxelGrid earlierKey = GridWorldTestFactory.AddGrid(
            world,
            Vector3d.Zero,
            Vector3d.Zero);
        GridCoveredAddressGeneration[] generations =
        {
            CreateGeneration(earlierKey),
            CreateGeneration(laterKey)
        };
        var cursor = new GridCoveredAddressCursor(generationCapacity: 2);
        var output = new GridCoveredAddress[1];

        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            new Vector3d(10, 0, 0),
            eligibleGenerationCount: generations.Length));

        Assert.Equal(
            GridCoveredAddressCursorStatus.More,
            world.AdvanceCoveredAddresses(
                cursor,
                generations.AsSpan(0, 1),
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 0,
                outputLimit: 0,
                out int firstLookupProbes,
                out int firstAddressProbes,
                out int firstInputsConsumed,
                out int firstCount));
        Assert.Equal(1, firstLookupProbes);
        Assert.Equal(0, firstAddressProbes);
        Assert.Equal(1, firstInputsConsumed);
        Assert.Equal(0, firstCount);

        Assert.Equal(
            GridCoveredAddressCursorStatus.More,
            world.AdvanceCoveredAddresses(
                cursor,
                generations.AsSpan(1, 1),
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 0,
                outputLimit: 0,
                out int secondLookupProbes,
                out int secondAddressProbes,
                out int secondInputsConsumed,
                out int secondCount));
        Assert.Equal(1, secondLookupProbes);
        Assert.Equal(0, secondAddressProbes);
        Assert.Equal(1, secondInputsConsumed);
        Assert.Equal(0, secondCount);

        GridCoveredAddress[] all = new GridCoveredAddress[2];
        int count = 0;
        GridCoveredAddressCursorStatus status;
        do
        {
            status = world.AdvanceCoveredAddresses(
                cursor,
                ReadOnlySpan<GridCoveredAddressGeneration>.Empty,
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 1,
                outputLimit: 1,
                out int lookupProbes,
                out int addressProbes,
                out int generationInputsConsumed,
                out int outputCount);
            Assert.InRange(lookupProbes, 0, 1);
            Assert.InRange(addressProbes, 0, 1);
            Assert.Equal(0, generationInputsConsumed);
            Assert.InRange(outputCount, 0, 1);
            if (outputCount != 0)
                all[count++] = output[0];
        }
        while (status == GridCoveredAddressCursorStatus.More);

        Assert.Equal(GridCoveredAddressCursorStatus.Complete, status);
        Assert.Equal(2, count);
        Assert.Equal(earlierKey.Configuration.ToGridKey(), all[0].ConfigurationKey);
        Assert.Equal(laterKey.Configuration.ToGridKey(), all[1].ConfigurationKey);
        Assert.Equal(default, all[0].VoxelIndex);
        Assert.Equal(default, all[1].VoxelIndex);
        Assert.Equal(earlierKey.SpawnToken, all[0].GridSpawnToken);
        Assert.Equal(laterKey.SpawnToken, all[1].GridSpawnToken);
        Assert.NotEqual(default, cursor.RunStamp);
    }

    [Fact]
    public void Advance_ShouldMarkShortGenerationInputStale()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        var cursor = new GridCoveredAddressCursor(generationCapacity: 2);
        var output = new GridCoveredAddress[1];
        GridCoveredAddressGeneration generation = CreateGeneration(grid);

        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.Zero,
            eligibleGenerationCount: 2));
        Assert.Equal(
            GridCoveredAddressCursorStatus.More,
            world.AdvanceCoveredAddresses(
                cursor,
                new[] { generation },
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 0,
                outputLimit: 0,
                out _,
                out _,
                out int firstInputsConsumed,
                out _));
        Assert.Equal(1, firstInputsConsumed);

        Assert.Equal(
            GridCoveredAddressCursorStatus.Stale,
            world.AdvanceCoveredAddresses(
                cursor,
                ReadOnlySpan<GridCoveredAddressGeneration>.Empty,
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 1,
                outputLimit: 1,
                out int lookupProbes,
                out int addressProbes,
                out int inputsConsumed,
                out int outputCount));
        Assert.Equal(0, lookupProbes);
        Assert.Equal(0, addressProbes);
        Assert.Equal(0, inputsConsumed);
        Assert.Equal(0, outputCount);
        Assert.Equal(default, cursor.RunStamp);
    }

    [Fact]
    public void Advance_ShouldYieldSparseTopologyAddressesWithoutPhysicalVoxels()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            storageKind: GridStorageKind.Sparse);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, configuration);
        var cursor = new GridCoveredAddressCursor(generationCapacity: 1);
        var output = new GridCoveredAddress[3];

        Assert.Equal(0, grid.ConfiguredVoxelCount);
        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            eligibleGenerationCount: 1));

        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            world.AdvanceCoveredAddresses(
                cursor,
                new[] { CreateGeneration(grid) },
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 3,
                outputLimit: 3,
                out int lookupProbes,
                out int addressProbes,
                out int inputsConsumed,
                out int outputCount));
        Assert.Equal(1, lookupProbes);
        Assert.Equal(3, addressProbes);
        Assert.Equal(1, inputsConsumed);
        Assert.Equal(3, outputCount);
        Assert.Equal(new VoxelIndex(0, 0, 0), output[0].VoxelIndex);
        Assert.Equal(new VoxelIndex(1, 0, 0), output[1].VoxelIndex);
        Assert.Equal(new VoxelIndex(2, 0, 0), output[2].VoxelIndex);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void Advance_ShouldIncludeHexAddressesAtVerticalPrismEdges(HexOrientation orientation)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            Fixed64.One,
            new Fixed64(2),
            orientation);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: metrics));
        GridCoveredAddressGeneration[] generation = { CreateGeneration(grid) };
        var cursor = new GridCoveredAddressCursor(generationCapacity: 1);
        var output = new GridCoveredAddress[1];

        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            DrainSinglePoint(
                world,
                cursor,
                generation,
                output,
                new Vector3d(Fixed64.Zero, Fixed64.One, Fixed64.Zero)));
        Assert.Equal(1UL, cursor.OutputOrdinal);
        Assert.Equal(default, output[0].VoxelIndex);

        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            DrainSinglePoint(
                world,
                cursor,
                generation,
                output,
                new Vector3d(Fixed64.Zero, -Fixed64.One, Fixed64.Zero)));
        Assert.Equal(1UL, cursor.OutputOrdinal);
        Assert.Equal(default, output[0].VoxelIndex);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void Advance_ShouldExcludeHexAddressesBeyondVerticalPrismEdges(HexOrientation orientation)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            Fixed64.One,
            new Fixed64(2),
            orientation);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(0, 2, 0),
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: metrics));
        GridCoveredAddressGeneration[] generation = { CreateGeneration(grid) };
        var cursor = new GridCoveredAddressCursor(generationCapacity: 1);
        var output = new GridCoveredAddress[2];

        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.Zero,
            eligibleGenerationCount: 1));
        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            world.AdvanceCoveredAddresses(
                cursor,
                generation,
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 2,
                outputLimit: 2,
                out _,
                out _,
                out _,
                out int outputCount));
        Assert.Equal(1, outputCount);
        Assert.Equal(new VoxelIndex(0, 0, 0), output[0].VoxelIndex);
    }

    [Fact]
    public void Filter_ShouldDiscardNonmatchingGenerationsBeforeAddressProbes()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid selected = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid excluded = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(10, 0, 0),
            new Vector3d(10, 0, 0));
        GridCoveredAddressGeneration[] generations =
        {
            CreateGeneration(selected),
            CreateGeneration(excluded)
        };
        var cursor = new GridCoveredAddressCursor(generationCapacity: 2);
        var output = new GridCoveredAddress[1];

        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            new Vector3d(10, 0, 0),
            eligibleGenerationCount: generations.Length,
            selected.Configuration.ToGridKey()));

        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            world.AdvanceCoveredAddresses(
                cursor,
                generations,
                output,
                lookupProbeLimit: 2,
                addressProbeLimit: 1,
                outputLimit: 1,
                out int lookupProbes,
                out int addressProbes,
                out int inputsConsumed,
                out int outputCount));
        Assert.Equal(2, lookupProbes);
        Assert.Equal(1, addressProbes);
        Assert.Equal(2, inputsConsumed);
        Assert.Equal(1, outputCount);
        Assert.Equal(selected.Configuration.ToGridKey(), output[0].ConfigurationKey);
    }

    [Fact]
    public void Advance_ShouldStopIndependentlyAtLookupAddressAndOutputCeilings()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        GridCoveredAddressGeneration[] generation = { CreateGeneration(grid) };
        var cursor = new GridCoveredAddressCursor(generationCapacity: 1);
        var output = new GridCoveredAddress[1];

        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.Zero,
            eligibleGenerationCount: 1));
        Assert.Equal(
            GridCoveredAddressCursorStatus.More,
            world.AdvanceCoveredAddresses(
                cursor,
                generation,
                output,
                lookupProbeLimit: 0,
                addressProbeLimit: 1,
                outputLimit: 1,
                out int zeroLookupProbes,
                out int zeroLookupAddressProbes,
                out int zeroLookupInputs,
                out int zeroLookupOutput));
        Assert.Equal(0, zeroLookupProbes);
        Assert.Equal(0, zeroLookupAddressProbes);
        Assert.Equal(0, zeroLookupInputs);
        Assert.Equal(0, zeroLookupOutput);

        Assert.Equal(
            GridCoveredAddressCursorStatus.More,
            world.AdvanceCoveredAddresses(
                cursor,
                generation,
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 0,
                outputLimit: 1,
                out int bindingProbes,
                out int zeroAddressProbes,
                out int bindingInputs,
                out int zeroAddressOutput));
        Assert.Equal(1, bindingProbes);
        Assert.Equal(0, zeroAddressProbes);
        Assert.Equal(1, bindingInputs);
        Assert.Equal(0, zeroAddressOutput);

        Assert.Equal(
            GridCoveredAddressCursorStatus.More,
            world.AdvanceCoveredAddresses(
                cursor,
                ReadOnlySpan<GridCoveredAddressGeneration>.Empty,
                output,
                lookupProbeLimit: 0,
                addressProbeLimit: 1,
                outputLimit: 0,
                out int pendingLookupProbes,
                out int pendingAddressProbes,
                out int pendingInputs,
                out int pendingOutput));
        Assert.Equal(0, pendingLookupProbes);
        Assert.Equal(1, pendingAddressProbes);
        Assert.Equal(0, pendingInputs);
        Assert.Equal(0, pendingOutput);

        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            world.AdvanceCoveredAddresses(
                cursor,
                ReadOnlySpan<GridCoveredAddressGeneration>.Empty,
                output,
                lookupProbeLimit: 0,
                addressProbeLimit: 0,
                outputLimit: 1,
                out int outputLookupProbes,
                out int outputAddressProbes,
                out int outputInputs,
                out int outputCount));
        Assert.Equal(0, outputLookupProbes);
        Assert.Equal(0, outputAddressProbes);
        Assert.Equal(0, outputInputs);
        Assert.Equal(1, outputCount);
    }

    [Fact]
    public void BeginAndAdvance_ShouldRejectCapacityExtraDuplicateAndInvalidGenerationInput()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        GridCoveredAddressGeneration generation = CreateGeneration(grid);
        var cursor = new GridCoveredAddressCursor(generationCapacity: 1);
        var output = new GridCoveredAddress[1];

        Assert.False(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.Zero,
            eligibleGenerationCount: 2));
        Assert.Equal(GridCoveredAddressCursorStatus.Stale, cursor.Status);
        Assert.Equal(default, cursor.RunStamp);

        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.Zero,
            eligibleGenerationCount: 1));
        Assert.Equal(
            GridCoveredAddressCursorStatus.Stale,
            world.AdvanceCoveredAddresses(
                cursor,
                new[] { generation, generation },
                output,
                lookupProbeLimit: 2,
                addressProbeLimit: 1,
                outputLimit: 1,
                out int extraLookupProbes,
                out _,
                out int extraInputs,
                out _));
        Assert.Equal(0, extraLookupProbes);
        Assert.Equal(0, extraInputs);

        cursor = new GridCoveredAddressCursor(generationCapacity: 2);
        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.Zero,
            eligibleGenerationCount: 2));
        Assert.Equal(
            GridCoveredAddressCursorStatus.Stale,
            world.AdvanceCoveredAddresses(
                cursor,
                new[] { generation, generation },
                output,
                lookupProbeLimit: 2,
                addressProbeLimit: 1,
                outputLimit: 1,
                out int duplicateLookupProbes,
                out _,
                out int duplicateInputs,
                out _));
        Assert.Equal(2, duplicateLookupProbes);
        Assert.Equal(2, duplicateInputs);

        var invalid = new GridCoveredAddressGeneration(
            generation.ConfigurationKey,
            generation.GridIndex,
            generation.GridSpawnToken + 1,
            generation.GridHighWaterSequence);
        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.Zero,
            eligibleGenerationCount: 1));
        Assert.Equal(
            GridCoveredAddressCursorStatus.Stale,
            world.AdvanceCoveredAddresses(
                cursor,
                new[] { invalid },
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 1,
                outputLimit: 1,
                out int invalidLookupProbes,
                out _,
                out int invalidInputs,
                out _));
        Assert.Equal(1, invalidLookupProbes);
        Assert.Equal(1, invalidInputs);
    }

    [Fact]
    public void Advance_ShouldRejectRecycledSlotAndCommittedWorldMutation()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid removed = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, Vector3d.Zero);
        GridCoveredAddressGeneration removedGeneration = CreateGeneration(removed);
        Assert.True(world.TryRemoveGrid(removed.GridIndex));
        VoxelGrid replacement = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(10, 0, 0),
            new Vector3d(10, 0, 0));
        Assert.Equal(removedGeneration.GridIndex, replacement.GridIndex);
        Assert.NotEqual(removedGeneration.GridSpawnToken, replacement.SpawnToken);

        var cursor = new GridCoveredAddressCursor(generationCapacity: 1);
        var output = new GridCoveredAddress[1];
        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.Zero,
            eligibleGenerationCount: 1));
        Assert.Equal(
            GridCoveredAddressCursorStatus.Stale,
            world.AdvanceCoveredAddresses(
                cursor,
                new[] { removedGeneration },
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 1,
                outputLimit: 1,
                out _,
                out _,
                out _,
                out _));

        GridCoveredAddressGeneration replacementGeneration = CreateGeneration(replacement);
        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            replacement.BoundsMin,
            replacement.BoundsMax,
            eligibleGenerationCount: 1));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(20, 0, 0), new Vector3d(20, 0, 0)),
            out _));
        Assert.Equal(
            GridCoveredAddressCursorStatus.Stale,
            world.AdvanceCoveredAddresses(
                cursor,
                new[] { replacementGeneration },
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 1,
                outputLimit: 1,
                out int staleLookupProbes,
                out int staleAddressProbes,
                out int staleInputs,
                out int staleOutput));
        Assert.Equal(0, staleLookupProbes);
        Assert.Equal(0, staleAddressProbes);
        Assert.Equal(0, staleInputs);
        Assert.Equal(0, staleOutput);
        Assert.Equal(default, cursor.RunStamp);
    }

    [Fact]
    public void Advance_ShouldYieldAddressesInCanonicalXThenYThenZOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            Vector3d.Zero,
            Vector3d.One);
        var cursor = new GridCoveredAddressCursor(generationCapacity: 1);
        var output = new GridCoveredAddress[8];

        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            Vector3d.One,
            eligibleGenerationCount: 1));
        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            world.AdvanceCoveredAddresses(
                cursor,
                new[] { CreateGeneration(grid) },
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 8,
                outputLimit: 8,
                out _,
                out int addressProbes,
                out _,
                out int outputCount));
        Assert.Equal(8, addressProbes);
        Assert.Equal(8, outputCount);
        Assert.Equal(new VoxelIndex(0, 0, 0), output[0].VoxelIndex);
        Assert.Equal(new VoxelIndex(0, 0, 1), output[1].VoxelIndex);
        Assert.Equal(new VoxelIndex(0, 1, 0), output[2].VoxelIndex);
        Assert.Equal(new VoxelIndex(0, 1, 1), output[3].VoxelIndex);
        Assert.Equal(new VoxelIndex(1, 0, 0), output[4].VoxelIndex);
        Assert.Equal(new VoxelIndex(1, 0, 1), output[5].VoxelIndex);
        Assert.Equal(new VoxelIndex(1, 1, 0), output[6].VoxelIndex);
        Assert.Equal(new VoxelIndex(1, 1, 1), output[7].VoxelIndex);
    }

    [Fact]
    public void Advance_ShouldCompleteFilteredNoRangeInputsWithoutAddressWork()
    {
        const int GridCount = 32;
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        var generations = new GridCoveredAddressGeneration[GridCount];
        for (int i = 0; i < GridCount; i++)
        {
            VoxelGrid grid = GridWorldTestFactory.AddGrid(
                world,
                new Vector3d(i, 0, 0),
                new Vector3d(i, 0, 0));
            generations[i] = CreateGeneration(grid);
        }

        var cursor = new GridCoveredAddressCursor(generationCapacity: GridCount);
        var output = new GridCoveredAddress[1];
        GridConfigurationKey missingFilter = new GridConfiguration(
            new Vector3d(100, 0, 0),
            new Vector3d(100, 0, 0)).ToGridKey();
        Assert.True(world.TryBeginCoveredAddresses(
            cursor,
            Vector3d.Zero,
            new Vector3d(GridCount - 1, 0, 0),
            eligibleGenerationCount: GridCount,
            missingFilter));

        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            world.AdvanceCoveredAddresses(
                cursor,
                generations,
                output,
                lookupProbeLimit: GridCount,
                addressProbeLimit: 0,
                outputLimit: 0,
                out int lookupProbes,
                out int addressProbes,
                out int inputsConsumed,
                out int outputCount));
        Assert.Equal(GridCount, lookupProbes);
        Assert.Equal(0, addressProbes);
        Assert.Equal(GridCount, inputsConsumed);
        Assert.Equal(0, outputCount);
    }

    [Fact]
    public void Advance_ShouldAllocateNothingAfterWarmup()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            Vector3d.Zero,
            new Vector3d(1, 0, 0));
        GridCoveredAddressGeneration[] generations = { CreateGeneration(grid) };
        var cursor = new GridCoveredAddressCursor(generationCapacity: 1);
        var output = new GridCoveredAddress[1];
        Assert.Equal(
            GridCoveredAddressCursorStatus.Complete,
            Drain(world, cursor, generations, output));

        long before = GC.GetAllocatedBytesForCurrentThread();
        GridCoveredAddressCursorStatus status = Drain(world, cursor, generations, output);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(GridCoveredAddressCursorStatus.Complete, status);
        Assert.Equal(0, allocated);
    }

    private static GridCoveredAddressCursorStatus Drain(
        GridWorld world,
        GridCoveredAddressCursor cursor,
        GridCoveredAddressGeneration[] generations,
        GridCoveredAddress[] output)
    {
        if (!world.TryBeginCoveredAddresses(
                cursor,
                Vector3d.Zero,
                new Vector3d(1, 0, 0),
                generations.Length))
        {
            return cursor.Status;
        }

        ReadOnlySpan<GridCoveredAddressGeneration> inputs = generations;
        GridCoveredAddressCursorStatus status;
        do
        {
            status = world.AdvanceCoveredAddresses(
                cursor,
                inputs,
                output,
                lookupProbeLimit: 1,
                addressProbeLimit: 1,
                outputLimit: 1,
                out _,
                out _,
                out int inputsConsumed,
                out _);
            inputs = inputs.Slice(inputsConsumed);
        }
        while (status == GridCoveredAddressCursorStatus.More);

        return status;
    }

    private static GridCoveredAddressCursorStatus DrainSinglePoint(
        GridWorld world,
        GridCoveredAddressCursor cursor,
        GridCoveredAddressGeneration[] generations,
        GridCoveredAddress[] output,
        Vector3d point)
    {
        if (!world.TryBeginCoveredAddresses(
                cursor,
                point,
                point,
                generations.Length))
        {
            return cursor.Status;
        }

        return world.AdvanceCoveredAddresses(
            cursor,
            generations,
            output,
            lookupProbeLimit: 1,
            addressProbeLimit: 1,
            outputLimit: 1,
            out _,
            out _,
            out _,
            out _);
    }

    private static GridCoveredAddressGeneration CreateGeneration(VoxelGrid grid) => new(
        grid.Configuration.ToGridKey(),
        grid.GridIndex,
        grid.SpawnToken,
        grid.ChangeHighWaterSequence);
}
