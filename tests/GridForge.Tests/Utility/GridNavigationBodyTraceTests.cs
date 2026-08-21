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
            addressCandidateLimit: 4,
            outputLimit: 4,
            candidateWorkLimit: 5L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
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
        Assert.All(results, value => Assert.Equal(grid.ChangeHighWaterSequence, value.GridHighWaterSequence));
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
            addressCandidateLimit: 9, outputLimit: 9, candidateWorkLimit: 10L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, large.Status);
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
            addressCandidateLimit: 9, outputLimit: 9, candidateWorkLimit: 10L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, tangent.Status);
        Assert.Equal(new[] { center.VoxelIndex }, results.Select(value => value.Cell.VoxelIndex));

        GridNavigationBodyTraceReport oneRawOverlap = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.FromRaw(Fixed64.Half.m_rawValue + 1L), Fixed64.One,
            results, scratch,
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
            addressCandidateLimit: 1, outputLimit: 1, candidateWorkLimit: 2L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, tangent.Status);
        Assert.Single(results);

        GridNavigationBodyTraceReport planarPenetration = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.FromRaw(Fixed64.Half.m_rawValue + 1L), Fixed64.One,
            results, scratch,
            addressCandidateLimit: 1, outputLimit: 1, candidateWorkLimit: 2L);

        Assert.Equal(
            GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            planarPenetration.Status);
        Assert.Empty(results);

        GridNavigationBodyTraceReport verticalPenetration = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.Zero, Fixed64.FromRaw(Fixed64.One.m_rawValue + 1L),
            results, scratch,
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
            addressCandidateLimit: 4, outputLimit: 2, candidateWorkLimit: 6L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, missing.Status);
        Assert.Equal(2, missing.GridCandidateCount);
        Assert.Equal(4, missing.AddressCandidateCount);
        Assert.Equal(6, missing.CandidateWorkCount);
        Assert.Equal(2, missing.CellCount);
        GridNavigationBodyTraceCell retainedMissing = Assert.Single(results, value => !value.IsPhysicallyPresent);
        Assert.Equal(target, retainedMissing.Cell);
        ulong firstHighWater = results[0].GridHighWaterSequence;
        GridCoveredAddressRunStamp firstRun = missing.RunStamp;

        Assert.True(secondGrid.TryAddVoxel(new VoxelIndex(0, 0, 0), out _));
        GridNavigationBodyTraceReport complete = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.FromFraction(1, 4), Fixed64.One,
            results, scratch,
            addressCandidateLimit: 4, outputLimit: 2, candidateWorkLimit: 6L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, complete.Status);
        Assert.NotEqual(firstRun, complete.RunStamp);
        Assert.Equal(firstHighWater, results[0].GridHighWaterSequence);
        Assert.True(results[1].GridHighWaterSequence > retainedMissing.GridHighWaterSequence);
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
            addressCandidateLimit: 4,
            outputLimit: 4,
            candidateWorkLimit: 8L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(4, report.GridCandidateCount);
        Assert.Equal(4, report.AddressCandidateCount);
        Assert.Equal(8, report.CandidateWorkCount);
        Assert.Equal(4, report.CellCount);
        Assert.Equal(centers, results.Select(value => value.ConfigurationKey.BoundsMin));
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
            addressCandidateLimit: 4, outputLimit: 2, candidateWorkLimit: 6L);

        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, forward.Status);
        Assert.Equal(2, forward.CellCount);
        Assert.Equal(new[] { source, target }, results.Select(value => value.Cell));
        Assert.Equal(new[] { true, false }, results.Select(value => value.IsPhysicallyPresent));

        GridNavigationBodyTraceReport reverse = GridTracer.TraceNavigationBodyInto(
            _world, target, source, foot, foot,
            Fixed64.FromFraction(1, 4), Fixed64.One,
            results, scratch,
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
            addressCandidateLimit: 10, outputLimit: 10, candidateWorkLimit: 12L);
        WorldVoxelIndex unrelated = Address(unrelatedGrid, default);
        GridNavigationBodyTraceReport unrelatedReport = GridTracer.TraceNavigationBodyInto(
            _world, unrelated, unrelated,
            new Vector3d(new Fixed64(10), -Fixed64.Half, Fixed64.Zero),
            new Vector3d(new Fixed64(10), -Fixed64.Half, Fixed64.Zero),
            Fixed64.FromFraction(1, 4), Fixed64.One,
            unrelatedEvidence, unrelatedScratch,
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
            addressCandidateLimit: 10, outputLimit: 9, candidateWorkLimit: 12L);

        Assert.Equal(GridNavigationBodyTraceStatus.Complete, complete.Status);
        Assert.Equal(9, complete.CellCount);
        Assert.All(routeEvidence, value =>
            Assert.Equal(GridNavigationBodyTraceCellRole.RequiredCoverage, value.Role));
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

        GridNavigationBodyTraceReport exact = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, exact.Status);
        WorldVoxelIndex[] canonical = results.Select(value => value.Cell).ToArray();

        GridNavigationBodyTraceReport reverse = GridTracer.TraceNavigationBodyInto(
            _world, target, source, end, start,
            Fixed64.Zero, Fixed64.One, results, scratch,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        Assert.Equal(canonical, results.Select(value => value.Cell));

        GridNavigationBodyTraceReport addressFailure = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            addressCandidateLimit: 8, outputLimit: 4, candidateWorkLimit: 10L);
        Assert.Equal(GridNavigationBodyTraceStatus.AddressLimitExceeded, addressFailure.Status);
        Assert.Equal(8, addressFailure.AddressCandidateCount);
        Assert.Empty(results);

        GridNavigationBodyTraceReport workFailure = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
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
            addressCandidateLimit: 9, outputLimit: 8, candidateWorkLimit: 10L);
        Assert.Equal(GridNavigationBodyTraceStatus.OutputLimitExceeded, outputFailure.Status);
        Assert.Empty(results);

        GridNavigationBodyTraceReport invalid = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            -Fixed64.MinIncrement, Fixed64.One, results, scratch,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, invalid.Status);
        Assert.Empty(results);

        _ = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = GridTracer.TraceNavigationBodyInto(
            _world, source, target, start, end,
            Fixed64.Zero, Fixed64.One, results, scratch,
            addressCandidateLimit: 9, outputLimit: 4, candidateWorkLimit: 10L);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
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
            addressCandidateLimit: 3, outputLimit: 3, candidateWorkLimit: 4L);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, exact.Status);
        Assert.Equal(new[] { center.VoxelIndex }, results.Select(value => value.Cell.VoxelIndex));

        GridNavigationBodyTraceReport oneRaw = GridTracer.TraceNavigationBodyInto(
            _world, center, center, foot, foot,
            Fixed64.Zero, Fixed64.FromRaw(Fixed64.One.m_rawValue + 1L), results, scratch,
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
                || grid.ChangeHighWaterSequence != cell.GridHighWaterSequence)
            {
                return false;
            }
        }

        return true;
    }
}
