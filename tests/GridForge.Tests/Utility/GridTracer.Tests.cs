using FixedMathSharp;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Spatial;
using GridForge.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
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

        Vector3d start = new(5, 0.5, 5);
        Vector3d end = new(45.28, 1, 18.31);

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
                new Vector3d(1.01, 0, 1.01),
                new Vector3d(5.01, 0, 5.01),
                includeEnd: true)
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.WorldPosition)
            .ToArray();

        Vector3d[] secondTrace = GridTracer.TraceLine(
                _world,
                new Vector3d(1.99, 0, 1.99),
                new Vector3d(5.49, 0, 5.49),
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

        Vector3d start = new(5.8, 0, 5.8);
        Vector3d end = new(-5.2, 0, -5.2);

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

        BoundingArea boundingArea = new(new Vector3d(-5.3, 0, -5.3), new Vector3d(5.8, 0, 5.8));
        var blocker = new BoundsBlocker(_world, boundingArea);
        blocker.ApplyBlockage();

        Vector3d snappedMin = _world.FloorToVoxelSize(boundingArea.Min);
        Vector3d snappedMax = _world.CeilToVoxelSize(boundingArea.Max);

        foreach (var coveredVoxels in GridTracer.GetCoveredVoxels(_world, boundingArea.Min, boundingArea.Max))
        {
            foreach (var voxel in coveredVoxels.Voxels)
            {
                Assert.True(voxel.WorldPosition.x >= snappedMin.x
                    && voxel.WorldPosition.x <= snappedMax.x, "Voxel X coordinate is out of bounds");
                Assert.True(voxel.WorldPosition.y >= snappedMin.y
                    && voxel.WorldPosition.y <= snappedMax.y, "Voxel Y coordinate is out of bounds");
                Assert.True(voxel.WorldPosition.z >= snappedMin.z
                    && voxel.WorldPosition.z <= snappedMax.z, "Voxel Z coordinate is out of bounds");
            }
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
                new Vector3d(9.6, 0, 0),
                new Vector3d(9.6, 0, 0),
                padding: Fixed64.One).ToArray();

            Assert.NotEmpty(coveredScanCells);
        }
        finally
        {
            ResetWorld();
        }
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

        Vector3d start = new(5.8, 0, 5.8);
        Vector3d end = new(1.2, 2, 1.2);

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

        Vector3d start = new(1.2, 2, 1.2);
        Vector3d end = new(5.8, 0, 5.8);

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

    private void AddStaleSpatialGridReference(Vector3d position, ushort staleIndex)
    {
        int cellIndex = _world.GetSpatialGridKey(position);
        _world.SpatialGridHash[cellIndex].Add(staleIndex);
    }

    private void ResetWorld(
        Fixed64? voxelSize = null,
        int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        _world.Dispose();
        _world = GridWorldTestFactory.CreateWorld(voxelSize, spatialGridCellSize);
    }
}
