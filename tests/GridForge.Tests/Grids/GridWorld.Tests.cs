using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Diagnostics;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class GridWorldTests
{
    [Fact]
    public void TryAddGrid_WithHugeBounds_ShouldRegisterResolveAndRemoveAtDefaultSpatialCellSize()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Fixed64 extent = (Fixed64)100_000;
        GridConfiguration configuration = new(
            new Vector3d(-extent, -extent, -extent),
            new Vector3d(extent, extent, extent),
            topologyMetrics: GridTopologyMetrics.Rectangular(extent),
            storageKind: GridStorageKind.Sparse);

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));
        Assert.True(world.TryGetGrid(Vector3d.Zero, out VoxelGrid resolvedGrid));
        Assert.Equal(gridIndex, resolvedGrid.GridIndex);

        Assert.True(world.TryRemoveGrid(gridIndex));
        Assert.False(world.TryGetGrid(Vector3d.Zero, out _));
    }

    [Fact]
    public void TryGetGrid_WithOverlappingHashAndBvhGrids_ShouldResolveLowestLiveSlot()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration oversizedConfiguration = new(
            Vector3d.Zero,
            new Vector3d(3_200, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)3_200),
            storageKind: GridStorageKind.Sparse);
        GridConfiguration ordinaryConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero);

        Assert.True(world.TryAddGrid(oversizedConfiguration, out ushort oversizedIndex));
        Assert.True(world.TryAddGrid(ordinaryConfiguration, out ushort ordinaryIndex));
        Assert.Equal(0, oversizedIndex);
        Assert.Equal(1, ordinaryIndex);
        Assert.True(world.TryGetGrid(Vector3d.Zero, out VoxelGrid resolved));
        Assert.Equal(oversizedIndex, resolved.GridIndex);

        Assert.True(world.TryRemoveGrid(oversizedIndex));
        Assert.True(world.TryGetGrid(Vector3d.Zero, out resolved));
        Assert.Equal(ordinaryIndex, resolved.GridIndex);

        Assert.True(world.TryAddGrid(oversizedConfiguration, out ushort reusedIndex));
        Assert.Equal(oversizedIndex, reusedIndex);
        Assert.True(world.TryGetGrid(Vector3d.Zero, out resolved));
        Assert.Equal(reusedIndex, resolved.GridIndex);
    }

    [Fact]
    public void FindOverlappingGrids_WithHashAndBvhCandidates_ShouldReturnAscendingSlots()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration oversizedConfiguration = new(
            Vector3d.Zero,
            new Vector3d(3_200, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)3_200),
            storageKind: GridStorageKind.Sparse);
        Assert.True(world.TryAddGrid(oversizedConfiguration, out ushort oversizedIndex));
        Assert.False(world.TryAddGrid(oversizedConfiguration, out ushort duplicateIndex));
        Assert.Equal(oversizedIndex, duplicateIndex);
        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort ordinaryIndex));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.Half)),
            out ushort targetIndex));

        VoxelGrid[] overlaps = world.FindOverlappingGrids(world.ActiveGrids[targetIndex]).ToArray();

        Assert.Equal(new[] { oversizedIndex, ordinaryIndex }, overlaps.Select(grid => grid.GridIndex));
    }

    [Fact]
    public void FindOverlappingGridsInto_AfterWarmup_ClearsAndFillsWithoutAllocating()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid target = GridWorldTestFactory.AddGrid(
            world,
            Vector3d.Zero,
            new Vector3d(2, 0, 2));
        VoxelGrid overlap = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(1, 0, 1),
            new Vector3d(3, 0, 3));
        var results = new SwiftList<VoxelGrid>(2);

        world.FindOverlappingGridsInto(target, results);
        results.Add(target);

        long before = GC.GetAllocatedBytesForCurrentThread();
        world.FindOverlappingGridsInto(target, results);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Single(results);
        Assert.Same(overlap, results[0]);
    }

    [Fact]
    public void Reset_ShouldClearBothIndexTiersBeforeGridSlotReuse()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(3_200, 0, 0),
                topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)3_200),
                storageKind: GridStorageKind.Sparse),
            out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(10_000, 0, 0), new Vector3d(10_000, 0, 0)),
            out _));

        world.Reset();

        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(20_000, 0, 0), new Vector3d(20_000, 0, 0)),
            out ushort replacementIndex));
        Assert.Equal(0, replacementIndex);
        Assert.False(world.TryGetGrid(Vector3d.Zero, out _));
        Assert.False(world.TryGetGrid(new Vector3d(10_000, 0, 0), out _));
        Assert.True(world.TryGetGrid(new Vector3d(20_000, 0, 0), out VoxelGrid replacement));
        Assert.Equal(replacementIndex, replacement.GridIndex);
    }

    [Fact]
    public void ReentrantReset_ShouldRemainInTheSameCommittedPublicationDrain()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        var committedKinds = new List<GridEventKind>();
        bool resetRequested = false;
        world.OnChangeCommitted += eventInfo =>
        {
            committedKinds.Add(eventInfo.ChangeKind);
            if (eventInfo.ChangeKind == GridEventKind.GridAdded && !resetRequested)
            {
                resetRequested = true;
                world.Reset();
            }
        };

        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out _));

        Assert.Equal(
            new[] { GridEventKind.GridAdded, GridEventKind.WorldReset },
            committedKinds);
        Assert.True(world.IsActive);
        Assert.Empty(world.ActiveGrids);
    }

    [Fact]
    public void TryAddGrid_ShouldNormalizeBoundsUsingRectangularTopologyMetrics()
    {
        GridConfiguration rawConfiguration = new(
            Vector3d.FromDouble(-1.25, 0, -1.25),
            Vector3d.FromDouble(1.25, 0, 1.25),
            scanCellSize: 4,
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)0.5));

        Assert.Equal(Vector3d.FromDouble(-1.25, 0, -1.25), rawConfiguration.BoundsMin);
        Assert.Equal(Vector3d.FromDouble(1.25, 0, 1.25), rawConfiguration.BoundsMax);

        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 32);

        Assert.True(world.TryAddGrid(rawConfiguration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.Equal(Vector3d.FromDouble(-1.5, 0, -1.5), grid.BoundsMin);
        Assert.Equal(Vector3d.FromDouble(1.5, 0, 1.5), grid.BoundsMax);
        Assert.Equal(rawConfiguration.ScanCellSize, grid.Configuration.ScanCellSize);
        Assert.Equal(rawConfiguration.TopologyMetrics, grid.Configuration.TopologyMetrics);
    }

    [Fact]
    public void Constructor_ShouldFallbackForInvalidSpatialSettings()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 0);
        using GridWorld negativeWorld = GridWorldTestFactory.CreateWorld(spatialGridCellSize: -8);
        using GridWorld positiveWorld = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 4);

        Assert.Equal(GridWorld.DefaultSpatialGridCellSize, world.SpatialGridCellSize);
        Assert.Equal(GridWorld.DefaultSpatialGridCellSize, negativeWorld.SpatialGridCellSize);
        Assert.Equal(4, positiveWorld.SpatialGridCellSize);
    }

    [Fact]
    public void DiagnosticsEnabled_ShouldLogGridWorldConfigurationAndTopologyGuards()
    {
        using DiagnosticCaptureScope diagnostics = new();

        _ = new GridConfiguration(new Vector3d(5, 1, 5), new Vector3d(1, -1, 1));
        using GridWorld invalidSpatialWorld = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 0);

        GridConfiguration invalidRectangularMetrics = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: new GridTopologyMetrics(
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One,
                Fixed64.One));
        GridConfiguration invalidHexMetrics = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(Fixed64.Zero, Fixed64.One));
        GridConfiguration invalidTopology = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: (GridTopologyKind)int.MaxValue);

        Assert.False(invalidRectangularMetrics.TryNormalize(out _));
        Assert.False(invalidHexMetrics.TryNormalize(out _));
        Assert.False(invalidTopology.TryNormalize(out _));
        Assert.False(new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(50_000, 50_000, 0)).TryNormalize(out _));

        GridConfiguration sparseConfiguration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1),
            storageKind: GridStorageKind.Sparse);
        using GridWorld sparseWorld = GridWorldTestFactory.CreateWorld();
        Assert.False(sparseWorld.TryAddGrid(sparseConfiguration, new bool[3, 1, 2], out _));
        Assert.False(sparseWorld.TryAddGrid(sparseConfiguration, new[] { new VoxelIndex(99, 0, 0) }, out _));

        using GridWorld duplicateWorld = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 50);
        GridConfiguration duplicateConfiguration = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        Assert.True(duplicateWorld.TryAddGrid(duplicateConfiguration, out ushort duplicateIndex));
        Assert.False(duplicateWorld.TryAddGrid(duplicateConfiguration, out ushort existingIndex));
        Assert.Equal(duplicateIndex, existingIndex);
        Assert.False(duplicateWorld.TryGetGrid(-1, out _));
        Assert.False(duplicateWorld.TryGetGrid(1, out _));
        Assert.False(duplicateWorld.TryGetGrid(new Vector3d(25, 0, 25), out _));

        using GridWorld capacityWorld = GridWorldTestFactory.CreateWorld();
        try
        {
            for (int i = 0; i < GridWorld.MaxGrids; i++)
                capacityWorld.ActiveGrids.Add(null);

            Assert.False(capacityWorld.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        }
        finally
        {
            capacityWorld.ActiveGrids.Clear();
        }

        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();
        inactiveWorld.Reset();
        inactiveWorld.IncrementGridVersion(0);
        Assert.False(inactiveWorld.TryAddGrid(duplicateConfiguration, out _));
        Assert.False(inactiveWorld.TryGetGrid(0, out _));
        Assert.False(inactiveWorld.TryGetGrid(Vector3d.Zero, out _));
        Assert.Empty(inactiveWorld.FindOverlappingGrids(new VoxelGrid()));

        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("GridMin was greater"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Spatial grid cell size"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Rectangular-prism topology"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Hex-prism topology"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("not implemented"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("voxel address space"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("mask dimensions"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Sparse voxel index"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("already been allocated"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("out-of-bounds"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("has not been allocated"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("No grid contains position"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("No more grids"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Cannot reset"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Cannot increment"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Cannot add grids"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Cannot resolve grids"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Cannot resolve positions"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Cannot resolve overlaps"));
    }

    [Fact]
    public void DiagnosticsDisabled_ShouldSkipGridWorldErrorGuardFormatting()
    {
        using DiagnosticCaptureScope diagnostics = new(SwiftCollections.Diagnostics.DiagnosticLevel.None);
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero);

        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();

        Assert.False(inactiveWorld.TryAddGrid(configuration, out _));

        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.False(world.TryGetGrid(-1, out _));
        Assert.False(world.TryGetGrid(0, out _));
        Assert.Empty(diagnostics.Messages);
    }

    [Fact]
    public void TryAddGrid_ShouldAllowMatchingBoundsWhenTopologyMetricsDiffer()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 50);
        GridConfiguration defaultMetrics = new(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4));
        GridConfiguration halfMetrics = new(
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 4),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)0.5));

        Assert.True(world.TryAddGrid(defaultMetrics, out ushort defaultIndex));
        Assert.True(world.TryAddGrid(halfMetrics, out ushort halfIndex));
        Assert.NotEqual(defaultIndex, halfIndex);

        Assert.False(world.TryAddGrid(defaultMetrics, out ushort duplicateIndex));
        Assert.Equal(defaultIndex, duplicateIndex);
    }

    [Fact]
    public void TryAddGrid_ShouldRejectDuplicateConfigurations()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 50);
        GridConfiguration firstConfiguration = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(world.TryAddGrid(firstConfiguration, out ushort firstIndex));
        Assert.False(world.TryAddGrid(firstConfiguration, out ushort duplicateIndex));
        Assert.Equal(firstIndex, duplicateIndex);

        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(11, 0, 11)),
            out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryAddGrid_ShouldRejectInactiveWorldRegardlessOfDiagnostics(
        bool disableDiagnostics)
    {
        using GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Reset(deactivate: true);
        DiagnosticLevel previousMinimumLevel = GridForgeLogger.MinimumLevel;
        GridForgeLogger.MinimumLevel = disableDiagnostics
            ? DiagnosticLevel.None
            : DiagnosticLevel.Error;

        try
        {
            Assert.False(inactiveWorld.TryAddGrid(
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
                out ushort inactiveIndex));

            Assert.Equal(ushort.MaxValue, inactiveIndex);
            Assert.Empty(inactiveWorld.ActiveGrids);
        }
        finally
        {
            GridForgeLogger.MinimumLevel = previousMinimumLevel;
        }
    }

    [Fact]
    public void TryAddGrid_ShouldRejectDisposedWorldWithoutAccessingReleasedLock()
    {
        GridWorld disposedWorld = GridWorldTestFactory.CreateWorld();
        disposedWorld.Dispose();

        Assert.False(disposedWorld.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort allocatedIndex));
        Assert.Equal(ushort.MaxValue, allocatedIndex);
    }

    [Fact]
    public void TryAddGrid_ShouldRejectUnsupportedTopologyAtBoundary()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration invalidRectangularMetrics = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: new GridTopologyMetrics(
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One,
                Fixed64.One));
        GridConfiguration invalidHexMetrics = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(Fixed64.Zero, Fixed64.One));
        GridConfiguration invalidTopology = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: (GridTopologyKind)int.MaxValue);

        Assert.False(invalidRectangularMetrics.TryNormalize(out _));
        Assert.False(world.TryAddGrid(invalidRectangularMetrics, out ushort invalidRectangularIndex));
        Assert.Equal(ushort.MaxValue, invalidRectangularIndex);

        Assert.False(invalidHexMetrics.TryNormalize(out _));
        Assert.False(world.TryAddGrid(invalidHexMetrics, out ushort invalidHexIndex));
        Assert.Equal(ushort.MaxValue, invalidHexIndex);

        Assert.False(invalidTopology.TryNormalize(out NormalizedGridConfiguration descriptor));
        Assert.False(descriptor.IsValid);

        Assert.False(world.TryAddGrid(invalidTopology, out ushort allocatedIndex));
        Assert.Equal(ushort.MaxValue, allocatedIndex);
    }

    [Fact]
    public void TryAddGrid_ShouldRejectWhenGridBucketIsAtCapacity()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();

        try
        {
            for (int i = 0; i < GridWorld.MaxGrids; i++)
                world.ActiveGrids.Add(null);

            Assert.False(world.TryAddGrid(
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
                out ushort allocatedIndex));
            Assert.Equal(ushort.MaxValue, allocatedIndex);
        }
        finally
        {
            world.ActiveGrids.Clear();
        }
    }

    [Fact]
    public void TryAddGrid_ShouldRejectSparseMaskShapeInvalidIndicesAndOversizedDimensions()
    {
        GridConfiguration sparseConfiguration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1),
            storageKind: GridStorageKind.Sparse);

        using GridWorld maskWorld = GridWorldTestFactory.CreateWorld();
        Assert.False(maskWorld.TryAddGrid(sparseConfiguration, new bool[3, 1, 2], out ushort maskIndex));
        Assert.Equal(ushort.MaxValue, maskIndex);
        Assert.False(maskWorld.TryAddGrid(sparseConfiguration, new bool[2, 2, 2], out _));
        Assert.False(maskWorld.TryAddGrid(sparseConfiguration, new bool[2, 1, 3], out _));

        using GridWorld invalidIndexWorld = GridWorldTestFactory.CreateWorld();
        Assert.False(invalidIndexWorld.TryAddGrid(
            sparseConfiguration,
            Enumerable.Range(0, 1).Select(_ => new VoxelIndex(99, 0, 0)),
            out ushort invalidIndex));
        Assert.Equal(ushort.MaxValue, invalidIndex);
        Assert.False(invalidIndexWorld.TryAddGrid(sparseConfiguration, new[] { new VoxelIndex(0, 99, 0) }, out _));
        Assert.False(invalidIndexWorld.TryAddGrid(sparseConfiguration, new[] { new VoxelIndex(0, 0, 99) }, out _));

        using GridWorld validIndexWorld = GridWorldTestFactory.CreateWorld();
        Assert.True(validIndexWorld.TryAddGrid(
            sparseConfiguration,
            Enumerable.Range(0, 1).Select(_ => new VoxelIndex(0, 0, 0)),
            out ushort validIndex));
        Assert.NotEqual(ushort.MaxValue, validIndex);

        Assert.True(new GridConfiguration(Vector3d.Zero, Vector3d.Zero).TryNormalize(out _));
        Assert.False(new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(50_000, 50_000, 0)).TryNormalize(out _));
    }

    [Fact]
    public void TryGetGrid_ShouldRejectInactiveOutOfBoundsFreedAndOutOfBoundsPositionLookups()
    {
        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();

        Assert.False(inactiveWorld.TryGetGrid(0, out _));
        Assert.False(inactiveWorld.TryGetGrid(new Vector3d(0, 0, 0), out _));

        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 50);
        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));
        VoxelGrid secondGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(10, 0, 10),
            new Vector3d(11, 0, 11));

        Assert.False(world.TryGetGrid(-1, out _));
        Assert.False(world.TryGetGrid(3, out _));
        Assert.False(world.TryGetGrid(GridWorld.MaxGrids, out _));
        Assert.True(world.TryGetGrid(secondGrid.GridIndex, out VoxelGrid secondByIndex));
        Assert.Same(secondGrid, secondByIndex);
        Assert.False(world.TryGetVoxel(
            new WorldVoxelIndex(world.SpawnToken, firstGrid.GridIndex, firstGrid.SpawnToken, new VoxelIndex(99, 0, 99)),
            out _));

        Assert.True(world.TryGetGrid(new Vector3d(10, 0, 10), out VoxelGrid secondByPosition));
        Assert.Same(secondGrid, secondByPosition);

        Assert.True(world.TryRemoveGrid(firstGrid.GridIndex));
        Assert.False(world.TryGetGrid(firstGrid.GridIndex, out _));
        Assert.False(world.TryGetGrid(new Vector3d(25, 0, 25), out _));
    }

    [Fact]
    public void TryGetGridAndVoxel_WithVector2d_ShouldUseDefaultLayerZero()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 50);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 2, 2));
        Vector2d position = new(1, 1);

        Assert.True(world.TryGetGrid(position, out VoxelGrid resolvedGrid));
        Assert.Same(grid, resolvedGrid);
        Assert.True(world.TryGetVoxel(position, out Voxel resolvedVoxel));
        Assert.Equal(new VoxelIndex(1, 0, 1), resolvedVoxel.Index);
        Assert.True(world.TryGetGridAndVoxel(position, out VoxelGrid resolvedGridAndVoxel, out Voxel resolvedPairVoxel));
        Assert.Same(grid, resolvedGridAndVoxel);
        Assert.Same(resolvedVoxel, resolvedPairVoxel);
    }

    [Fact]
    public void TryGetGridAndVoxel_WithVector2d_ShouldUseExplicitLayerAndRejectOutsideBounds()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 50);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 2, 2));
        Vector2d position = new(1, 1);
        Fixed64 layerY = (Fixed64)2;

        Assert.True(world.TryGetGrid(position, layerY, out VoxelGrid resolvedGrid));
        Assert.Same(grid, resolvedGrid);
        Assert.True(world.TryGetGridAndVoxel(position, layerY, out VoxelGrid resolvedGridAndVoxel, out Voxel resolvedVoxel));
        Assert.Same(grid, resolvedGridAndVoxel);
        Assert.Equal(new VoxelIndex(1, 2, 1), resolvedVoxel.Index);
        Assert.True(world.TryGetVoxel(position, layerY, out Voxel directVoxel));
        Assert.Same(resolvedVoxel, directVoxel);

        Assert.False(world.TryGetGrid(position, (Fixed64)3, out _));
        Assert.False(world.TryGetGridAndVoxel(new Vector2d(3, 1), layerY, out _, out _));
        Assert.False(world.TryGetVoxel(new Vector2d(1, 3), layerY, out _));
    }

    [Fact]
    public void ResetAndRemoveGrid_ShouldHandleInactiveAndMissingGrids()
    {
        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();

        inactiveWorld.Reset();
        inactiveWorld.IncrementGridVersion(0);
        Assert.False(inactiveWorld.TryRemoveGrid(0));
        Assert.Empty(inactiveWorld.FindOverlappingGrids(new VoxelGrid()));

        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 2);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 4));

        Assert.True(world.TryRemoveGrid(grid.GridIndex));
        Assert.False(world.TryRemoveGrid(grid.GridIndex));
    }

    [Fact]
    public void IncrementGridVersion_ShouldUpdateAllocatedGridAndIgnoreInactiveOrMissingGrid()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));
        uint initialWorldVersion = world.Version;
        uint initialGridVersion = grid.Version;

        world.IncrementGridVersion(grid.GridIndex, significant: true);

        Assert.Equal(initialWorldVersion + 1, world.Version);
        Assert.Equal(initialGridVersion + 1, grid.Version);

        world.IncrementGridVersion(ushort.MaxValue, significant: false);

        Assert.Equal(initialWorldVersion + 1, world.Version);
        Assert.Equal(initialGridVersion + 1, grid.Version);

        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();
        inactiveWorld.IncrementGridVersion(0, significant: true);
    }

    [Fact]
    public void FindOverlappingGrids_ShouldReturnActiveOverlapsInGridSlotOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1024);
        VoxelGrid targetGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(8, 0, 8),
            scanCellSize: 2);
        VoxelGrid overlappingGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(4, 0, 4),
            new Vector3d(12, 0, 12),
            scanCellSize: 2);
        GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(20, 0, 20),
            new Vector3d(24, 0, 24),
            scanCellSize: 2);
        GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(100, 0, 100),
            new Vector3d(104, 0, 104),
            scanCellSize: 2);

        VoxelGrid[] overlaps = world.FindOverlappingGrids(targetGrid).ToArray();

        Assert.Single(overlaps);
        Assert.Same(overlappingGrid, overlaps[0]);
    }

    [Fact]
    public void ClosestGridQueries_ShouldHandleEmptyAndInactiveCandidateBuckets()
    {
        using GridWorld emptyWorld = GridWorldTestFactory.CreateWorld();
        Assert.False(emptyWorld.TryGetClosestGrid(Vector3d.Zero, out _));

        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid staleGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));
        Assert.True(world.TryRemoveGrid(staleGrid.GridIndex));
        int staleSlot = world.ActiveGrids.Add(staleGrid);

        try
        {
            Assert.False(world.TryGetClosestGrid(Vector3d.Zero, out _));
        }
        finally
        {
            world.ActiveGrids.RemoveAt(staleSlot);
        }
    }

    [Fact]
    public void FindOverlappingGrids_ShouldReturnEmptyForInactiveWorld()
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));

        world.Dispose();

        Assert.Empty(world.FindOverlappingGrids(grid));
    }

    [Fact]
    public void GridWorldEvents_ShouldSwallowSubscriberExceptions()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        int addedCount = 0;
        int removedCount = 0;
        int changedCount = 0;
        int resetCount = 0;
        Action<GridEventInfo> throwingGridHandler = _ => throw new InvalidOperationException("grid event");
        Action<GridEventInfo> recordingAddedHandler = _ => addedCount++;
        Action<GridEventInfo> recordingRemovedHandler = _ => removedCount++;
        Action<GridEventInfo> recordingChangedHandler = _ => changedCount++;
        Action throwingResetHandler = () => throw new InvalidOperationException("reset event");
        Action recordingResetHandler = () => resetCount++;

        world.OnActiveGridAdded += throwingGridHandler;
        world.OnActiveGridAdded += recordingAddedHandler;
        world.OnActiveGridRemoved += throwingGridHandler;
        world.OnActiveGridRemoved += recordingRemovedHandler;
        world.OnActiveGridChange += throwingGridHandler;
        world.OnActiveGridChange += recordingChangedHandler;
        world.OnActiveGridChange -= recordingChangedHandler;
        world.OnActiveGridChange += recordingChangedHandler;
        world.OnReset += throwingResetHandler;
        world.OnReset += recordingResetHandler;

        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));
        Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken()));
        Assert.True(world.TryRemoveGrid(gridIndex));
        world.Reset();

        Assert.Equal(1, addedCount);
        Assert.Equal(2, changedCount);
        Assert.Equal(1, removedCount);
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public void CommittedChangeEvent_ShouldContinueAfterSubscriberException()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridEventInfo recorded = default;
        Action<GridEventInfo> throwingHandler = _ => throw new InvalidOperationException("committed event");
        void RecordingHandler(GridEventInfo eventInfo) => recorded = eventInfo;
        world.OnChangeCommitted += throwingHandler;
        world.OnChangeCommitted += RecordingHandler;

        try
        {
            Assert.True(world.TryAddGrid(
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
                out ushort gridIndex));

            Assert.Equal(GridEventKind.GridAdded, recorded.ChangeKind);
            Assert.Equal(gridIndex, recorded.GridIndex);
            Assert.Equal(world.SpawnToken, recorded.WorldSpawnToken);
            Assert.NotEqual(0UL, recorded.ChangeSequence);
        }
        finally
        {
            world.OnChangeCommitted -= throwingHandler;
            world.OnChangeCommitted -= RecordingHandler;
        }
    }

    [Fact]
    public void GridWorldEventAccessors_ShouldUseChangeSyncRoot()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Action<GridEventInfo> gridHandler = _ => { };
        Action resetHandler = () => { };

        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnActiveGridAdded += gridHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnActiveGridAdded -= gridHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnActiveGridRemoved += gridHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnActiveGridRemoved -= gridHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnActiveGridChange += gridHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnActiveGridChange -= gridHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnChangeCommitted += gridHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnChangeCommitted -= gridHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnReset += resetHandler);
        AssertEventAccessorUsesChangeSyncRoot(world, () => world.OnReset -= resetHandler);
    }

    [Fact]
    public void TraceLine_ShouldOnlyReturnGridsFromSpecifiedWorld()
    {
        using GridWorld firstWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld secondWorld = GridWorldTestFactory.CreateWorld();
        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(
            firstWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 0));
        VoxelGrid secondGrid = GridWorldTestFactory.AddGrid(
            secondWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 0));

        GridVoxelSet[] tracedSets = GridTracer.TraceLine(
            firstWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 0),
            includeEnd: true).ToArray();

        Assert.Single(tracedSets);
        Assert.Equal(firstGrid.GridIndex, tracedSets[0].Grid.GridIndex);
        Assert.Equal(firstWorld.SpawnToken, tracedSets[0].Grid.World!.SpawnToken);
        Assert.NotEqual(secondGrid.World!.SpawnToken, tracedSets[0].Grid.World!.SpawnToken);
    }

    [Fact]
    public void OccupantTrackingAndScan_ShouldStayInsideExplicitWorld()
    {
        using GridWorld firstWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld secondWorld = GridWorldTestFactory.CreateWorld();
        Guid sharedId = Guid.NewGuid();

        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(
            firstWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2));
        VoxelGrid secondGrid = GridWorldTestFactory.AddGrid(
            secondWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2));
        SharedIdOccupant firstOccupant = new(sharedId, new Vector3d(1, 0, 1), 3);
        SharedIdOccupant secondOccupant = new(sharedId, new Vector3d(1, 0, 1), 3);

        Assert.True(GridOccupantManager.TryRegister(firstWorld, firstOccupant));
        Assert.True(GridOccupantManager.TryRegister(secondWorld, secondOccupant));
        Assert.True(firstGrid.TryGetVoxel(firstOccupant.Position, out Voxel firstVoxel));
        Assert.True(secondGrid.TryGetVoxel(secondOccupant.Position, out Voxel secondVoxel));

        Assert.True(GridOccupantManager.TryGetOccupancyTicket(firstWorld, firstOccupant, firstVoxel.WorldIndex, out OccupantTicket firstTicket));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(secondWorld, secondOccupant, secondVoxel.WorldIndex, out OccupantTicket secondTicket));
        Assert.False(GridOccupantManager.TryGetOccupancyTicket(firstWorld, secondOccupant, secondVoxel.WorldIndex, out _));
        Assert.Equal(firstTicket.Slot, secondTicket.Slot);
        Assert.NotEqual(firstTicket, secondTicket);

        Assert.Same(firstOccupant, GridScanManager.ScanRadius(firstWorld, new Vector3d(1, 0, 1), Fixed64.One).Single());
        Assert.Same(secondOccupant, GridScanManager.ScanRadius(secondWorld, new Vector3d(1, 0, 1), Fixed64.One).Single());
        Assert.True(GridScanManager.TryGetVoxelOccupant(firstWorld, firstVoxel.WorldIndex, firstTicket, out IVoxelOccupant resolvedFirst));
        Assert.True(GridScanManager.TryGetVoxelOccupant(secondWorld, secondVoxel.WorldIndex, secondTicket, out IVoxelOccupant resolvedSecond));
        Assert.False(GridScanManager.TryGetVoxelOccupant(secondWorld, secondVoxel.WorldIndex, firstTicket, out _));
        Assert.Same(firstOccupant, resolvedFirst);
        Assert.Same(secondOccupant, resolvedSecond);
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(firstWorld, secondOccupant));
    }

    [Fact]
    public void Blocker_ShouldIgnoreGridChangesFromOtherWorlds()
    {
        using GridWorld blockerWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld otherWorld = GridWorldTestFactory.CreateWorld();
        FixedBoundBox area = FixedBoundBox.FromMinMax(new(0, 0, 0), new(0, 0, 0));
        BoundsBlocker blocker = new(blockerWorld, area, cacheCoveredVoxels: true);

        blocker.ApplyBlockage();
        Assert.False(blocker.IsBlocking);

        VoxelGrid otherGrid = GridWorldTestFactory.AddGrid(
            otherWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        Assert.True(otherGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel otherVoxel));

        Assert.False(blocker.IsBlocking);
        Assert.False(otherVoxel.IsBlocked);

        VoxelGrid blockerGrid = GridWorldTestFactory.AddGrid(
            blockerWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        Assert.True(blockerGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel blockerVoxel));

        Assert.True(blocker.IsBlocking);
        Assert.True(blockerVoxel.IsBlocked);
        Assert.False(otherVoxel.IsBlocked);
    }

    [Fact]
    public void DisposedWorld_ShouldInvalidateStaleWorldVoxelIndices()
    {
        WorldVoxelIndex staleIndex;

        using (GridWorld originalWorld = GridWorldTestFactory.CreateWorld())
        {
            VoxelGrid originalGrid = GridWorldTestFactory.AddGrid(
                originalWorld,
                new Vector3d(0, 0, 0),
                new Vector3d(0, 0, 0));
            Assert.True(originalGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel originalVoxel));
            staleIndex = originalVoxel.WorldIndex;
        }

        using GridWorld replacementWorld = GridWorldTestFactory.CreateWorld();
        VoxelGrid replacementGrid = GridWorldTestFactory.AddGrid(
            replacementWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        Assert.True(replacementGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel replacementVoxel));

        Assert.False(replacementWorld.TryGetGrid(staleIndex, out _));
        Assert.False(replacementWorld.TryGetVoxel(staleIndex, out _));
        Assert.False(replacementWorld.TryGetGridAndVoxel(staleIndex, out _, out _));
        Assert.NotEqual(staleIndex.WorldSpawnToken, replacementVoxel.WorldIndex.WorldSpawnToken);
    }

    [Fact]
    public void LiveWorlds_ShouldRejectEachOthersVoxelIndices()
    {
        using GridWorld firstWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld secondWorld = GridWorldTestFactory.CreateWorld();
        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(firstWorld, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid secondGrid = GridWorldTestFactory.AddGrid(secondWorld, Vector3d.Zero, Vector3d.Zero);
        Assert.True(firstGrid.TryGetVoxel(Vector3d.Zero, out Voxel firstVoxel));
        Assert.True(secondGrid.TryGetVoxel(Vector3d.Zero, out Voxel secondVoxel));

        Assert.NotEqual(0, firstWorld.SpawnToken);
        Assert.NotEqual(0, secondWorld.SpawnToken);
        Assert.NotEqual(firstWorld.SpawnToken, secondWorld.SpawnToken);
        Assert.False(firstWorld.TryGetGridAndVoxel(secondVoxel.WorldIndex, out _, out _));
        Assert.False(secondWorld.TryGetGridAndVoxel(firstVoxel.WorldIndex, out _, out _));
    }

    [Fact]
    public void ReaddedIdenticalGrid_ShouldInvalidateStaleWorldVoxelIndices()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero);
        VoxelGrid originalGrid = GridWorldTestFactory.AddGrid(
            world,
            configuration.BoundsMin,
            configuration.BoundsMax);
        Assert.True(originalGrid.TryGetVoxel(Vector3d.Zero, out Voxel originalVoxel));
        WorldVoxelIndex staleIndex = originalVoxel.WorldIndex;

        Assert.True(world.TryRemoveGrid(originalGrid.GridIndex));
        Assert.True(world.TryAddGrid(configuration, out ushort replacementIndex));
        VoxelGrid replacementGrid = world.ActiveGrids[replacementIndex];
        Assert.True(replacementGrid.TryGetVoxel(Vector3d.Zero, out Voxel replacementVoxel));

        Assert.Same(originalGrid, replacementGrid);
        Assert.NotEqual(0, replacementGrid.SpawnToken);
        Assert.Equal(staleIndex.GridIndex, replacementIndex);
        Assert.False(world.TryGetGrid(staleIndex, out _));
        Assert.False(world.TryGetVoxel(staleIndex, out _));
        Assert.False(world.TryGetGridAndVoxel(staleIndex, out _, out _));
        Assert.NotEqual(staleIndex.GridSpawnToken, replacementVoxel.WorldIndex.GridSpawnToken);
        Assert.True(world.TryGetGridAndVoxel(replacementVoxel.WorldIndex, out _, out _));
    }

    [Fact]
    public void ReaddedIdenticalGrid_ShouldInvalidatePooledScanCellTicketAndTrackedOccupancy()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero);
        Guid sharedId = Guid.NewGuid();
        SharedIdOccupant originalOccupant = new(sharedId, Vector3d.Zero, 1);
        SharedIdOccupant replacementOccupant = new(sharedId, Vector3d.Zero, 2);
        VoxelGrid originalGrid = GridWorldTestFactory.AddGrid(
            world,
            configuration.BoundsMin,
            configuration.BoundsMax);

        Assert.True(originalGrid.TryAddVoxelOccupant(originalOccupant));
        Assert.True(originalGrid.TryGetVoxel(Vector3d.Zero, out Voxel originalVoxel));
        Assert.True(originalGrid.TryGetScanCell(Vector3d.Zero, out ScanCell originalScanCell));
        WorldVoxelIndex staleIndex = originalVoxel.WorldIndex;
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            world,
            originalOccupant,
            staleIndex,
            out OccupantTicket staleTicket));

        Assert.True(world.TryRemoveGrid(originalGrid.GridIndex));
        Assert.False(GridOccupantManager.TryGetOccupancyTicket(
            world,
            originalOccupant,
            staleIndex,
            out _));
        Assert.True(world.TryAddGrid(configuration, out ushort replacementIndex));
        VoxelGrid replacementGrid = world.ActiveGrids[replacementIndex];
        Assert.True(replacementGrid.TryAddVoxelOccupant(replacementOccupant));
        Assert.True(replacementGrid.TryGetVoxel(Vector3d.Zero, out Voxel replacementVoxel));
        Assert.True(replacementGrid.TryGetScanCell(Vector3d.Zero, out ScanCell replacementScanCell));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            world,
            replacementOccupant,
            replacementVoxel.WorldIndex,
            out OccupantTicket currentTicket));

        Assert.Same(originalGrid, replacementGrid);
        Assert.Same(originalScanCell, replacementScanCell);
        Assert.Equal(staleTicket.Slot, currentTicket.Slot);
        Assert.NotEqual(staleTicket, currentTicket);
        Assert.False(replacementGrid.TryGetVoxelOccupant(replacementVoxel, staleTicket, out _));
        Assert.True(replacementGrid.TryGetVoxelOccupant(
            replacementVoxel,
            currentTicket,
            out IVoxelOccupant resolvedOccupant));
        Assert.Same(replacementOccupant, resolvedOccupant);
        Assert.Single(GridOccupantManager.GetOccupiedIndices(world, replacementOccupant));
    }

    [Fact]
    public void NonDeactivatingReset_ShouldAdvanceGridGenerationAndPreserveWorldIdentity()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero);
        VoxelGrid originalGrid = GridWorldTestFactory.AddGrid(
            world,
            configuration.BoundsMin,
            configuration.BoundsMax);
        Assert.True(originalGrid.TryGetVoxel(Vector3d.Zero, out Voxel originalVoxel));
        WorldVoxelIndex staleIndex = originalVoxel.WorldIndex;

        world.Reset(deactivate: false);
        Assert.True(world.TryAddGrid(configuration, out ushort replacementIndex));
        VoxelGrid replacementGrid = world.ActiveGrids[replacementIndex];
        Assert.True(replacementGrid.TryGetVoxel(Vector3d.Zero, out Voxel replacementVoxel));

        Assert.Equal(staleIndex.WorldSpawnToken, world.SpawnToken);
        Assert.Equal(staleIndex.GridIndex, replacementIndex);
        Assert.False(world.TryGetGridAndVoxel(staleIndex, out _, out _));
        Assert.NotEqual(staleIndex.GridSpawnToken, replacementVoxel.WorldIndex.GridSpawnToken);
        Assert.True(world.TryGetGridAndVoxel(replacementVoxel.WorldIndex, out _, out _));
    }

    [Fact]
    public void NonDeactivatingReset_ShouldPreserveOccupantTicketGeneration()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero);
        Guid sharedId = Guid.NewGuid();
        SharedIdOccupant originalOccupant = new(sharedId, Vector3d.Zero, 1);
        SharedIdOccupant replacementOccupant = new(sharedId, Vector3d.Zero, 2);
        VoxelGrid originalGrid = GridWorldTestFactory.AddGrid(
            world,
            configuration.BoundsMin,
            configuration.BoundsMax);

        Assert.True(originalGrid.TryAddVoxelOccupant(originalOccupant));
        Assert.True(originalGrid.TryGetVoxel(Vector3d.Zero, out Voxel originalVoxel));
        WorldVoxelIndex staleIndex = originalVoxel.WorldIndex;
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            world,
            originalOccupant,
            staleIndex,
            out OccupantTicket staleTicket));

        world.Reset(deactivate: false);
        Assert.False(GridOccupantManager.TryGetOccupancyTicket(
            world,
            originalOccupant,
            staleIndex,
            out _));
        Assert.True(world.TryAddGrid(configuration, out ushort replacementIndex));
        VoxelGrid replacementGrid = world.ActiveGrids[replacementIndex];
        Assert.True(replacementGrid.TryAddVoxelOccupant(replacementOccupant));
        Assert.True(replacementGrid.TryGetVoxel(Vector3d.Zero, out Voxel replacementVoxel));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            world,
            replacementOccupant,
            replacementVoxel.WorldIndex,
            out OccupantTicket currentTicket));

        Assert.Equal(staleTicket.Slot, currentTicket.Slot);
        Assert.NotEqual(staleTicket, currentTicket);
        Assert.False(replacementGrid.TryGetVoxelOccupant(replacementVoxel, staleTicket, out _));
        Assert.True(replacementGrid.TryGetVoxelOccupant(replacementVoxel, currentTicket, out _));
        Assert.Single(GridOccupantManager.GetOccupiedIndices(world, replacementOccupant));
    }

    [Fact]
    public void NonDeactivatingReset_ShouldPreserveObstacleTokenGeneration()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        ObstacleToken firstToken = world.AllocateObstacleToken();

        world.Reset(deactivate: false);

        ObstacleToken secondToken = world.AllocateObstacleToken();
        Assert.True(firstToken.IsValid);
        Assert.True(secondToken.IsValid);
        Assert.NotEqual(firstToken, secondToken);

        world.Reset(deactivate: true);
        Assert.Throws<InvalidOperationException>(() => world.AllocateObstacleToken());
    }

    [Fact]
    public void ObstacleTokens_ShouldNotAliasAcrossWorlds()
    {
        using GridWorld firstWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld secondWorld = GridWorldTestFactory.CreateWorld();

        Assert.True(secondWorld.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort secondGridIndex));

        ObstacleToken firstToken = firstWorld.AllocateObstacleToken();
        ObstacleToken secondToken = secondWorld.AllocateObstacleToken();
        VoxelGrid secondGrid = secondWorld.ActiveGrids[secondGridIndex];

        Assert.True(secondGrid.TryGetVoxel(Vector3d.Zero, out Voxel secondVoxel));
        Assert.NotEqual(firstToken, secondToken);
        Assert.True(secondGrid.TryAddObstacle(secondVoxel, secondToken));
        Assert.False(secondGrid.TryRemoveObstacle(secondVoxel, firstToken));
        Assert.Equal(1, secondVoxel.ObstacleCount);
        Assert.True(secondGrid.TryRemoveObstacle(secondVoxel, secondToken));
    }

    [Fact]
    public void RuntimeIdentityAllocator_ShouldThrowBeforeWraparound()
    {
        long counter = long.MaxValue - 1;

        Assert.Equal(long.MaxValue, RuntimeIdentityAllocator.Allocate(ref counter));
        Assert.Throws<InvalidOperationException>(() => RuntimeIdentityAllocator.Allocate(ref counter));
        Assert.Equal(long.MaxValue, counter);

        long negativeCounter = -1;
        Assert.Throws<InvalidOperationException>(
            () => RuntimeIdentityAllocator.Allocate(ref negativeCounter));
        Assert.Equal(-1, negativeCounter);
    }

    private static void AssertEventAccessorUsesChangeSyncRoot(GridWorld world, ThreadStart accessor)
    {
        using ManualResetEventSlim started = new();
        Thread accessorThread = new(() =>
        {
            started.Set();
            accessor();
        });

        Monitor.Enter(world.ChangeSyncRoot);
        try
        {
            accessorThread.Start();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            AssertThreadIsWaiting(accessorThread);
        }
        finally
        {
            Monitor.Exit(world.ChangeSyncRoot);
        }

        Assert.True(accessorThread.Join(TimeSpan.FromSeconds(5)));
    }

    private static void AssertThreadIsWaiting(Thread thread)
    {
        Assert.True(SpinWait.SpinUntil(
            () => (thread.ThreadState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) != 0,
            TimeSpan.FromSeconds(5)));
        Assert.Equal(ThreadState.WaitSleepJoin, thread.ThreadState & ThreadState.WaitSleepJoin);
    }

    private sealed class SharedIdOccupant : IVoxelOccupant
    {
        public Guid GlobalId { get; }

        public byte OccupantGroupId { get; }

        public Vector3d Position { get; set; }

        public SharedIdOccupant(Guid globalId, Vector3d position, byte occupantGroupId)
        {
            GlobalId = globalId;
            Position = position;
            OccupantGroupId = occupantGroupId;
        }
    }
}
