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
    public GridTracerTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        GlobalGridManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TraceLine_ShouldReturnCorrectVoxels()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-50, -1, -50), new Vector3d(50, 1, 50)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d start = new Vector3d(5, 0.5, 5);
        Vector3d end = new Vector3d(45.28, 1, 18.31);

        List<Voxel> tracedVoxels = new List<Voxel>();

        foreach (var gridVoxelSet in GridTracer.TraceLine(start, end, includeEnd: true))
        {
            if (gridVoxelSet.Grid.GlobalIndex == gridIndex)
                tracedVoxels.AddRange(gridVoxelSet.Voxels);
        }

        Assert.NotEmpty(tracedVoxels);

        grid.TryGetVoxel(start, out Voxel startVoxel);
        grid.TryGetVoxel(end, out Voxel endVoxel);

        // Ensure that the first and last voxel correspond to the start and end positions
        Assert.Equal(startVoxel.GlobalIndex, tracedVoxels.First().GlobalIndex);
        Assert.Equal(endVoxel.GlobalIndex, tracedVoxels.Last().GlobalIndex);
    }

    [Fact]
    public void TraceLine_ShouldNotIncludeEndWhenSpecified()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d start = new Vector3d(-5, 0, -5);
        Vector3d end = new Vector3d(5, 0, 5);

        List<Voxel> tracedVoxels = new List<Voxel>();

        foreach (var gridVoxelSet in GridTracer.TraceLine(start, end, includeEnd: false))
        {
            if (gridVoxelSet.Grid.GlobalIndex == gridIndex)
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
        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector2d start = new Vector2d(-5, -5);
        Vector2d end = new Vector2d(5, 5);

        List<Voxel> tracedVoxels = new List<Voxel>();

        foreach (var gridVoxelSet in GridTracer.TraceLine(start, end, includeEnd: true))
        {
            if (gridVoxelSet.Grid.GlobalIndex == gridIndex)
                tracedVoxels.AddRange(gridVoxelSet.Voxels);
        }

        Assert.NotEmpty(tracedVoxels);

        grid.TryGetVoxel(start.ToVector3d(Fixed64.Zero), out Voxel startVoxel);
        grid.TryGetVoxel(end.ToVector3d(Fixed64.Zero), out Voxel endVoxel);

        // Ensure the start and end voxels are included
        Assert.Equal(startVoxel.GlobalIndex, tracedVoxels.First().GlobalIndex);
        Assert.Equal(endVoxel.GlobalIndex, tracedVoxels.Last().GlobalIndex);
    }

    [Fact]
    public void TraceLine_ShouldFullyCoverAllVoxelsBetweenTwoPoints()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d start = new Vector3d(-5, 0, -5);
        Vector3d end = new Vector3d(5, 0, 5);

        var tracedVoxels = GridTracer.TraceLine(start, end, includeEnd: true)
            .SelectMany(set => set.Voxels).ToList();

        Assert.NotEmpty(tracedVoxels);
        Assert.Contains(tracedVoxels, voxel => voxel.WorldPosition == start);
        Assert.Contains(tracedVoxels, voxel => voxel.WorldPosition == end);
    }

    [Fact]
    public void TraceLine_ShouldBeStableForEquivalentFractionalEndpoints()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10)),
            out _));

        Vector3d[] firstTrace = GridTracer.TraceLine(
                new Vector3d(1.01, 0, 1.01),
                new Vector3d(5.01, 0, 5.01),
                includeEnd: true)
            .SelectMany(set => set.Voxels)
            .Select(voxel => voxel.WorldPosition)
            .ToArray();

        Vector3d[] secondTrace = GridTracer.TraceLine(
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
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d start = new Vector3d(5.8, 0, 5.8);
        Vector3d end = new Vector3d(-5.2, 0, -5.2);

        Vector3d[] tracedPositions = GridTracer.TraceLine(start, end, includeEnd: true)
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
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-50, 0, -50), new Vector3d(50, 0, 50)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        BoundingArea boundingArea = new BoundingArea(new Vector3d(-5.3, 0, -5.3), new Vector3d(5.8, 0, 5.8));
        var blocker = new BoundsBlocker(boundingArea);
        blocker.ApplyBlockage();

        Vector3d snappedMin = GlobalGridManager.FloorToVoxelSize(boundingArea.Min);
        Vector3d snappedMax = GlobalGridManager.CeilToVoxelSize(boundingArea.Max);

        foreach (var coveredVoxels in GridTracer.GetCoveredVoxels(boundingArea.Min, boundingArea.Max))
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
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup(spatialGridCellSize: 10);

        try
        {
            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(19, 0, 9), scanCellSize: 2),
                out _));

            ScanCell[] coveredScanCells = GridTracer.GetCoveredScanCells(
                new Vector3d(9.6, 0, 0),
                new Vector3d(9.6, 0, 0),
                padding: Fixed64.One).ToArray();

            Assert.NotEmpty(coveredScanCells);
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void TraceLine_ShouldSkipEmptyCellsStaleEntriesAndDuplicateGridMembership()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup(spatialGridCellSize: 10);

        try
        {
            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(20, 0, 0)),
                out ushort gridIndex));
            AddStaleSpatialGridReference(new Vector3d(0, 0, 0), ushort.MaxValue);

            int tracedSetCount = 0;
            ushort tracedGridIndex = ushort.MaxValue;
            List<GlobalVoxelIndex> tracedVoxelIndices = new List<GlobalVoxelIndex>();

            foreach (GridVoxelSet tracedSet in GridTracer.TraceLine(
                new Vector3d(0, 0, 0),
                new Vector3d(35, 0, 0),
                includeEnd: false))
            {
                tracedSetCount++;
                tracedGridIndex = tracedSet.Grid.GlobalIndex;
                tracedVoxelIndices.AddRange(tracedSet.Voxels.Select(voxel => voxel.GlobalIndex));
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
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void TraceLine_ShouldPreservePerAxisDirectionWhenAxesDiffer()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(6, 2, 6)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d start = new Vector3d(5.8, 0, 5.8);
        Vector3d end = new Vector3d(1.2, 2, 1.2);

        Vector3d[] tracedPositions = GridTracer.TraceLine(start, end, includeEnd: true)
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
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(6, 2, 6)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d start = new Vector3d(1.2, 2, 1.2);
        Vector3d end = new Vector3d(5.8, 0, 5.8);

        Vector3d[] tracedPositions = GridTracer.TraceLine(start, end, includeEnd: true)
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
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup(spatialGridCellSize: 10);

        try
        {
            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(20, 0, 0)),
                out ushort gridIndex));
            AddStaleSpatialGridReference(new Vector3d(0, 0, 0), ushort.MaxValue);

            int coveredSetCount = 0;
            ushort coveredGridIndex = ushort.MaxValue;
            List<GlobalVoxelIndex> coveredVoxelIndices = new List<GlobalVoxelIndex>();

            foreach (GridVoxelSet coveredSet in GridTracer.GetCoveredVoxels(
                new Vector3d(0, 0, 0),
                new Vector3d(20, 0, 0)))
            {
                coveredSetCount++;
                coveredGridIndex = coveredSet.Grid.GlobalIndex;
                coveredVoxelIndices.AddRange(coveredSet.Voxels.Select(voxel => voxel.GlobalIndex));
            }

            Assert.Equal(1, coveredSetCount);
            Assert.Equal(gridIndex, coveredGridIndex);
            Assert.Equal(21, coveredVoxelIndices.Count);
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void GetCoveredScanCells_ShouldIgnoreStaleAndDuplicateGridMembership()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup(spatialGridCellSize: 10);

        try
        {
            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(20, 0, 0), scanCellSize: 4),
                out _));
            AddStaleSpatialGridReference(new Vector3d(0, 0, 0), ushort.MaxValue);

            ScanCell[] coveredScanCells = GridTracer.GetCoveredScanCells(
                new Vector3d(0, 0, 0),
                new Vector3d(20, 0, 0)).ToArray();

            Assert.NotEmpty(coveredScanCells);
            Assert.Equal(
                coveredScanCells.Select(cell => cell.CellKey).Distinct().Count(),
                coveredScanCells.Length);
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    private static void AddStaleSpatialGridReference(Vector3d position, ushort staleIndex)
    {
        int cellIndex = GlobalGridManager.GetSpatialGridKey(position);
        GlobalGridManager.SpatialGridHash[cellIndex].Add(staleIndex);
    }
}
