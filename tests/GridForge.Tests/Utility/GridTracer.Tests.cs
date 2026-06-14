using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class GridTracerTests : IDisposable
{
    private GridWorld _world;

    public GridTracerTests()
    {
        _world = GridWorldTestFactory.CreateWorld();
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TraceLine_ShouldReturnCorrectVoxels()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-50, -1, -50), new Vector3d(50, 1, 50)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d start = Vector3d.FromDouble(5, 0.5, 5);
        Vector3d end = Vector3d.FromDouble(45.28, 1, 18.31);

        List<Voxel> tracedVoxels = new();

        foreach (var gridVoxelSet in GridTracer.TraceLine(_world, start, end, includeEnd: true))
        {
            if (gridVoxelSet.Grid.GridIndex == gridIndex)
                tracedVoxels.AddRange(gridVoxelSet.Voxels);
        }

        Assert.NotEmpty(tracedVoxels);

        grid.TryGetVoxel(start, out Voxel startVoxel);
        grid.TryGetVoxel(end, out Voxel endVoxel);

        // Ensure that the first and last voxel correspond to the start and end positions
        Assert.Equal(startVoxel.WorldIndex, tracedVoxels.First().WorldIndex);
        Assert.Equal(endVoxel.WorldIndex, tracedVoxels.Last().WorldIndex);
    }

    [Fact]
    public void TraceLine_ShouldNotReturnEmptySetsForMissingSparseVoxels()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4));

        Assert.True(_world.TryAddGrid(config, new[] { new VoxelIndex(4, 0, 4) }, out _));

        GridVoxelSet[] coveredSets = GridTracer.TraceLine(
            _world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            includeEnd: true).ToArray();

        Assert.Empty(coveredSets);
    }

    [Fact]
    public void TraceLine_ShouldIncludeConfiguredSparseEndpointWhenIntermediateVoxelsAreMissing()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4));
        VoxelIndex endpointIndex = new(4, 0, 4);

        Assert.True(_world.TryAddGrid(config, new[] { endpointIndex }, out _));

        VoxelIndex[] tracedIndices = GridTracer.TraceLine(
                _world,
                new Vector3d(0, 0, 0),
                new Vector3d(4, 0, 4),
                includeEnd: true)
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.Index)
            .ToArray();

        Assert.Equal(new[] { endpointIndex }, tracedIndices);
    }

    [Fact]
    public void GetCoveredVoxels_ShouldReturnConfiguredSparseVoxelsOnly()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4));
        VoxelIndex[] configured =
        {
            new(1, 0, 1),
            new(3, 0, 3)
        };

        Assert.True(_world.TryAddGrid(config, configured, out _));

        GridVoxelSet[] missingSets = GridTracer.GetCoveredVoxels(
            _world,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0)).ToArray();
        VoxelIndex[] coveredIndices = GridTracer.GetCoveredVoxels(
                _world,
                new Vector3d(0, 0, 0),
                new Vector3d(4, 0, 4))
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.Index)
            .ToArray();

        Assert.Empty(missingSets);
        Assert.Equal(configured, coveredIndices);
    }

    [Fact]
    public void GetCoveredScanCells_ShouldReturnSparseScanCellsForConfiguredBlocksOnly()
    {
        GridConfiguration config = CreateSparseConfig(
            new Vector3d(0, 0, 0),
            new Vector3d(5, 0, 5),
            scanCellSize: 2);

        Assert.True(_world.TryAddGrid(
            config,
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(4, 0, 4) },
            out _));

        int[] missingCellKeys = GridTracer.GetCoveredScanCells(
                _world,
                new Vector3d(2, 0, 2),
                new Vector3d(3, 0, 3))
            .Select(cell => cell.CellKey)
            .ToArray();
        int[] coveredCellKeys = GridTracer.GetCoveredScanCells(
                _world,
                new Vector3d(0, 0, 0),
                new Vector3d(5, 0, 5))
            .Select(cell => cell.CellKey)
            .ToArray();

        Assert.Empty(missingCellKeys);
        Assert.Equal(new[] { 0, 8 }, coveredCellKeys);
    }

    [Fact]
    public void TraceLine_ShouldNotIncludeEndWhenSpecified()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d start = new(-5, 0, -5);
        Vector3d end = new(5, 0, 5);

        List<Voxel> tracedVoxels = new();

        foreach (var gridVoxelSet in GridTracer.TraceLine(_world, start, end, includeEnd: false))
        {
            if (gridVoxelSet.Grid.GridIndex == gridIndex)
                tracedVoxels.AddRange(gridVoxelSet.Voxels);
        }

        Assert.NotEmpty(tracedVoxels);

        grid.TryGetVoxel(end, out Voxel endVoxel);

        // Ensure that the last voxel is not the end voxel
        Assert.NotEqual(endVoxel, tracedVoxels.Last());
    }

    [Fact]
    public void TraceLine2D_ShouldReturnCorrectVoxels()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector2d start = new(-5, -5);
        Vector2d end = new(5, 5);

        List<Voxel> tracedVoxels = new();

        foreach (var gridVoxelSet in GridTracer.TraceLine(_world, start, end, includeEnd: true))
        {
            if (gridVoxelSet.Grid.GridIndex == gridIndex)
                tracedVoxels.AddRange(gridVoxelSet.Voxels);
        }

        Assert.NotEmpty(tracedVoxels);

        grid.TryGetVoxel(start.ToVector3d(Fixed64.Zero), out Voxel startVoxel);
        grid.TryGetVoxel(end.ToVector3d(Fixed64.Zero), out Voxel endVoxel);

        // Ensure the start and end voxels are included
        Assert.Equal(startVoxel.WorldIndex, tracedVoxels.First().WorldIndex);
        Assert.Equal(endVoxel.WorldIndex, tracedVoxels.Last().WorldIndex);
    }

    [Fact]
    public void TraceLine2D_ShouldMatchEquivalentVector3dTraceOnExplicitLayer()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(8, 2, 8)),
            out _));
        Vector2d start = Vector2d.FromDouble(1.2, 1.2);
        Vector2d end = Vector2d.FromDouble(6.8, 4.8);
        Fixed64 layerY = (Fixed64)2;

        WorldVoxelIndex[] expected = CopyCoveredVoxelIndices(GridTracer.TraceLine(
            _world,
            GridPlane2d.ToWorld(start, layerY),
            GridPlane2d.ToWorld(end, layerY),
            includeEnd: true));
        WorldVoxelIndex[] actual = CopyCoveredVoxelIndices(GridTracer.TraceLine(
            _world,
            start,
            end,
            includeEnd: true,
            layerY: layerY));

        Assert.Equal(expected, actual);
        Assert.All(actual, index => Assert.Equal(2, index.VoxelIndex.y));
    }

    [Fact]
    public void TraceLine2D_ShouldPreservePositionalPaddingAndIncludeEndMeaning()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(8, 0, 8)),
            out _));
        Vector2d start = new(1, 1);
        Vector2d end = new(5, 1);
        Fixed64 padding = Fixed64.One;

        WorldVoxelIndex[] expected = CopyCoveredVoxelIndices(GridTracer.TraceLine(
            _world,
            GridPlane2d.ToWorld(start),
            GridPlane2d.ToWorld(end),
            padding,
            includeEnd: false));
        WorldVoxelIndex[] actual = CopyCoveredVoxelIndices(GridTracer.TraceLine(
            _world,
            start,
            end,
            padding,
            includeEnd: false));
        WorldVoxelIndex[] unpadded = CopyCoveredVoxelIndices(GridTracer.TraceLine(
            _world,
            GridPlane2d.ToWorld(start),
            GridPlane2d.ToWorld(end),
            includeEnd: false));

        Assert.Equal(expected, actual);
        Assert.NotEqual(unpadded, actual);
    }

    [Fact]
    public void TraceLine_ShouldFullyCoverAllVoxelsBetweenTwoPoints()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d start = new(-5, 0, -5);
        Vector3d end = new(5, 0, 5);

        var tracedVoxels = GridTracer.TraceLine(_world, start, end, includeEnd: true)
            .SelectMany(set => set.Voxels).ToList();

        Assert.NotEmpty(tracedVoxels);
        Assert.Contains(tracedVoxels, voxel => voxel.WorldPosition == start);
        Assert.Contains(tracedVoxels, voxel => voxel.WorldPosition == end);
    }

    [Fact]
    public void TraceLine_ShouldBeStableForEquivalentFractionalEndpoints()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10)),
            out _));

        Vector3d[] firstTrace = GridTracer.TraceLine(
                _world,
                Vector3d.FromDouble(1.01, 0, 1.01),
                Vector3d.FromDouble(5.01, 0, 5.01),
                includeEnd: true)
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.WorldPosition)
            .ToArray();

        Vector3d[] secondTrace = GridTracer.TraceLine(
                _world,
                Vector3d.FromDouble(1.99, 0, 1.99),
                Vector3d.FromDouble(5.49, 0, 5.49),
                includeEnd: true)
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.WorldPosition)
            .ToArray();

        Assert.Equal(firstTrace, secondTrace);
    }

    [Fact]
    public void TraceLine_ShouldRespectDescendingDirectionAfterSnapping()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d start = Vector3d.FromDouble(5.8, 0, 5.8);
        Vector3d end = Vector3d.FromDouble(-5.2, 0, -5.2);

        Vector3d[] tracedPositions = GridTracer.TraceLine(_world, start, end, includeEnd: true)
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.WorldPosition)
            .ToArray();

        Assert.NotEmpty(tracedPositions);
        Assert.Contains(new Vector3d(0, 0, 0), tracedPositions);
        Assert.True(grid.TryGetVoxel(start, out Voxel startVoxel));
        Assert.True(grid.TryGetVoxel(end, out Voxel endVoxel));
        Assert.Equal(startVoxel.WorldPosition, tracedPositions.First());
        Assert.Equal(endVoxel.WorldPosition, tracedPositions.Last());
    }

    [Fact]
    public void Blocker_ShouldOnlyAffectSnappedBounds()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-50, 0, -50), new Vector3d(50, 0, 50)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        FixedBoundArea boundingArea = new(Vector3d.FromDouble(-5.3, 0, -5.3), Vector3d.FromDouble(5.8, 0, 5.8));
        var blocker = new BoundsBlocker(_world, boundingArea);
        blocker.ApplyBlockage();

        Vector3d snappedMin = grid.FloorToGrid(boundingArea.Min);
        Vector3d snappedMax = grid.CeilToGrid(boundingArea.Max);

        foreach (var coveredVoxels in GridTracer.GetCoveredVoxels(_world, boundingArea.Min, boundingArea.Max))
        {
            foreach (var voxel in coveredVoxels.Voxels)
            {
                Assert.True(voxel.WorldPosition.X >= snappedMin.X
                    && voxel.WorldPosition.X <= snappedMax.X, "Voxel X coordinate is out of bounds");
                Assert.True(voxel.WorldPosition.Y >= snappedMin.Y
                    && voxel.WorldPosition.Y <= snappedMax.Y, "Voxel Y coordinate is out of bounds");
                Assert.True(voxel.WorldPosition.Z >= snappedMin.Z
                    && voxel.WorldPosition.Z <= snappedMax.Z, "Voxel Z coordinate is out of bounds");
            }
        }
    }

    [Fact]
    public void GetCoveredVoxels2D_ShouldMatchEquivalentVector3dBounds()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(6, 2, 6)),
            out _));
        Vector2d boundsMin = new(1, 1);
        Vector2d boundsMax = new(3, 4);
        Fixed64 layerY = (Fixed64)2;

        WorldVoxelIndex[] expected = CopyCoveredVoxelIndices(GridTracer.GetCoveredVoxels(
            _world,
            GridPlane2d.ToWorld(boundsMin, layerY),
            GridPlane2d.ToWorld(boundsMax, layerY)));
        WorldVoxelIndex[] actual = CopyCoveredVoxelIndices(GridTracer.GetCoveredVoxels(
            _world,
            boundsMin,
            boundsMax,
            layerY));

        Assert.Equal(expected, actual);
        Assert.All(actual, index => Assert.Equal(2, index.VoxelIndex.y));
    }

    [Fact]
    public void GetCoveredVoxels2D_ShouldHandleDescendingBoundsPaddingAndMultiGridCoverage()
    {
        ResetWorld(spatialGridCellSize: 4);

        try
        {
            Assert.True(_world.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3)),
                out ushort firstGridIndex));
            Assert.True(_world.TryAddGrid(
                new GridConfiguration(new Vector3d(4, 0, 0), new Vector3d(7, 0, 3)),
                out ushort secondGridIndex));
            Vector2d boundsMin = Vector2d.FromDouble(4.2, 2.2);
            Vector2d boundsMax = Vector2d.FromDouble(2.8, 0.8);
            Fixed64 padding = Fixed64.One;

            WorldVoxelIndex[] expected = CopyCoveredVoxelIndices(GridTracer.GetCoveredVoxels(
                _world,
                GridPlane2d.ToWorld(boundsMin),
                GridPlane2d.ToWorld(boundsMax),
                padding));
            WorldVoxelIndex[] actual = CopyCoveredVoxelIndices(GridTracer.GetCoveredVoxels(
                _world,
                boundsMin,
                boundsMax,
                padding: padding));

            Assert.Equal(expected, actual);
            Assert.Contains(actual, index => index.GridIndex == firstGridIndex);
            Assert.Contains(actual, index => index.GridIndex == secondGridIndex);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetCoveredScanCells_ShouldUsePaddedSnappedBoundsForSpatialHashLookup()
    {
        ResetWorld(spatialGridCellSize: 10);

        try
        {
            Assert.True(_world.TryAddGrid(
                new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(19, 0, 9), scanCellSize: 2),
                out _));

            ScanCell[] coveredScanCells = GridTracer.GetCoveredScanCells(
                _world,
                Vector3d.FromDouble(9.6, 0, 0),
                Vector3d.FromDouble(9.6, 0, 0),
                padding: Fixed64.One).ToArray();

            Assert.NotEmpty(coveredScanCells);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetCoveredScanCells2D_ShouldMatchEnumerableAndCallerOwnedVector3dPaths()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(8, 2, 8), scanCellSize: 2),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetScanCell(new Vector3d(0, 0, 0), out ScanCell staleCell));
        Vector2d boundsMin = new(1, 1);
        Vector2d boundsMax = new(5, 5);
        Fixed64 layerY = (Fixed64)2;
        SwiftList<ScanCell> results = new();
        GridScanScratch scratch = new();

        (ushort GridIndex, int CellKey)[] expected = CopyCoveredScanCells(GridTracer.GetCoveredScanCells(
            _world,
            GridPlane2d.ToWorld(boundsMin, layerY),
            GridPlane2d.ToWorld(boundsMax, layerY)));
        (ushort GridIndex, int CellKey)[] actual = CopyCoveredScanCells(GridTracer.GetCoveredScanCells(
            _world,
            boundsMin,
            boundsMax,
            layerY));

        Assert.Equal(expected, actual);

        results.Add(staleCell);
        GridTracer.GetCoveredScanCellsInto(_world, boundsMin, boundsMax, results, layerY);
        Assert.Equal(expected, CopyCoveredScanCells(results));
        Assert.DoesNotContain(staleCell, results);

        results.Add(staleCell);
        GridTracer.GetCoveredScanCellsInto(_world, boundsMin, boundsMax, results, scratch, layerY);
        Assert.Equal(expected, CopyCoveredScanCells(results));
        Assert.DoesNotContain(staleCell, results);
    }

    [Fact]
    public void GridTracerPublicEntryPoints_ShouldReturnEmptyForNullOrInactiveWorld()
    {
        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();

        Assert.Empty(GridTracer.TraceLine(null, Vector3d.Zero, Vector3d.Zero));
        Assert.Empty(GridTracer.TraceLine(inactiveWorld, Vector3d.Zero, Vector3d.Zero));
        Assert.Empty(GridTracer.GetCoveredVoxels(null, Vector3d.Zero, Vector3d.Zero));
        Assert.Empty(GridTracer.GetCoveredVoxels(inactiveWorld, Vector3d.Zero, Vector3d.Zero));
        Assert.Empty(GridTracer.GetCoveredScanCells(null, Vector3d.Zero, Vector3d.Zero));
        Assert.Empty(GridTracer.GetCoveredScanCells(inactiveWorld, Vector3d.Zero, Vector3d.Zero));
        Assert.Empty(GridTracer.GetCoveredVoxels(_world, Vector3d.Zero, Vector3d.Zero, Fixed64.Zero));
        Assert.Empty(GridTracer.GetCoveredScanCells(_world, Vector3d.Zero, Vector3d.Zero, Fixed64.Zero));
    }

    [Fact]
    public void GetCoveredScanCellsInto_ShouldValidateClearAndFillCallerOwnedResults()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(8, 0, 8), scanCellSize: 2),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetScanCell(new Vector3d(0, 0, 0), out ScanCell staleCell));
        SwiftList<ScanCell> results = new();
        GridScanScratch scratch = new();

        Assert.Throws<ArgumentNullException>(() => GridTracer.GetCoveredScanCellsInto(
            _world,
            Vector3d.Zero,
            Vector3d.Zero,
            (SwiftList<ScanCell>)null));
        Assert.Throws<ArgumentNullException>(() => GridTracer.GetCoveredScanCellsInto(
            _world,
            Vector3d.Zero,
            Vector3d.Zero,
            (SwiftList<ScanCell>)null,
            scratch));
        Assert.Throws<ArgumentNullException>(() => GridTracer.GetCoveredScanCellsInto(
            _world,
            Vector3d.Zero,
            Vector3d.Zero,
            results,
            (GridScanScratch)null));

        results.Add(staleCell);

        GridTracer.GetCoveredScanCellsInto(
            _world,
            new Vector3d(4, 0, 4),
            new Vector3d(8, 0, 8),
            results);

        Assert.NotEmpty(results);
        Assert.DoesNotContain(staleCell, results);

        results.Add(staleCell);

        GridTracer.GetCoveredScanCellsInto(
            _world,
            new Vector3d(4, 0, 4),
            new Vector3d(8, 0, 8),
            results,
            scratch);

        Assert.NotEmpty(results);
        Assert.DoesNotContain(staleCell, results);

        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();
        results.Add(staleCell);

        GridTracer.GetCoveredScanCellsInto(
            null,
            Vector3d.Zero,
            Vector3d.Zero,
            results);

        Assert.Empty(results);

        results.Add(staleCell);

        GridTracer.GetCoveredScanCellsInto(
            null,
            Vector3d.Zero,
            Vector3d.Zero,
            results,
            scratch);

        Assert.Empty(results);

        results.Add(staleCell);

        GridTracer.GetCoveredScanCellsInto(
            inactiveWorld,
            Vector3d.Zero,
            Vector3d.Zero,
            results);

        Assert.Empty(results);

        results.Add(staleCell);

        GridTracer.GetCoveredScanCellsInto(
            inactiveWorld,
            Vector3d.Zero,
            Vector3d.Zero,
            results,
            scratch);

        Assert.Empty(results);
    }

    [Fact]
    public void TraceLine_ShouldSkipEmptyCellsStaleEntriesAndDuplicateGridMembership()
    {
        ResetWorld(spatialGridCellSize: 10);

        try
        {
            Assert.True(_world.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(20, 0, 0)),
                out ushort gridIndex));
            AddStaleSpatialGridReference(new Vector3d(0, 0, 0), ushort.MaxValue);

            int tracedSetCount = 0;
            ushort tracedGridIndex = ushort.MaxValue;
            List<WorldVoxelIndex> tracedVoxelIndices = new();

            foreach (GridVoxelSet tracedSet in GridTracer.TraceLine(
                _world,
                new Vector3d(0, 0, 0),
                new Vector3d(35, 0, 0),
                includeEnd: false))
            {
                tracedSetCount++;
                tracedGridIndex = tracedSet.Grid.GridIndex;
                tracedVoxelIndices.AddRange(tracedSet.Voxels.Select(voxel => voxel.WorldIndex));
            }

            Assert.Equal(1, tracedSetCount);
            Assert.Equal(gridIndex, tracedGridIndex);
            Assert.Equal(
                tracedVoxelIndices.Distinct().Count(),
                tracedVoxelIndices.Count);
            Assert.Equal(21, tracedVoxelIndices.Count);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void TraceLine_ShouldPreservePerAxisDirectionWhenAxesDiffer()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(6, 2, 6)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d start = Vector3d.FromDouble(5.8, 0, 5.8);
        Vector3d end = Vector3d.FromDouble(1.2, 2, 1.2);

        Vector3d[] tracedPositions = GridTracer.TraceLine(_world, start, end, includeEnd: true)
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.WorldPosition)
            .ToArray();

        Assert.NotEmpty(tracedPositions);
        Assert.True(grid.TryGetVoxel(start, out Voxel startVoxel));
        Assert.True(grid.TryGetVoxel(end, out Voxel endVoxel));
        Assert.Equal(startVoxel.WorldPosition, tracedPositions.First());
        Assert.Equal(endVoxel.WorldPosition, tracedPositions.Last());
        Assert.Contains(new Vector3d(3, 1, 3), tracedPositions);
    }

    [Fact]
    public void TraceLine_ShouldPreserveDescendingYDirectionAfterSnapping()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(6, 2, 6)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d start = Vector3d.FromDouble(1.2, 2, 1.2);
        Vector3d end = Vector3d.FromDouble(5.8, 0, 5.8);

        Vector3d[] tracedPositions = GridTracer.TraceLine(_world, start, end, includeEnd: true)
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.WorldPosition)
            .ToArray();

        Assert.NotEmpty(tracedPositions);
        Assert.True(grid.TryGetVoxel(start, out Voxel startVoxel));
        Assert.True(grid.TryGetVoxel(end, out Voxel endVoxel));
        Assert.Equal(startVoxel.WorldPosition, tracedPositions.First());
        Assert.Equal(endVoxel.WorldPosition, tracedPositions.Last());
        Assert.Contains(new Vector3d(3, 1, 3), tracedPositions);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void TraceLine_ShouldUseHexAxialInterpolation(HexOrientation orientation)
    {
        ResetWorld(spatialGridCellSize: 64);

        try
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
                new Fixed64(2),
                Fixed64.One,
                orientation);
            GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(3, 0, 0));
            VoxelIndex[] expected =
            {
                new(0, 0, 0),
                new(1, 0, 0),
                new(2, 0, 0),
                new(3, 0, 0)
            };

            Assert.True(_world.TryAddGrid(configuration, out _));

            Vector3d start = configuration.BoundsMin;
            Vector3d end = configuration.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(expected[^1], metrics);

            VoxelIndex[] tracedIndices = GridTracer.TraceLine(_world, start, end, includeEnd: true)
                .SelectMany(set => set.Voxels)
                .Select(voxel => voxel.Index)
                .ToArray();

            Assert.Equal(expected, tracedIndices);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void TraceLine_ShouldReturnSingleHexVoxelWhenEndpointsMatch()
    {
        ResetWorld(spatialGridCellSize: 64);

        try
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
                new Fixed64(2),
                Fixed64.One,
                HexOrientation.PointyTop);
            GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1));

            Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));

            VoxelGrid grid = _world.ActiveGrids[gridIndex];
            Vector3d start = grid.BoundsMin;
            VoxelIndex[] tracedIndices = GridTracer.TraceLine(_world, start, start, includeEnd: false)
                .SelectMany(set => set.Voxels)
                .Select(voxel => voxel.Index)
                .ToArray();

            Assert.Equal(new[] { new VoxelIndex(0, 0, 0) }, tracedIndices);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void HexCoverageQueries_ShouldSkipCandidateGridWhenBoundsDoNotOverlap()
    {
        ResetWorld(spatialGridCellSize: 1024);

        try
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
                new Fixed64(2),
                Fixed64.One,
                HexOrientation.PointyTop);
            GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1), scanCellSize: 1);

            Assert.True(_world.TryAddGrid(configuration, out _));

            Vector3d queryMin = new Vector3d(100, 0, 100);
            Vector3d queryMax = new Vector3d(101, 0, 101);

            Assert.Empty(GridTracer.GetCoveredVoxels(_world, queryMin, queryMax).ToArray());
            Assert.Empty(GridTracer.GetCoveredScanCells(_world, queryMin, queryMax).ToArray());
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetCoveredScanCells_ShouldSkipRectangularCandidateGridWhenBoundsDoNotOverlap()
    {
        ResetWorld(spatialGridCellSize: 1024);

        try
        {
            Assert.True(_world.TryAddGrid(
                new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 1), scanCellSize: 1),
                out _));

            ScanCell[] scanCells = GridTracer.GetCoveredScanCells(
                    _world,
                    new Vector3d(100, 0, 100),
                    new Vector3d(101, 0, 101))
                .ToArray();

            Assert.Empty(scanCells);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetCoveredVoxels_ShouldUseConservativeHexCoverageWhenAabbCornerIsOutsideHexFootprint()
    {
        ResetWorld(spatialGridCellSize: 64);

        try
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
                new Fixed64(2),
                Fixed64.One,
                HexOrientation.PointyTop);
            GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1));
            VoxelIndex expectedOrigin = new(0, 0, 0);
            VoxelIndex expectedEast = new(1, 0, 0);

            Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));

            VoxelGrid grid = _world.ActiveGrids[gridIndex];
            Vector3d outsideHexFootprint = new(grid.BoundsMax.X, grid.BoundsMin.Y, grid.BoundsMin.Z);
            VoxelIndex[] coveredIndices = GridTracer.GetCoveredVoxels(_world, grid.BoundsMin, outsideHexFootprint)
                .SelectMany(set => set.Voxels)
                .Select(voxel => voxel.Index)
                .ToArray();

            Assert.Contains(expectedOrigin, coveredIndices);
            Assert.Contains(expectedEast, coveredIndices);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetCoveredVoxels_ShouldReturnConfiguredSparseHexVoxelsOnly()
    {
        ResetWorld(spatialGridCellSize: 64);

        try
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
                new Fixed64(2),
                Fixed64.One,
                HexOrientation.PointyTop);
            GridConfiguration configuration = CreateSparseHexConfiguration(metrics, new VoxelIndex(1, 0, 1), scanCellSize: 1);
            VoxelIndex originIndex = new(0, 0, 0);
            VoxelIndex eastIndex = new(1, 0, 0);
            VoxelIndex missingIndex = new(1, 0, 1);

            Assert.True(_world.TryAddGrid(configuration, new[] { originIndex, eastIndex }, out ushort gridIndex));

            VoxelGrid grid = _world.ActiveGrids[gridIndex];
            Vector3d outsideHexFootprint = new(grid.BoundsMax.X, grid.BoundsMin.Y, grid.BoundsMin.Z);
            VoxelIndex[] coveredIndices = GridTracer.GetCoveredVoxels(_world, grid.BoundsMin, outsideHexFootprint)
                .SelectMany(set => set.Voxels)
                .Select(voxel => voxel.Index)
                .ToArray();
            int[] coveredCellKeys = GridTracer.GetCoveredScanCells(_world, grid.BoundsMin, outsideHexFootprint)
                .Select(cell => cell.CellKey)
                .ToArray();

            Assert.Equal(new[] { originIndex, eastIndex }, coveredIndices);
            Assert.Contains(grid.GetScanCellKey(originIndex), coveredCellKeys);
            Assert.Contains(grid.GetScanCellKey(eastIndex), coveredCellKeys);
            Assert.DoesNotContain(grid.GetScanCellKey(missingIndex), coveredCellKeys);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetCoveredScanCells_ShouldUseConservativeHexCoverageWhenAabbCornerIsOutsideHexFootprint()
    {
        ResetWorld(spatialGridCellSize: 64);

        try
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
                new Fixed64(2),
                Fixed64.One,
                HexOrientation.PointyTop);
            GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1), scanCellSize: 1);

            Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));

            VoxelGrid grid = _world.ActiveGrids[gridIndex];
            Vector3d outsideHexFootprint = new(grid.BoundsMax.X, grid.BoundsMin.Y, grid.BoundsMin.Z);
            int[] coveredCellKeys = GridTracer.GetCoveredScanCells(_world, grid.BoundsMin, outsideHexFootprint)
                .Select(cell => cell.CellKey)
                .ToArray();

            Assert.Contains(grid.GetScanCellKey(new VoxelIndex(0, 0, 0)), coveredCellKeys);
            Assert.Contains(grid.GetScanCellKey(new VoxelIndex(1, 0, 0)), coveredCellKeys);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetCoveredVoxels_ShouldIgnoreStaleGridReferences()
    {
        ResetWorld(spatialGridCellSize: 10);

        try
        {
            Assert.True(_world.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(20, 0, 0)),
                out ushort gridIndex));
            AddStaleSpatialGridReference(new Vector3d(0, 0, 0), ushort.MaxValue);

            int coveredSetCount = 0;
            ushort coveredGridIndex = ushort.MaxValue;
            List<WorldVoxelIndex> coveredVoxelIndices = new();

            foreach (GridVoxelSet coveredSet in GridTracer.GetCoveredVoxels(
                _world,
                new Vector3d(0, 0, 0),
                new Vector3d(20, 0, 0)))
            {
                coveredSetCount++;
                coveredGridIndex = coveredSet.Grid.GridIndex;
                coveredVoxelIndices.AddRange(coveredSet.Voxels.Select(voxel => voxel.WorldIndex));
            }

            Assert.Equal(1, coveredSetCount);
            Assert.Equal(gridIndex, coveredGridIndex);
            Assert.Equal(21, coveredVoxelIndices.Count);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetCoveredScanCells_ShouldIgnoreStaleAndDuplicateGridMembership()
    {
        ResetWorld(spatialGridCellSize: 10);

        try
        {
            Assert.True(_world.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(20, 0, 0), scanCellSize: 4),
                out _));
            AddStaleSpatialGridReference(new Vector3d(0, 0, 0), ushort.MaxValue);

            ScanCell[] coveredScanCells = GridTracer.GetCoveredScanCells(
                _world,
                new Vector3d(0, 0, 0),
                new Vector3d(20, 0, 0)).ToArray();

            Assert.NotEmpty(coveredScanCells);
            Assert.Equal(
                coveredScanCells.Select(cell => cell.CellKey).Distinct().Count(),
                coveredScanCells.Length);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void TraceLine_PrivateIndexAppend_ShouldSkipMissingAndDuplicateHexVoxels()
    {
        ResetWorld(spatialGridCellSize: 64);

        try
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
                new Fixed64(2),
                Fixed64.One,
                HexOrientation.PointyTop);
            VoxelIndex originIndex = new(0, 0, 0);
            VoxelIndex missingIndex = new(1, 0, 0);
            GridConfiguration configuration = CreateSparseHexConfiguration(metrics, new VoxelIndex(1, 0, 0));

            Assert.True(_world.TryAddGrid(configuration, new[] { originIndex }, out ushort gridIndex));

            VoxelGrid grid = _world.ActiveGrids[gridIndex];
            SwiftList<Voxel> voxels = new();
            SwiftHashSet<Voxel> redundancy = new();

            InvokeAddTraceVoxelByIndex(grid, missingIndex, voxels, redundancy);
            Assert.Empty(voxels);

            InvokeAddTraceVoxelByIndex(grid, originIndex, voxels, redundancy);
            InvokeAddTraceVoxelByIndex(grid, originIndex, voxels, redundancy);

            Assert.Single(voxels);
            Assert.Equal(originIndex, voxels[0].Index);
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void HexCoveragePredicate_ShouldRejectCentersOutsideEachHorizontalBoundary()
    {
        ResetWorld(spatialGridCellSize: 64);

        try
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
                new Fixed64(2),
                Fixed64.One,
                HexOrientation.PointyTop);
            GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1));

            Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));

            VoxelGrid grid = _world.ActiveGrids[gridIndex];
            Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));

            Vector3d center = voxel.WorldPosition;

            Assert.True(InvokeIsHexVoxelCenterInHorizontalCoverage(
                voxel,
                center.X,
                center.X,
                center.Z,
                center.Z));
            Assert.False(InvokeIsHexVoxelCenterInHorizontalCoverage(
                voxel,
                center.X + Fixed64.One,
                center.X + Fixed64.One,
                center.Z,
                center.Z));
            Assert.False(InvokeIsHexVoxelCenterInHorizontalCoverage(
                voxel,
                center.X - Fixed64.One,
                center.X - Fixed64.One,
                center.Z,
                center.Z));
            Assert.False(InvokeIsHexVoxelCenterInHorizontalCoverage(
                voxel,
                center.X,
                center.X,
                center.Z + Fixed64.One,
                center.Z + Fixed64.One));
            Assert.False(InvokeIsHexVoxelCenterInHorizontalCoverage(
                voxel,
                center.X,
                center.X,
                center.Z - Fixed64.One,
                center.Z - Fixed64.One));
        }
        finally
        {
            ResetWorld();
        }
    }

    private void AddStaleSpatialGridReference(Vector3d position, ushort staleIndex)
    {
        int cellIndex = _world.GetSpatialGridKey(position);
        _world.SpatialGridHash[cellIndex].Add(staleIndex);
    }

    private void ResetWorld(int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        _world.Dispose();
        _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize);
    }

    private static WorldVoxelIndex[] CopyCoveredVoxelIndices(IEnumerable<GridVoxelSet> coveredSets)
    {
        List<WorldVoxelIndex> indices = new();

        foreach (GridVoxelSet coveredSet in coveredSets)
        {
            foreach (Voxel voxel in coveredSet.Voxels)
                indices.Add(voxel.WorldIndex);
        }

        return indices.ToArray();
    }

    private static (ushort GridIndex, int CellKey)[] CopyCoveredScanCells(IEnumerable<ScanCell> scanCells)
    {
        List<(ushort GridIndex, int CellKey)> cells = new();

        foreach (ScanCell scanCell in scanCells)
            cells.Add((scanCell.GridIndex, scanCell.CellKey));

        return cells.ToArray();
    }

    private static void InvokeAddTraceVoxelByIndex(
        VoxelGrid grid,
        VoxelIndex index,
        SwiftList<Voxel> voxels,
        SwiftHashSet<Voxel> redundancy)
    {
        MethodInfo method = typeof(GridTracer).GetMethod(
            "AddTraceVoxelByIndex",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find GridTracer.AddTraceVoxelByIndex.");

        method.Invoke(null, new object[] { grid, index, voxels, redundancy });
    }

    private static bool InvokeIsHexVoxelCenterInHorizontalCoverage(
        Voxel voxel,
        Fixed64 coverageMinX,
        Fixed64 coverageMaxX,
        Fixed64 coverageMinZ,
        Fixed64 coverageMaxZ)
    {
        MethodInfo method = typeof(GridTracer).GetMethod(
            "IsHexVoxelCenterInHorizontalCoverage",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find GridTracer.IsHexVoxelCenterInHorizontalCoverage.");

        return (bool)method.Invoke(
            null,
            new object[] { voxel, coverageMinX, coverageMaxX, coverageMinZ, coverageMaxZ });
    }

    private static GridConfiguration CreateSparseConfig(
        Vector3d min,
        Vector3d max,
        int scanCellSize = GridConfiguration.DefaultScanCellSize)
    {
        return new GridConfiguration(
            min,
            max,
            scanCellSize,
            storageKind: GridStorageKind.Sparse);
    }

    private static GridConfiguration CreateSparseHexConfiguration(
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        int scanCellSize = GridConfiguration.DefaultScanCellSize)
    {
        Vector3d boundsMax = HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            Vector3d.Zero,
            boundsMax,
            scanCellSize,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
    }

    private static GridConfiguration CreateHexConfiguration(
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        int scanCellSize = GridConfiguration.DefaultScanCellSize) =>
        CreateHexConfiguration(Vector3d.Zero, metrics, maxIndex, scanCellSize);

    private static GridConfiguration CreateHexConfiguration(
        Vector3d boundsMin,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        int scanCellSize = GridConfiguration.DefaultScanCellSize)
    {
        Vector3d boundsMax = boundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            boundsMin,
            boundsMax,
            scanCellSize,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);
    }
}
