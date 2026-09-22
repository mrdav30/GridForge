//=======================================================================
// GridPlanarNavigationBodyTraceTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
public sealed partial class GridPlanarNavigationBodyTraceTests : IDisposable
{
    private readonly GridWorld _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
    private readonly SwiftList<GridNavigationBodyTraceCell> _results = new SwiftList<GridNavigationBodyTraceCell>(128);
    private readonly GridNavigationBodyTraceScratch _scratch = new GridNavigationBodyTraceScratch(8, 128);

    public void Dispose() { _world.Dispose(); GC.SuppressFinalize(this); }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 6)]
    public void UprightBody_ClaimsWholeFootprintOnOnlySelectedLayer(int axisLength, int count)
    {
        VoxelGrid grid = Add(new Vector3d(-2, 0, -2), new Vector3d(3, 2, 2));
        GridNavigationBodyTraceReport report = Trace(grid, new VoxelIndex(2, 1, 2), new VoxelIndex(3, 1, 2),
            new Vector2d(0, 0), new Vector2d(1, 0), new Fixed64(axisLength), Fixed64.FromFraction(1, 4), layer: 1, embeddingY: Fixed64.One);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(count, _results.Count);
        Assert.All(_results, cell => { Assert.Equal(1, cell.Cell.VoxelIndex.y); Assert.True(cell.IsPhysicallyPresent); });
        Assert.Contains(_results, cell => cell.Cell.VoxelIndex == new VoxelIndex(2, 1, 2));
        Assert.Contains(_results, cell => cell.Cell.VoxelIndex == new VoxelIndex(3, 1, 2));
        Assert.Equal(report.GridCandidateCount + (long)report.AddressCandidateCount, report.CandidateWorkCount);
        AssertScratchEmpty();
    }

    [Fact]
    public void Diagonal_MissingSideRemainsRequiredNegativeEvidence()
    {
        VoxelGrid grid = Add(Vector3d.Zero, new Vector3d(1, 0, 1), new[]
        { new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 0), new VoxelIndex(1, 0, 1) });
        GridNavigationBodyTraceReport report = Trace(grid, default, new VoxelIndex(1, 0, 1), Vector2d.Zero, Vector2d.One, Fixed64.Zero, Fixed64.FromFraction(1, 8));
        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
        Assert.Equal(4, _results.Count);
        GridNavigationBodyTraceCell missing = Assert.Single(_results, cell => !cell.IsPhysicallyPresent);
        Assert.Equal(new VoxelIndex(0, 0, 1), missing.Cell.VoxelIndex);
        Assert.Equal(GridNavigationBodyTraceCellRole.RequiredCoverage, missing.Role);
        Assert.Equal(4, report.AddressCandidateCount);
    }

    [Fact]
    public void WideBody_ClosesOuterNeighborsAndRejectsWorldEdge()
    {
        VoxelGrid grid = Add(new Vector3d(-2, 0, -2), new Vector3d(2, 0, 2));
        GridNavigationBodyTraceReport report = Trace(grid, new VoxelIndex(2, 0, 2), new VoxelIndex(2, 0, 2), Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.One);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(9, _results.Count);
        report = Trace(grid, new VoxelIndex(4, 0, 2), new VoxelIndex(4, 0, 2), new Vector2d(2, 0), new Vector2d(2, 0), Fixed64.Zero, Fixed64.One);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Empty(_results);
        AssertScratchEmpty();
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexNeighbors_PreserveCanonicalPlanarCoverage(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, new Fixed64(4), orientation);
        VoxelGrid grid = Add(Vector3d.Zero, HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(3, 1, 3), metrics), metrics: metrics, kind: GridTopologyKind.HexPrism);
        VoxelIndex source = new VoxelIndex(1, 1, 1);
        VoxelIndex target = new VoxelIndex(2, 1, 1);
        Vector2d start = grid.GetWorldPosition(source).ToVector2d();
        Vector2d end = grid.GetWorldPosition(target).ToVector2d();
        GridNavigationBodyTraceReport report = Trace(grid, source, target, start, end, Fixed64.Zero, Fixed64.FromFraction(1, 8), layer: 1, embeddingY: new Fixed64(4));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(new[] { source, target }, _results.Select(cell => cell.Cell.VoxelIndex));
        GridNavigationBodyTraceCell[] forward = _results.ToArray();
        report = Trace(grid, target, source, end, start, Fixed64.Zero, Fixed64.FromFraction(1, 8), layer: 1, embeddingY: new Fixed64(4));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(forward.Select(cell => cell.Cell), _results.Select(cell => cell.Cell));
    }

    [Fact]
    public void InvalidEndpointAndDomainInputs_DoNotPublishPartialProof()
    {
        VoxelGrid grid = Add(Vector3d.Zero, new Vector3d(3, 1, 0));
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            Trace(grid, default, new VoxelIndex(2, 0, 0), Vector2d.Zero, new Vector2d(2, 0), Fixed64.Zero, Fixed64.Half).Status);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            Trace(grid, default, new VoxelIndex(1, 0, 0), Vector2d.Zero, Vector2d.Right, Fixed64.Zero, Fixed64.Zero).Status);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            Trace(grid, default, new VoxelIndex(1, 0, 0), Vector2d.Zero, Vector2d.Right, Fixed64.Zero, Fixed64.Half, embeddingY: new Fixed64(3)).Status);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            Trace(grid, default, new VoxelIndex(1, 0, 0), Vector2d.Zero, Vector2d.Right, Fixed64.Zero, Fixed64.Half, layer: 1, embeddingY: Fixed64.One).Status);
        Assert.Empty(_results);
        AssertScratchEmpty();
    }

    private VoxelGrid Add(Vector3d min, Vector3d max, VoxelIndex[] physical = null,
        GridTopologyMetrics metrics = default, GridTopologyKind kind = GridTopologyKind.RectangularPrism)
    {
        GridConfiguration configuration = new GridConfiguration(min, max, topologyKind: kind, topologyMetrics: metrics,
            storageKind: physical == null ? GridStorageKind.Dense : GridStorageKind.Sparse);
        ushort id;
        Assert.True(physical == null ? _world.TryAddGrid(configuration, out id) : _world.TryAddGrid(configuration, physical, out id));
        return _world.ActiveGrids[id];
    }

    private GridPlanarLayer Layer(VoxelGrid grid, int layer = 0) => new GridPlanarLayer(
        new GridCoveredAddressGeneration(grid.Configuration.ToGridKey(), grid.GridIndex, grid.SpawnToken, grid.LastChangeSequence), layer);

    private WorldVoxelIndex Address(VoxelGrid grid, VoxelIndex index) => new WorldVoxelIndex(_world.SpawnToken, grid.GridIndex, grid.SpawnToken, index);

    private GridNavigationBodyTraceReport Trace(VoxelGrid grid, VoxelIndex source, VoxelIndex target,
        Vector2d start, Vector2d end, Fixed64 axis, Fixed64 radius, int layer = 0, Fixed64 embeddingY = default) =>
        GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(grid, layer) }, embeddingY,
            Address(grid, source), Address(grid, target), start, end, axis, radius, _results, _scratch,
            8, 128, 128, 136);

    private void AssertScratchEmpty()
    {
        Assert.Empty(_scratch.CandidateGrids);
        Assert.Empty(_scratch.AddressCandidates);
        Assert.Empty(_scratch.UnionMembers);
    }
}
