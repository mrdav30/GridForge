using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class GridWorldTests
{
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

        (int xMin, int yMin, int zMin, int xMax, int yMax, int zMax) =
            positiveWorld.GetSpatialGridCellBounds(new Vector3d(8, 8, 8), new Vector3d(0, -8, 0));

        Assert.True(xMin <= xMax);
        Assert.True(yMin <= yMax);
        Assert.True(zMin <= zMax);
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

        Assert.False(GridWorld.TryNormalizeConfiguration(invalidRectangularMetrics, out _, out _, out _));
        Assert.False(GridWorld.TryNormalizeConfiguration(invalidHexMetrics, out _, out _, out _));
        Assert.False(GridWorld.TryNormalizeConfiguration(invalidTopology, out _, out _, out _));
        Assert.False(InvokeTryValidateGridDimensions(new GridDimensions(int.MaxValue, 2, 2)));

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
    public void TryAddGrid_ShouldRejectInactiveDuplicateAndSkipInvalidSpatialNeighbors()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 50);
        GridConfiguration firstConfiguration = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(world.TryAddGrid(firstConfiguration, out ushort firstIndex));
        Assert.False(world.TryAddGrid(firstConfiguration, out ushort duplicateIndex));
        Assert.Equal(firstIndex, duplicateIndex);

        int firstCellIndex = world.GetSpatialGridKey(new Vector3d(0, 0, 0));
        world.SpatialGridHash[firstCellIndex].Add(ushort.MaxValue);

        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(11, 0, 11)),
            out _));

        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();

        Assert.False(inactiveWorld.TryAddGrid(firstConfiguration, out ushort inactiveIndex));
        Assert.Equal(ushort.MaxValue, inactiveIndex);
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

        Assert.False(GridWorld.TryNormalizeConfiguration(
            invalidRectangularMetrics,
            out _,
            out _,
            out _));
        Assert.False(world.TryAddGrid(invalidRectangularMetrics, out ushort invalidRectangularIndex));
        Assert.Equal(ushort.MaxValue, invalidRectangularIndex);

        Assert.False(GridWorld.TryNormalizeConfiguration(
            invalidHexMetrics,
            out _,
            out _,
            out _));
        Assert.False(world.TryAddGrid(invalidHexMetrics, out ushort invalidHexIndex));
        Assert.Equal(ushort.MaxValue, invalidHexIndex);

        Assert.False(GridWorld.TryNormalizeConfiguration(
            invalidTopology,
            out GridConfiguration normalized,
            out IGridTopology topology,
            out GridDimensions dimensions));
        Assert.Equal(default, normalized);
        Assert.Null(topology);
        Assert.Equal(default, dimensions);

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

        Assert.True(InvokeTryValidateGridDimensions(new GridDimensions(1, 1, 1)));
        Assert.False(InvokeTryValidateGridDimensions(new GridDimensions(int.MaxValue, 2, 2)));
        Assert.False(InvokeTryValidateGridDimensions(new GridDimensions(46340, 46340, 2)));
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

        world.SpatialGridHash[world.GetSpatialGridKey(new Vector3d(10, 0, 10))].Add(ushort.MaxValue);
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
    public void ResetAndRemoveGrid_ShouldHandleInactiveMissingAndPartiallyMissingSpatialState()
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

        int removedCellIndex = world.GetSpatialGridKey(new Vector3d(0, 0, 0));
        Assert.True(world.SpatialGridHash.Remove(removedCellIndex));

        Assert.True(world.TryRemoveGrid(grid.GridIndex));
        Assert.False(world.TryRemoveGrid(grid.GridIndex));
    }

    [Fact]
    public void TryRemoveGrid_ShouldSkipStaleSpatialNeighborEntries()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 8);
        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));
        GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(1, 0, 1),
            new Vector3d(2, 0, 2));
        int spatialCell = world.GetSpatialGridKey(firstGrid.BoundsMin);
        world.SpatialGridHash[spatialCell].Add(ushort.MaxValue);

        Assert.True(world.TryRemoveGrid(firstGrid.GridIndex));
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
    public void NotifyActiveGridChange_ShouldIgnoreNullAndInactiveGrids()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));
        int notifications = 0;
        world.OnActiveGridChange += _ => notifications++;

        world.NotifyActiveGridChange(null);
        world.NotifyActiveGridChange(null, GridEventKind.GridChanged, default, Vector3d.Zero);

        Assert.True(world.TryRemoveGrid(grid.GridIndex));
        world.NotifyActiveGridChange(grid);
        world.NotifyActiveGridChange(grid, GridEventKind.GridChanged, default, Vector3d.Zero);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void SpatialCellBounds_ShouldNormalizeReversedBounds()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 10);

        int[] forwardCells = world.GetSpatialGridCells(
            new Vector3d(-12, -4, -20),
            new Vector3d(21, 9, 30)).ToArray();
        int[] reversedCells = world.GetSpatialGridCells(
            new Vector3d(21, 9, 30),
            new Vector3d(-12, -4, -20)).ToArray();

        Assert.Equal(forwardCells, reversedCells);
    }

    [Fact]
    public void FindOverlappingGrids_ShouldReturnUniqueActiveOverlaps()
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

        world.SpatialGridHash[world.GetSpatialGridKey(new Vector3d(0, 0, 0))].Add(ushort.MaxValue);

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
    public void FindOverlappingGrids_ShouldSkipMissingSpatialHashCells()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 4);
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

        world.SpatialGridHash.Remove(world.GetSpatialGridKey(new Vector3d(8, 0, 8)));

        VoxelGrid[] overlaps = world.FindOverlappingGrids(targetGrid).ToArray();

        Assert.Single(overlaps);
        Assert.Same(overlappingGrid, overlaps[0]);
    }

    [Fact]
    public void NotifyActiveGridChange_ShouldIgnoreNullOrInactiveGrid()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        int changedCount = 0;
        world.OnActiveGridChange += _ => changedCount++;

        world.NotifyActiveGridChange(null);

        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));
        Assert.True(world.TryRemoveGrid(grid.GridIndex));

        world.NotifyActiveGridChange(grid);

        Assert.Equal(0, changedCount);
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
        Assert.True(grid.TryAddObstacle(voxel, new BoundsKey(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1))));
        Assert.True(world.TryRemoveGrid(gridIndex));
        world.Reset();

        Assert.Equal(1, addedCount);
        Assert.Equal(2, changedCount);
        Assert.Equal(1, removedCount);
        Assert.Equal(1, resetCount);
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

        Assert.True(GridOccupantManager.TryGetOccupancyTicket(firstWorld, firstOccupant, firstVoxel.WorldIndex, out int firstTicket));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(secondWorld, secondOccupant, secondVoxel.WorldIndex, out int secondTicket));
        Assert.False(GridOccupantManager.TryGetOccupancyTicket(firstWorld, secondOccupant, secondVoxel.WorldIndex, out _));

        Assert.Same(firstOccupant, GridScanManager.ScanRadius(firstWorld, new Vector3d(1, 0, 1), Fixed64.One).Single());
        Assert.Same(secondOccupant, GridScanManager.ScanRadius(secondWorld, new Vector3d(1, 0, 1), Fixed64.One).Single());
        Assert.True(GridScanManager.TryGetVoxelOccupant(firstWorld, firstVoxel.WorldIndex, firstTicket, out IVoxelOccupant resolvedFirst));
        Assert.True(GridScanManager.TryGetVoxelOccupant(secondWorld, secondVoxel.WorldIndex, secondTicket, out IVoxelOccupant resolvedSecond));
        Assert.Same(firstOccupant, resolvedFirst);
        Assert.Same(secondOccupant, resolvedSecond);
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(firstWorld, secondOccupant));
    }

    [Fact]
    public void Blocker_ShouldIgnoreGridChangesFromOtherWorlds()
    {
        using GridWorld blockerWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld otherWorld = GridWorldTestFactory.CreateWorld();
        FixedBoundArea area = new(Vector3d.FromDouble(0, 0, 0), Vector3d.FromDouble(0, 0, 0));
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

    private static bool InvokeTryValidateGridDimensions(GridDimensions dimensions)
    {
        MethodInfo method = typeof(GridWorld).GetMethod(
            "TryValidateGridDimensions",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find GridWorld.TryValidateGridDimensions.");

        return (bool)method.Invoke(null, new object[] { dimensions });
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
