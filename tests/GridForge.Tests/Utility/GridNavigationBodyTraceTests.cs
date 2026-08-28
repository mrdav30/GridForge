using System;
using System.Linq;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridNavigationBodyTraceTests : IDisposable
{
    private readonly GridWorld _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RectangularTwoAxisTrace_ShouldRetainMissingFourCellClosureAndRejectUnion()
    {
        GridConfiguration configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(1, 0, 1),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            configuration,
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(1, 0, 1)
            },
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex source = new WorldVoxelIndex(
            _world.SpawnToken,
            gridIndex,
            grid.SpawnToken,
            new VoxelIndex(0, 0, 0));
        WorldVoxelIndex target = new WorldVoxelIndex(
            _world.SpawnToken,
            gridIndex,
            grid.SpawnToken,
            new VoxelIndex(1, 0, 1));
        SwiftList<GridNavigationBodyTraceCell> results =
            new SwiftList<GridNavigationBodyTraceCell>(4);
        GridNavigationBodyTraceScratch scratch =
            new GridNavigationBodyTraceScratch(gridCapacity: 1, addressCapacity: 4);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            source,
            target,
            new Vector3d(Fixed64.Zero, Fixed64.FromFraction(-1, 2), Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.FromFraction(-1, 2), Fixed64.One),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4,
            outputLimit: 4,
            candidateWorkLimit: 5L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
        Assert.False(report.IsComplete);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(4, report.AddressCandidateCount);
        Assert.Equal(5, report.CandidateWorkCount);
        Assert.Equal(4, report.CellCount);
        Assert.Equal(
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(0, 0, 1),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(1, 0, 1)
            },
            results.Select(value => value.Cell.VoxelIndex));
        Assert.Equal(new VoxelIndex(0, 0, 1), Assert.Single(results, value => !value.IsPhysicallyPresent).Cell.VoxelIndex);
        Assert.All(results, value => Assert.Equal(grid.LastChangeSequence, value.GridLastChangeSequence));
    }

    [Fact]
    public void RectangularThreeAxisTrace_ShouldRetainAllEightClosureCells()
    {
        GridConfiguration configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.One,
            storageKind: GridStorageKind.Sparse);
        VoxelIndex missing = new VoxelIndex(0, 1, 0);
        VoxelIndex[] present =
        {
            new VoxelIndex(0, 0, 0), new VoxelIndex(0, 0, 1),
            new VoxelIndex(0, 1, 1), new VoxelIndex(1, 0, 0),
            new VoxelIndex(1, 0, 1), new VoxelIndex(1, 1, 0),
            new VoxelIndex(1, 1, 1)
        };
        Assert.True(_world.TryAddGrid(configuration, present, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex source = Address(grid, new VoxelIndex(0, 0, 0));
        WorldVoxelIndex target = Address(grid, new VoxelIndex(1, 1, 1));
        SwiftList<GridNavigationBodyTraceCell> results = new(8);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 8);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world, source, target,
            new Vector3d(Fixed64.Zero, Fixed64.FromFraction(-1, 2), Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.FromFraction(1, 2), Fixed64.One),
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 8, outputLimit: 8, candidateWorkLimit: 9L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
        Assert.Equal(8, report.AddressCandidateCount);
        Assert.Equal(9, report.CandidateWorkCount);
        Assert.Equal(8, report.CellCount);
        Assert.Equal(missing, Assert.Single(results, value => !value.IsPhysicallyPresent).Cell.VoxelIndex);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexVerticalPlanarTrace_ShouldRetainFourCellClosure(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            Fixed64.One,
            Fixed64.One,
            orientation);
        VoxelIndex targetIndex = new VoxelIndex(1, 1, 0);
        GridConfiguration configuration = new GridConfiguration(
            Vector3d.Zero,
            HexCoordinateUtility.AxialToWorldOffset(targetIndex, metrics),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        VoxelIndex missing = new VoxelIndex(0, 1, 0);
        Assert.True(_world.TryAddGrid(
            configuration,
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(1, 0, 0),
                targetIndex
            },
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(GridCellGeometry.TryGetPrism(grid, new VoxelIndex(0, 0, 0), out GridCellPrism sourcePrism));
        Assert.True(GridCellGeometry.TryGetPrism(grid, targetIndex, out GridCellPrism targetPrism));
        SwiftList<GridNavigationBodyTraceCell> results = new(4);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 4);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            Address(grid, new VoxelIndex(0, 0, 0)),
            Address(grid, targetIndex),
            new Vector3d(sourcePrism.Center.X, sourcePrism.VerticalMin, sourcePrism.Center.Z),
            new Vector3d(targetPrism.Center.X, targetPrism.VerticalMin, targetPrism.Center.Z),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4,
            outputLimit: 4,
            candidateWorkLimit: 5L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
        Assert.Equal(4, report.AddressCandidateCount);
        Assert.Equal(5, report.CandidateWorkCount);
        Assert.Equal(4, report.CellCount);
        Assert.Equal(
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(0, 1, 0),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(1, 1, 0)
            },
            results.Select(value => value.Cell.VoxelIndex));
        Assert.Equal(missing, Assert.Single(results, value => !value.IsPhysicallyPresent).Cell.VoxelIndex);
    }

    [Fact]
    public void LargeStationaryBody_ShouldIncludePositiveOverlapBeyondClosureButExcludeTangency()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex center = Address(grid, new VoxelIndex(1, 0, 1));
        Vector3d foot = new Vector3d(Fixed64.One, Fixed64.FromFraction(-1, 2), Fixed64.One);
        SwiftList<GridNavigationBodyTraceCell> results = new(9);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 9);

        GridNavigationBodyTraceReport large = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.FromFraction(3, 4), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 9, candidateWorkLimit: 10L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, large.Status);
        Assert.True(large.IsComplete);
        Assert.Equal(9, large.AddressCandidateCount);
        Assert.Equal(9, large.CellCount);
        Assert.Equal(
            new[]
            {
                new VoxelIndex(0, 0, 0), new VoxelIndex(0, 0, 1), new VoxelIndex(0, 0, 2),
                new VoxelIndex(1, 0, 0), new VoxelIndex(1, 0, 1), new VoxelIndex(1, 0, 2),
                new VoxelIndex(2, 0, 0), new VoxelIndex(2, 0, 1), new VoxelIndex(2, 0, 2)
            },
            results.Select(value => value.Cell.VoxelIndex));

        GridNavigationBodyTraceReport tangent = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.Half, Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 9, candidateWorkLimit: 10L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, tangent.Status);
        Assert.Equal(new[] { center.VoxelIndex }, results.Select(value => value.Cell.VoxelIndex));

        GridNavigationBodyTraceReport oneRawOverlap = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.FromRaw(Fixed64.Half.m_rawValue + 1L), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 9, candidateWorkLimit: 10L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, oneRawOverlap.Status);
        Assert.Equal(5, oneRawOverlap.CellCount);
    }

    [Fact]
    public void DisjointPlanarAndVerticalInteriorIntervals_ShouldExcludePrism()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-Fixed64.One, new Fixed64(-2), -Fixed64.One),
                new Vector3d(new Fixed64(2), Fixed64.One, Fixed64.One)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelIndex excludedIndex = new(2, 3, 1);
        Assert.True(GridCellGeometry.TryGetPrism(grid, excludedIndex, out GridCellPrism excludedPrism));
        Assert.Equal(new Vector3d(Fixed64.One, Fixed64.One, Fixed64.Zero), excludedPrism.Center);
        SwiftList<GridNavigationBodyTraceCell> results = new(48);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 48);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            Address(grid, new VoxelIndex(1, 2, 1)),
            Address(grid, new VoxelIndex(2, 1, 1)),
            new Vector3d(
                Fixed64.FromFraction(-1, 4),
                Fixed64.FromFraction(-1, 4),
                Fixed64.Zero),
            new Vector3d(
                Fixed64.FromFraction(3, 4),
                Fixed64.FromFraction(-5, 4),
                Fixed64.Zero),
            Fixed64.Half,
            Fixed64.One,
            results,
            scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 48,
            outputLimit: 48,
            candidateWorkLimit: 49L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.DoesNotContain(results, value => value.Cell.VoxelIndex == excludedIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaredEndpointsWithoutBodyContact_ShouldFailClosedInBothDirections(bool reverse)
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(10, 0, 10)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex source = Address(grid, new VoxelIndex(0, 0, 0));
        WorldVoxelIndex target = Address(grid, new VoxelIndex(1, 0, 0));
        Vector3d start = new(Fixed64.Zero, Fixed64.FromFraction(19, 2), Fixed64.Zero);
        Vector3d end = new(new Fixed64(10), -Fixed64.Half, Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(121);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 121);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            reverse ? target : source,
            reverse ? source : target,
            reverse ? end : start,
            reverse ? start : end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 121, outputLimit: 121, candidateWorkLimit: 122L);

        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Equal(0, report.GridCandidateCount);
        Assert.Equal(0, report.AddressCandidateCount);
        Assert.Equal(0L, report.CandidateWorkCount);
        Assert.Equal(0, report.CellCount);
        Assert.Empty(results);
    }

    [Fact]
    public void BodyAtFiniteGridBoundary_ShouldAcceptTangencyAndRejectOneRawPenetration()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex center = Address(grid, default);
        Vector3d foot = new(Fixed64.Zero, Fixed64.FromFraction(-1, 2), Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(1);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 1);

        GridNavigationBodyTraceReport tangent = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.Half, Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 1, outputLimit: 1, candidateWorkLimit: 2L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, tangent.Status);
        Assert.Single(results);

        GridNavigationBodyTraceReport planarPenetration = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.FromRaw(Fixed64.Half.m_rawValue + 1L), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 1, outputLimit: 1, candidateWorkLimit: 2L);

        Assert.Equal(
            GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            planarPenetration.Status);
        Assert.Empty(results);

        GridNavigationBodyTraceReport verticalPenetration = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.Zero, Fixed64.FromRaw(Fixed64.One.m_rawValue + 1L),
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 1, outputLimit: 1, candidateWorkLimit: 2L);

        Assert.Equal(
            GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            verticalPenetration.Status);
        Assert.Empty(results);

        Vector3d boundaryFoot = new(Fixed64.Half, -Fixed64.Half, Fixed64.Zero);
        GridNavigationBodyTraceReport zeroRadiusBoundary = GridTracer.TraceNavigationBodyInto(
            _world, center, center, boundaryFoot, boundaryFoot,
            Fixed64.Zero, Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 1, outputLimit: 1, candidateWorkLimit: 2L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, zeroRadiusBoundary.Status);
        Assert.Single(results);

        Vector3d oneRawOutside = new(
            Fixed64.FromRaw(Fixed64.Half.m_rawValue + 1L),
            -Fixed64.Half,
            Fixed64.Zero);
        GridNavigationBodyTraceReport zeroRadiusPenetration = GridTracer.TraceNavigationBodyInto(
            _world, center, center, oneRawOutside, oneRawOutside,
            Fixed64.Zero, Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 1, outputLimit: 1, candidateWorkLimit: 2L);

        Assert.Equal(
            GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            zeroRadiusPenetration.Status);
        Assert.Empty(results);
    }

    [Fact]
    public void ZeroRadiusBoundaryLine_ShouldUseClosureOwnershipAndRejectOneRawOutside()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(0, 0, 1)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex source = Address(grid, default);
        WorldVoxelIndex target = Address(grid, new VoxelIndex(0, 0, 1));
        SwiftList<GridNavigationBodyTraceCell> results = new(2);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 2);
        Vector3d start = new(Fixed64.Half, -Fixed64.Half, Fixed64.Zero);
        Vector3d end = new(Fixed64.Half, -Fixed64.Half, Fixed64.One);

        GridNavigationBodyTraceReport boundary = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 2, outputLimit: 2, candidateWorkLimit: 3L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, boundary.Status);
        Assert.Equal(2, boundary.CellCount);

        Fixed64 outsideX = Fixed64.FromRaw(Fixed64.Half.m_rawValue + 1L);
        GridNavigationBodyTraceReport outside = GridTracer.TraceNavigationBodyInto(
            _world,
            source,
            target,
            new Vector3d(outsideX, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(outsideX, -Fixed64.Half, Fixed64.One),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 2,
            outputLimit: 2,
            candidateWorkLimit: 3L);

        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, outside.Status);
        Assert.Empty(results);
    }

    [Fact]
    public void CrossGridSparseGap_ShouldRetainCanonicalEvidenceAndRefreshOnlyChangedGrid()
    {
        GridConfiguration firstConfiguration = new(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            storageKind: GridStorageKind.Sparse);
        GridConfiguration secondConfiguration = new(
            new Vector3d(2, 0, 0),
            new Vector3d(3, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            firstConfiguration,
            new[] { new VoxelIndex(1, 0, 0) },
            out ushort firstIndex));
        Assert.True(_world.TryAddGrid(
            secondConfiguration,
            Array.Empty<VoxelIndex>(),
            out ushort secondIndex));
        VoxelGrid firstGrid = _world.ActiveGrids[firstIndex];
        VoxelGrid secondGrid = _world.ActiveGrids[secondIndex];
        WorldVoxelIndex source = Address(firstGrid, new VoxelIndex(1, 0, 0));
        WorldVoxelIndex target = Address(secondGrid, new VoxelIndex(0, 0, 0));
        Vector3d start = new(Fixed64.One, Fixed64.FromFraction(-1, 2), Fixed64.Zero);
        Vector3d end = new(new Fixed64(2), Fixed64.FromFraction(-1, 2), Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(2);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 2, addressCapacity: 4);

        GridNavigationBodyTraceReport missing = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.FromFraction(1, 4), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4, outputLimit: 2, candidateWorkLimit: 6L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, missing.Status);
        Assert.Equal(2, missing.GridCandidateCount);
        Assert.Equal(4, missing.AddressCandidateCount);
        Assert.Equal(6, missing.CandidateWorkCount);
        Assert.Equal(2, missing.CellCount);
        GridNavigationBodyTraceCell retainedMissing = Assert.Single(results, value => !value.IsPhysicallyPresent);
        Assert.Equal(target, retainedMissing.Cell);
        ulong firstLastChangeSequence = results[0].GridLastChangeSequence;
        GridCoveredAddressRunStamp firstRun = missing.RunStamp;

        GridNavigationBodyTraceReport missingSource = GridTracer.TraceNavigationBodyInto(
            _world,
            target,
            source,
            end,
            start,
            Fixed64.FromFraction(1, 4),
            Fixed64.One,
            results,
            scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4,
            outputLimit: 2,
            candidateWorkLimit: 6L);
        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, missingSource.Status);
        Assert.Equal(target, Assert.Single(results, value => !value.IsPhysicallyPresent).Cell);
        Assert.DoesNotContain(
            results,
            value => value.Role == GridNavigationBodyTraceCellRole.PhysicalAlternativeDependency);

        Assert.True(secondGrid.TryAddVoxel(new VoxelIndex(0, 0, 0), out _));
        GridNavigationBodyTraceReport complete = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.FromFraction(1, 4), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4, outputLimit: 2, candidateWorkLimit: 6L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, complete.Status);
        Assert.NotEqual(firstRun, complete.RunStamp);
        Assert.Equal(firstLastChangeSequence, results[0].GridLastChangeSequence);
        Assert.True(results[1].GridLastChangeSequence > retainedMissing.GridLastChangeSequence);
    }

    [Fact]
    public void StationaryLargeBody_ShouldUseAlignedPrismsAcrossAdjacentGrids()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(0, 0, 2)),
            out ushort firstIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 2)),
            out ushort secondIndex));
        VoxelGrid first = _world.ActiveGrids[firstIndex];
        VoxelGrid second = _world.ActiveGrids[secondIndex];
        WorldVoxelIndex source = Address(first, new VoxelIndex(0, 0, 1));
        WorldVoxelIndex target = Address(second, new VoxelIndex(0, 0, 1));
        Vector3d foot = new(Fixed64.Half, Fixed64.FromFraction(-1, 2), Fixed64.One);
        SwiftList<GridNavigationBodyTraceCell> results = new(6);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 2, addressCapacity: 6);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world, source, target, foot, foot,
            Fixed64.FromFraction(3, 4), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 6, outputLimit: 6, candidateWorkLimit: 8L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(2, report.GridCandidateCount);
        Assert.Equal(6, report.AddressCandidateCount);
        Assert.Equal(8, report.CandidateWorkCount);
        Assert.Equal(6, report.CellCount);
        Assert.Equal(3, results.Count(value => value.Cell.GridIndex == first.GridIndex));
        Assert.Equal(3, results.Count(value => value.Cell.GridIndex == second.GridIndex));
    }

    [Fact]
    public void CrossGridRectangularDiagonal_ShouldRetainTheGeometryDerivedFourCellClosure()
    {
        VoxelGrid[] grids = new VoxelGrid[4];
        Vector3d[] centers =
        {
            Vector3d.Zero,
            new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.One),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.One)
        };
        for (int i = 0; i < centers.Length; i++)
        {
            Assert.True(_world.TryAddGrid(
                new GridConfiguration(centers[i], centers[i]),
                out ushort gridIndex));
            grids[i] = _world.ActiveGrids[gridIndex];
        }

        SwiftList<GridNavigationBodyTraceCell> results = new(4);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 4, addressCapacity: 4);
        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            Address(grids[0], default),
            Address(grids[3], default),
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(Fixed64.One, -Fixed64.Half, Fixed64.One),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4,
            outputLimit: 4,
            candidateWorkLimit: 8L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(4, report.GridCandidateCount);
        Assert.Equal(4, report.AddressCandidateCount);
        Assert.Equal(8, report.CandidateWorkCount);
        Assert.Equal(4, report.CellCount);
        Assert.Equal(centers, results.Select(value => value.ConfigurationKey.BoundsMin));

        Assert.True(_world.TryRemoveGrid(grids[1].GridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                centers[1],
                centers[1],
                storageKind: GridStorageKind.Sparse),
            Array.Empty<VoxelIndex>(),
            out _));

        GridNavigationBodyTraceReport missingIntermediate = GridTracer.TraceNavigationBodyInto(
            _world,
            Address(grids[0], default),
            Address(grids[3], default),
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(Fixed64.One, -Fixed64.Half, Fixed64.One),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4,
            outputLimit: 4,
            candidateWorkLimit: 8L);

        Assert.Equal(
            GridNavigationBodyTraceStatus.IncompletePhysicalCoverage,
            missingIntermediate.Status);
        GridNavigationBodyTraceCell missing = Assert.Single(
            results,
            value => !value.IsPhysicallyPresent);
        Assert.Equal(centers[1], missing.ConfigurationKey.BoundsMin);
        Assert.Equal(GridNavigationBodyTraceCellRole.RequiredCoverage, missing.Role);
        Assert.DoesNotContain(
            results,
            value => value.Role == GridNavigationBodyTraceCellRole.PhysicalAlternativeDependency);
    }

    [Fact]
    public void DuplicateMissingPrism_ShouldNotPoisonACompleteSelectedUnion()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0)),
            out ushort denseIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(1, 0, 0),
                new Vector3d(1, 0, 0),
                storageKind: GridStorageKind.Sparse),
            Array.Empty<VoxelIndex>(),
            out _));
        VoxelGrid dense = _world.ActiveGrids[denseIndex];
        WorldVoxelIndex center = Address(dense, new VoxelIndex(1, 0, 0));
        Vector3d foot = new(Fixed64.One, -Fixed64.Half, Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(1);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 2, addressCapacity: 4);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.FromFraction(1, 4), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4, outputLimit: 1, candidateWorkLimit: 6L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(2, report.GridCandidateCount);
        Assert.Equal(4, report.AddressCandidateCount);
        Assert.Equal(6, report.CandidateWorkCount);
        Assert.Equal(1, report.CellCount);
        GridNavigationBodyTraceCell selected = Assert.Single(results);
        Assert.Equal(center, selected.Cell);
        Assert.True(selected.IsPhysicallyPresent);
    }

    [Fact]
    public void DistinctDuplicateEndpoints_ShouldRetainBothPinnedIdentitiesUnderReversal()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0)),
            out ushort denseIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(1, 0, 0),
                new Vector3d(1, 0, 0),
                storageKind: GridStorageKind.Sparse),
            Array.Empty<VoxelIndex>(),
            out ushort sparseIndex));
        VoxelGrid dense = _world.ActiveGrids[denseIndex];
        VoxelGrid sparse = _world.ActiveGrids[sparseIndex];
        WorldVoxelIndex source = Address(dense, new VoxelIndex(1, 0, 0));
        WorldVoxelIndex target = Address(sparse, default);
        Vector3d foot = new(Fixed64.One, -Fixed64.Half, Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(2);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 2, addressCapacity: 4);

        GridNavigationBodyTraceReport forward = GridTracer.TraceNavigationBodyInto(
            _world, source, target, foot, foot,
            Fixed64.FromFraction(1, 4), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4, outputLimit: 2, candidateWorkLimit: 6L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, forward.Status);
        Assert.Equal(2, forward.CellCount);
        Assert.Equal(new[] { source, target }, results.Select(value => value.Cell));
        Assert.Equal(new[] { true, false }, results.Select(value => value.IsPhysicallyPresent));

        GridNavigationBodyTraceReport reverse = GridTracer.TraceNavigationBodyInto(
            _world, target, source, foot, foot,
            Fixed64.FromFraction(1, 4), Fixed64.One,
            results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 4, outputLimit: 2, candidateWorkLimit: 6L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, reverse.Status);
        Assert.Equal(forward.CellCount, reverse.CellCount);
        Assert.Equal(new[] { source, target }, results.Select(value => value.Cell));
        Assert.Equal(new[] { true, false }, results.Select(value => value.IsPhysicallyPresent));
    }

    [Fact]
    public void AllMissingDuplicateAlternatives_ShouldPublishAffectedOnlyDependencyEvidence()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-Fixed64.One, Fixed64.Zero, -Fixed64.One),
                new Vector3d(1, 0, 1),
                storageKind: GridStorageKind.Sparse),
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(0, 0, 1),
                new VoxelIndex(0, 0, 2),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(1, 0, 1),
                new VoxelIndex(1, 0, 2),
                new VoxelIndex(2, 0, 0),
                new VoxelIndex(2, 0, 2)
            },
            out ushort routeIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(1, 0, 0),
                new Vector3d(1, 0, 0),
                storageKind: GridStorageKind.Sparse),
            Array.Empty<VoxelIndex>(),
            out ushort alternativeIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(10, 0, 0)),
            out ushort unrelatedIndex));
        VoxelGrid route = _world.ActiveGrids[routeIndex];
        VoxelGrid alternative = _world.ActiveGrids[alternativeIndex];
        VoxelGrid unrelatedGrid = _world.ActiveGrids[unrelatedIndex];
        WorldVoxelIndex source = Address(route, new VoxelIndex(1, 0, 1));
        SwiftList<GridNavigationBodyTraceCell> routeEvidence = new(10);
        SwiftList<GridNavigationBodyTraceCell> unrelatedEvidence = new(1);
        GridNavigationBodyTraceScratch routeScratch = new(gridCapacity: 2, addressCapacity: 10);
        GridNavigationBodyTraceScratch unrelatedScratch = new(gridCapacity: 1, addressCapacity: 1);

        GridNavigationBodyTraceReport oneBelow = GridTracer.TraceNavigationBodyInto(
            _world, source, source,
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            Fixed64.FromFraction(3, 4), Fixed64.One,
            routeEvidence, routeScratch,
            gridCandidateLimit: routeScratch.CandidateGrids.Capacity,
            addressCandidateLimit: 10, outputLimit: 9, candidateWorkLimit: 12L);

        Assert.Equal(GridNavigationBodyTraceStatus.OutputLimitExceeded, oneBelow.Status);
        Assert.Equal(2, oneBelow.GridCandidateCount);
        Assert.Equal(10, oneBelow.AddressCandidateCount);
        Assert.Equal(12, oneBelow.CandidateWorkCount);
        Assert.Equal(0, oneBelow.CellCount);
        Assert.Empty(routeEvidence);

        GridNavigationBodyTraceReport missing = GridTracer.TraceNavigationBodyInto(
            _world, source, source,
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            Fixed64.FromFraction(3, 4), Fixed64.One,
            routeEvidence, routeScratch,
            gridCandidateLimit: routeScratch.CandidateGrids.Capacity,
            addressCandidateLimit: 10, outputLimit: 10, candidateWorkLimit: 12L);
        WorldVoxelIndex unrelated = Address(unrelatedGrid, default);
        GridNavigationBodyTraceReport unrelatedReport = GridTracer.TraceNavigationBodyInto(
            _world, unrelated, unrelated,
            new Vector3d(new Fixed64(10), -Fixed64.Half, Fixed64.Zero),
            new Vector3d(new Fixed64(10), -Fixed64.Half, Fixed64.Zero),
            Fixed64.FromFraction(1, 4), Fixed64.One,
            unrelatedEvidence, unrelatedScratch,
            gridCandidateLimit: unrelatedScratch.CandidateGrids.Capacity,
            addressCandidateLimit: 1, outputLimit: 1, candidateWorkLimit: 2L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, missing.Status);
        Assert.Equal(10, missing.CellCount);
        Assert.Equal(9, routeEvidence.Count(value =>
            value.Role == GridNavigationBodyTraceCellRole.RequiredCoverage));
        GridNavigationBodyTraceCell dependency = Assert.Single(routeEvidence, value =>
            value.Role == GridNavigationBodyTraceCellRole.PhysicalAlternativeDependency);
        Assert.Equal(Address(alternative, default), dependency.Cell);
        Assert.False(dependency.IsPhysicallyPresent);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, unrelatedReport.Status);
        Assert.True(EvidenceIsCurrent(_world, routeEvidence));
        Assert.True(EvidenceIsCurrent(_world, unrelatedEvidence));

        Assert.True(alternative.TryAddVoxel(default, out _));

        Assert.NotEqual(unrelatedReport.RunStamp.ChangeSequence, _world.ChangeSequence);
        Assert.False(EvidenceIsCurrent(_world, routeEvidence));
        Assert.True(EvidenceIsCurrent(_world, unrelatedEvidence));

        GridNavigationBodyTraceReport complete = GridTracer.TraceNavigationBodyInto(
            _world, source, source,
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            Fixed64.FromFraction(3, 4), Fixed64.One,
            routeEvidence, routeScratch,
            gridCandidateLimit: routeScratch.CandidateGrids.Capacity,
            addressCandidateLimit: 10, outputLimit: 9, candidateWorkLimit: 12L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, complete.Status);
        Assert.Equal(9, complete.CellCount);
        Assert.All(routeEvidence, value =>
            Assert.Equal(GridNavigationBodyTraceCellRole.RequiredCoverage, value.Role));
    }

    [Fact]
    public void GridCandidateLimit_ShouldStopBeforeSecondGridAndRemainIndependentFromCombinedWork()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort sourceGridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        VoxelGrid sourceGrid = _world.ActiveGrids[sourceGridIndex];
        WorldVoxelIndex source = Address(sourceGrid, default);
        Vector3d foot = new(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(1);
        GridNavigationBodyTraceScratch exactScratch = new(gridCapacity: 2, addressCapacity: 2);

        GridNavigationBodyTraceReport exact = GridTracer.TraceNavigationBodyInto(
            _world, source, source, foot, foot,
            Fixed64.Zero, Fixed64.One, results, exactScratch,
            gridCandidateLimit: 2,
            addressCandidateLimit: 2,
            outputLimit: 1,
            candidateWorkLimit: 4L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, exact.Status);
        Assert.Equal(2, exact.GridCandidateCount);
        Assert.Equal(2, exact.AddressCandidateCount);
        Assert.Equal(4L, exact.CandidateWorkCount);
        Assert.Single(results);

        GridNavigationBodyTraceScratch boundedScratch = new(gridCapacity: 1, addressCapacity: 1);

        GridNavigationBodyTraceReport oneBelow = GridTracer.TraceNavigationBodyInto(
            _world, source, source, foot, foot,
            Fixed64.Zero, Fixed64.One, results, boundedScratch,
            gridCandidateLimit: 1,
            addressCandidateLimit: 1,
            outputLimit: 1,
            candidateWorkLimit: 2L);

        Assert.Equal(GridNavigationBodyTraceStatus.GridCandidateLimitExceeded, oneBelow.Status);
        Assert.Equal(1, oneBelow.GridCandidateCount);
        Assert.Equal(0, oneBelow.AddressCandidateCount);
        Assert.Equal(1L, oneBelow.CandidateWorkCount);
        Assert.Empty(results);

        GridNavigationBodyTraceReport combinedTighter = GridTracer.TraceNavigationBodyInto(
            _world, source, source, foot, foot,
            Fixed64.Zero, Fixed64.One, results, boundedScratch,
            gridCandidateLimit: 2,
            addressCandidateLimit: 1,
            outputLimit: 1,
            candidateWorkLimit: 1L);
        Assert.Equal(
            GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded,
            combinedTighter.Status);
        Assert.Equal(1L, combinedTighter.CandidateWorkCount);

        GridNavigationBodyTraceReport equalTie = GridTracer.TraceNavigationBodyInto(
            _world, source, source, foot, foot,
            Fixed64.Zero, Fixed64.One, results, boundedScratch,
            gridCandidateLimit: 1,
            addressCandidateLimit: 1,
            outputLimit: 1,
            candidateWorkLimit: 1L);
        Assert.Equal(GridNavigationBodyTraceStatus.GridCandidateLimitExceeded, equalTie.Status);
        Assert.Equal(1L, equalTie.CandidateWorkCount);
    }

    [Fact]
    public void CheckedBodyBoundsArithmetic_ShouldBeDistinctFromInvalidGeometry()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex source = Address(grid, default);
        SwiftList<GridNavigationBodyTraceCell> results = new(1);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 1);

        GridNavigationBodyTraceReport topOverflow = GridTracer.TraceNavigationBodyInto(
            _world, source, source,
            new Vector3d(Fixed64.Zero, Fixed64.MaxValue, Fixed64.Zero),
            new Vector3d(Fixed64.Zero, Fixed64.MaxValue, Fixed64.Zero),
            Fixed64.Zero, Fixed64.MinIncrement, results, scratch,
            gridCandidateLimit: 1,
            addressCandidateLimit: 1,
            outputLimit: 1,
            candidateWorkLimit: 2L);
        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, topOverflow.Status);
        Assert.Equal(0L, topOverflow.CandidateWorkCount);

        GridNavigationBodyTraceReport boundsOverflow = GridTracer.TraceNavigationBodyInto(
            _world, source, source,
            new Vector3d(Fixed64.MaxValue, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(Fixed64.MaxValue, -Fixed64.Half, Fixed64.Zero),
            Fixed64.MinIncrement, Fixed64.One, results, scratch,
            gridCandidateLimit: 1,
            addressCandidateLimit: 1,
            outputLimit: 1,
            candidateWorkLimit: 2L);
        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, boundsOverflow.Status);
        Assert.Equal(0L, boundsOverflow.CandidateWorkCount);

        Fixed64 maximumCellOrigin = new Fixed64(int.MaxValue);
        Vector3d maximumCell = new(maximumCellOrigin, Fixed64.Zero, Fixed64.Zero);
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(maximumCell, maximumCell),
            out ushort maximumGridIndex));
        WorldVoxelIndex maximumSource = Address(_world.ActiveGrids[maximumGridIndex], default);
        GridNavigationBodyTraceReport expandedBoundsOverflow = GridTracer.TraceNavigationBodyInto(
            _world, maximumSource, maximumSource,
            new Vector3d(maximumCellOrigin, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(maximumCellOrigin, -Fixed64.Half, Fixed64.Zero),
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: 1,
            addressCandidateLimit: 1,
            outputLimit: 1,
            candidateWorkLimit: 2L);
        Assert.Equal(
            GridNavigationBodyTraceStatus.ArithmeticOverflow,
            expandedBoundsOverflow.Status);
        Assert.Equal(0L, expandedBoundsOverflow.CandidateWorkCount);
    }

    [Fact]
    public void LimitsInvalidReversalAndWarmup_ShouldRemainExactAndAllocationFree()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex source = Address(grid, new VoxelIndex(0, 0, 0));
        WorldVoxelIndex target = Address(grid, new VoxelIndex(1, 0, 1));
        Vector3d start = new(Fixed64.Zero, Fixed64.FromFraction(-1, 2), Fixed64.Zero);
        Vector3d end = new(Fixed64.One, Fixed64.FromFraction(-1, 2), Fixed64.One);
        SwiftList<GridNavigationBodyTraceCell> results = new(9);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 9);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridTracer.TraceNavigationBodyInto(
                _world, source, target, start, end,
                Fixed64.Zero, Fixed64.One, results, scratch,
                gridCandidateLimit: -1,
                addressCandidateLimit: 9,
                outputLimit: 4,
                candidateWorkLimit: 10L));
        Assert.Equal("gridCandidateLimit", exception.ParamName);

        GridNavigationBodyTraceReport exact = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, exact.Status);
        WorldVoxelIndex[] canonical = results.Select(value => value.Cell).ToArray();

        GridNavigationBodyTraceReport reverse = GridTracer.TraceNavigationBodyInto(
            _world, target, source, end, start,
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        Assert.Equal(canonical, results.Select(value => value.Cell));

        GridNavigationBodyTraceReport addressFailure = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 8, outputLimit: 4, candidateWorkLimit: 10L);
        Assert.Equal(GridNavigationBodyTraceStatus.AddressLimitExceeded, addressFailure.Status);
        Assert.Equal(8, addressFailure.AddressCandidateCount);
        Assert.Empty(results);

        GridNavigationBodyTraceReport workFailure = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 9L);
        Assert.Equal(GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded, workFailure.Status);
        Assert.Equal(9, workFailure.CandidateWorkCount);
        Assert.Empty(results);

        GridNavigationBodyTraceReport outputFailure = GridTracer.TraceNavigationBodyInto(
            _world,
            Address(grid, new VoxelIndex(1, 0, 1)),
            Address(grid, new VoxelIndex(1, 0, 1)),
            new Vector3d(Fixed64.One, Fixed64.FromFraction(-1, 2), Fixed64.One),
            new Vector3d(Fixed64.One, Fixed64.FromFraction(-1, 2), Fixed64.One),
            Fixed64.FromFraction(3, 4), Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 8, candidateWorkLimit: 10L);
        Assert.Equal(GridNavigationBodyTraceStatus.OutputLimitExceeded, outputFailure.Status);
        Assert.Empty(results);

        GridNavigationBodyTraceReport invalid = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            -Fixed64.MinIncrement, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, invalid.Status);
        Assert.Empty(results);

        _ = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void TraceNavigationBody_ShouldRejectInvalidLimitsWorldsProfilesAndEndpointIdentities()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex source = Address(grid, default);
        WorldVoxelIndex target = Address(grid, new VoxelIndex(1, 0, 0));
        Vector3d start = new(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero);
        Vector3d end = new(Fixed64.One, -Fixed64.Half, Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(3);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 3);

        Assert.Equal("addressCandidateLimit", Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridTracer.TraceNavigationBodyInto(
                _world, source, target, start, end,
                Fixed64.Zero, Fixed64.One, results, scratch,
                1, -1, 2, 4L)).ParamName);
        Assert.Equal("outputLimit", Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridTracer.TraceNavigationBodyInto(
                _world, source, target, start, end,
                Fixed64.Zero, Fixed64.One, results, scratch,
                1, 3, -1, 4L)).ParamName);
        Assert.Equal("candidateWorkLimit", Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridTracer.TraceNavigationBodyInto(
                _world, source, target, start, end,
                Fixed64.Zero, Fixed64.One, results, scratch,
                1, 3, 2, -1L)).ParamName);

        GridNavigationBodyTraceReport nullWorld = GridTracer.TraceNavigationBodyInto(
            null,
            source,
            target,
            start,
            end,
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            1,
            3,
            2,
            4L);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, nullWorld.Status);

        GridNavigationBodyTraceReport zeroHeight = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.Zero, results, scratch,
            1, 3, 2, 4L);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, zeroHeight.Status);

        WorldVoxelIndex missingSource = new(
            _world.SpawnToken + 1L,
            source.GridIndex,
            source.GridSpawnToken,
            source.VoxelIndex);
        GridNavigationBodyTraceReport missingEndpoint = GridTracer.TraceNavigationBodyInto(
            _world, missingSource, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            1, 3, 2, 4L);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, missingEndpoint.Status);

        GridWorld disposedWorld = GridWorldTestFactory.CreateWorld();
        disposedWorld.Dispose();
        GridNavigationBodyTraceReport disposed = GridTracer.TraceNavigationBodyInto(
            disposedWorld, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            1, 3, 2, 4L);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, disposed.Status);
        Assert.Empty(results);
    }

    [Fact]
    public void TraceNavigationBody_ShouldFailClosedForUnbisectablePrismsAndNonNeighborEndpoints()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort validEndpointGridIndex));
        VoxelGrid validEndpointGrid = _world.ActiveGrids[validEndpointGridIndex];
        WorldVoxelIndex validEndpoint = Address(validEndpointGrid, default);
        Fixed64 oddRaw = Fixed64.FromRaw(Fixed64.One.m_rawValue + 1L);
        GridTopologyMetrics invalidPrismMetrics = GridTopologyMetrics.Rectangular(
            oddRaw,
            Fixed64.One,
            Fixed64.One);
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyMetrics: invalidPrismMetrics),
            out ushort invalidGridIndex));
        VoxelGrid invalidGrid = _world.ActiveGrids[invalidGridIndex];
        WorldVoxelIndex invalidCell = Address(invalidGrid, default);
        SwiftList<GridNavigationBodyTraceCell> results = new(16);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 4, addressCapacity: 16);

        GridNavigationBodyTraceReport invalidEndpointPrism = GridTracer.TraceNavigationBodyInto(
            _world,
            invalidCell,
            invalidCell,
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            4,
            16,
            16,
            20L);
        Assert.Equal(
            GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            invalidEndpointPrism.Status);

        GridNavigationBodyTraceReport invalidTargetPrism = GridTracer.TraceNavigationBodyInto(
            _world,
            validEndpoint,
            invalidCell,
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            4,
            16,
            16,
            20L);
        Assert.Equal(
            GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            invalidTargetPrism.Status);

        GridNavigationBodyTraceReport invalidCandidatePrism = GridTracer.TraceNavigationBodyInto(
            _world,
            validEndpoint,
            validEndpoint,
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            4,
            16,
            16,
            20L);
        Assert.Equal(
            GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            invalidCandidatePrism.Status);

        Assert.True(_world.TryRemoveGrid(invalidGridIndex));
        Assert.True(_world.TryRemoveGrid(validEndpointGridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0)),
            out ushort validGridIndex));
        VoxelGrid validGrid = _world.ActiveGrids[validGridIndex];
        WorldVoxelIndex source = Address(validGrid, default);
        WorldVoxelIndex nonNeighbor = Address(validGrid, new VoxelIndex(2, 0, 0));
        GridNavigationBodyTraceReport disconnected = GridTracer.TraceNavigationBodyInto(
            _world,
            source,
            nonNeighbor,
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(new Fixed64(2), -Fixed64.Half, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            4,
            16,
            16,
            20L);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, disconnected.Status);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CoincidentMixedGeometry_ShouldNotPoisonTheSelectedPhysicalUnion(int geometry)
    {
        GridTopologyMetrics sourceMetrics = GridTopologyMetrics.Rectangular(
            Fixed64.One,
            Fixed64.One,
            new Fixed64(2));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyMetrics: sourceMetrics),
            out ushort sourceGridIndex));

        GridTopologyKind comparisonKind = geometry == 0
            ? GridTopologyKind.HexPrism
            : GridTopologyKind.RectangularPrism;
        GridTopologyMetrics comparisonMetrics = geometry switch
        {
            0 => GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop),
            1 => GridTopologyMetrics.Rectangular(Fixed64.One, new Fixed64(2), new Fixed64(2)),
            2 => GridTopologyMetrics.Rectangular(new Fixed64(2), Fixed64.One, new Fixed64(2)),
            3 => GridTopologyMetrics.Rectangular(new Fixed64(2), Fixed64.One, Fixed64.One),
            _ => GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, new Fixed64(3))
        };
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: comparisonKind,
                topologyMetrics: comparisonMetrics),
            out _));

        VoxelGrid sourceGrid = _world.ActiveGrids[sourceGridIndex];
        WorldVoxelIndex source = Address(sourceGrid, default);
        SwiftList<GridNavigationBodyTraceCell> results = new(2);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 2, addressCapacity: 2);
        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            source,
            source,
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            2,
            2,
            2,
            4L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(2, report.GridCandidateCount);
        Assert.Equal(2, report.AddressCandidateCount);
        Assert.Equal(source, Assert.Single(results).Cell);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosureDiscovery_ShouldFailClosedAtTheFixedPointWorldEdge(bool neighborCenterOverflows)
    {
        Fixed64 distanceFromMaximum = neighborCenterOverflows ? Fixed64.Half : Fixed64.One;
        Assert.True(Fixed64.TrySubtract(
            Fixed64.MaxValue,
            distanceFromMaximum,
            out Fixed64 sourceX));
        Vector3d minimum = new(sourceX, Fixed64.Zero, Fixed64.Zero);
        Vector3d maximum = new(sourceX, Fixed64.Zero, Fixed64.One);
        Assert.True(_world.TryAddGrid(new GridConfiguration(minimum, maximum), out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex source = Address(grid, default);
        WorldVoxelIndex target = Address(grid, new VoxelIndex(0, 0, 1));
        SwiftList<GridNavigationBodyTraceCell> results = new(2);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 2);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            source,
            target,
            new Vector3d(sourceX, -Fixed64.Half, Fixed64.Zero),
            new Vector3d(sourceX, -Fixed64.Half, Fixed64.One),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            1,
            2,
            2,
            3L);

        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateExpansion_ShouldReportVerticalFixedPointOverflow(bool upper)
    {
        Fixed64 centerY = upper ? Fixed64.One : -Fixed64.One;
        Vector3d center = new(Fixed64.Zero, centerY, Fixed64.Zero);
        Assert.True(_world.TryAddGrid(new GridConfiguration(center, center), out ushort gridIndex));
        Fixed64 hugeEdge = Fixed64.FromRaw(Fixed64.MaxValue.m_rawValue - 1L);
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(new Fixed64(10), Fixed64.Zero, Fixed64.Zero),
                new Vector3d(new Fixed64(10), Fixed64.Zero, Fixed64.Zero),
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    hugeEdge,
                    Fixed64.One,
                    Fixed64.One)),
            out _));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex cell = Address(grid, default);
        Vector3d foot = new(Fixed64.Zero, centerY - Fixed64.Half, Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(1);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 1);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            cell,
            cell,
            foot,
            foot,
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            1,
            1,
            1,
            2L);

        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, report.Status);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateExpansion_ShouldReportDepthFixedPointOverflow(bool upper)
    {
        Fixed64 centerZ = upper ? new Fixed64(2) : new Fixed64(-2);
        Vector3d center = new(Fixed64.Zero, Fixed64.Zero, centerZ);
        Assert.True(_world.TryAddGrid(new GridConfiguration(center, center), out ushort gridIndex));
        Fixed64 hugeEdge = Fixed64.FromRaw(
            Fixed64.MaxValue.m_rawValue - Fixed64.One.m_rawValue - 1L);
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(new Fixed64(10), Fixed64.Zero, Fixed64.Zero),
                new Vector3d(new Fixed64(10), Fixed64.Zero, Fixed64.Zero),
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    hugeEdge,
                    Fixed64.One,
                    Fixed64.One)),
            out _));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex cell = Address(grid, default);
        Vector3d foot = new(Fixed64.Zero, -Fixed64.Half, centerZ);
        SwiftList<GridNavigationBodyTraceCell> results = new(1);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 1);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            cell,
            cell,
            foot,
            foot,
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            1,
            1,
            1,
            2L);

        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, report.Status);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateExpansion_ShouldReportWidthFixedPointOverflow(bool upper)
    {
        Fixed64 centerX = upper ? new Fixed64(2) : new Fixed64(-2);
        Vector3d center = new(centerX, Fixed64.Zero, Fixed64.Zero);
        Assert.True(_world.TryAddGrid(new GridConfiguration(center, center), out ushort gridIndex));
        Fixed64 hugeEdge = Fixed64.FromRaw(
            Fixed64.MaxValue.m_rawValue - Fixed64.One.m_rawValue - 1L);
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(new Fixed64(10), Fixed64.Zero, Fixed64.Zero),
                new Vector3d(new Fixed64(10), Fixed64.Zero, Fixed64.Zero),
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    hugeEdge,
                    Fixed64.One,
                    Fixed64.One)),
            out _));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex cell = Address(grid, default);
        Vector3d foot = new(centerX, -Fixed64.Half, Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(1);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 1);

        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            cell,
            cell,
            foot,
            foot,
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            1,
            1,
            1,
            2L);

        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, report.Status);
        Assert.Empty(results);
    }

    [Fact]
    public void TraceNavigationBody_ShouldReportEveryPreLookupArithmeticOverflow()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort gridIndex));
        WorldVoxelIndex cell = Address(_world.ActiveGrids[gridIndex], default);
        SwiftList<GridNavigationBodyTraceCell> results = new(1);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 1);

        Vector3d[] feet =
        {
            new(Fixed64.MinValue, Fixed64.Zero, Fixed64.Zero),
            new(Fixed64.Zero, Fixed64.Zero, Fixed64.MinValue),
            new(Fixed64.MaxValue, Fixed64.Zero, Fixed64.Zero),
            new(Fixed64.Zero, Fixed64.Zero, Fixed64.MaxValue)
        };
        foreach (Vector3d foot in feet)
        {
            GridNavigationBodyTraceReport boundsOverflow = GridTracer.TraceNavigationBodyInto(
                _world,
                cell,
                cell,
                foot,
                foot,
                Fixed64.MinIncrement,
                Fixed64.One,
                results,
                scratch,
                1,
                1,
                1,
                2L);
            Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, boundsOverflow.Status);
        }

        GridNavigationBodyTraceReport startTopOverflow = GridTracer.TraceNavigationBodyInto(
            _world,
            cell,
            cell,
            new Vector3d(Fixed64.Zero, Fixed64.MaxValue, Fixed64.Zero),
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            1,
            1,
            1,
            2L);
        GridNavigationBodyTraceReport endTopOverflow = GridTracer.TraceNavigationBodyInto(
            _world,
            cell,
            cell,
            Vector3d.Zero,
            new Vector3d(Fixed64.Zero, Fixed64.MaxValue, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.One,
            results,
            scratch,
            1,
            1,
            1,
            2L);
        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, startTopOverflow.Status);
        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, endTopOverflow.Status);
    }

    [Fact]
    public void BodyHeightEquality_ShouldExcludeVerticalTangencyAndOneRawOverlapShouldClaimUpperCell()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(0, 2, 0)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        WorldVoxelIndex center = Address(grid, new VoxelIndex(0, 1, 0));
        Vector3d foot = new(Fixed64.Zero, Fixed64.Half, Fixed64.Zero);
        SwiftList<GridNavigationBodyTraceCell> results = new(3);
        GridNavigationBodyTraceScratch scratch = new(gridCapacity: 1, addressCapacity: 3);

        GridNavigationBodyTraceReport exact = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.Zero, Fixed64.One, results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 3, outputLimit: 3, candidateWorkLimit: 4L);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, exact.Status);
        Assert.Equal(new[] { center.VoxelIndex }, results.Select(value => value.Cell.VoxelIndex));

        GridNavigationBodyTraceReport oneRaw = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.Zero, Fixed64.FromRaw(Fixed64.One.m_rawValue + 1L), results, scratch,
            gridCandidateLimit: scratch.CandidateGrids.Capacity,
            addressCandidateLimit: 3, outputLimit: 3, candidateWorkLimit: 4L);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, oneRaw.Status);
        Assert.Equal(
            new[] { new VoxelIndex(0, 1, 0), new VoxelIndex(0, 2, 0) },
            results.Select(value => value.Cell.VoxelIndex));
    }

    private WorldVoxelIndex Address(VoxelGrid grid, VoxelIndex index) =>
        new WorldVoxelIndex(_world.SpawnToken, grid.GridIndex, grid.SpawnToken, index);

    private static bool EvidenceIsCurrent(
        GridWorld world,
        SwiftList<GridNavigationBodyTraceCell> evidence)
    {
        for (int i = 0; i < evidence.Count; i++)
        {
            GridNavigationBodyTraceCell cell = evidence[i];
            if (!world.TryGetGrid(cell.Cell, out VoxelGrid grid)
                || grid.LastChangeSequence != cell.GridLastChangeSequence)
            {
                return false;
            }
        }

        return true;
    }
}
