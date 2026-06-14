using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Linq;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class MixedTopologyNeighborTests : IDisposable
{
    private readonly GridWorld _world;

    public MixedTopologyNeighborTests()
    {
        _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TopologyVoxelAabb_ShouldUseRectangularHalfExtentsAndInclusiveOverlap()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(2),
            new Fixed64(4),
            new Fixed64(6));
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(4, 4, 6),
            topologyMetrics: metrics);

        Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 1, 1), out Voxel voxel));

        TopologyVoxelAabb footprint = TopologyVoxelAabb.FromVoxel(grid, voxel);
        TopologyVoxelAabb touching = new(
            footprint.Max,
            footprint.Max + new Vector3d(1, 1, 1));

        Assert.Equal(new Vector3d(1, 2, 3), footprint.Min);
        Assert.Equal(new Vector3d(3, 6, 9), footprint.Max);
        Assert.True(footprint.Overlaps(touching, Fixed64.Zero));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void TopologyVoxelAabb_ShouldUseHexOrientationSpecificHalfExtents(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(4),
            orientation);
        GridConfiguration configuration = CreateHexConfiguration(
            Vector3d.Zero,
            metrics,
            new VoxelIndex(0, 0, 0));

        Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));

        TopologyVoxelAabb footprint = TopologyVoxelAabb.FromVoxel(grid, voxel);
        Fixed64 expectedHalfX = orientation == HexOrientation.PointyTop
            ? HexCoordinateUtility.Sqrt3
            : new Fixed64(2);
        Fixed64 expectedHalfZ = orientation == HexOrientation.PointyTop
            ? new Fixed64(2)
            : HexCoordinateUtility.Sqrt3;

        Assert.Equal(new Vector3d(-expectedHalfX, new Fixed64(-2), -expectedHalfZ), footprint.Min);
        Assert.Equal(new Vector3d(expectedHalfX, new Fixed64(2), expectedHalfZ), footprint.Max);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void MixedTopologyNeighbors_ShouldResolveBetweenRectangularAndHexOrientations(HexOrientation orientation)
    {
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation);
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort rectangularIndex));
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(new Vector3d(1, 0, 0), hexMetrics, new VoxelIndex(0, 0, 0)),
            out ushort hexIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexIndex];
        Assert.True(rectangularGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel rectangularVoxel));
        Assert.True(hexGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel hexVoxel));

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        rectangularVoxel.GetNeighborsInto(rectangularGrid, results, VoxelNeighborScope.MixedTopologyGrids);

        Assert.True(rectangularVoxel.HasNeighbor(rectangularGrid, VoxelNeighborScope.MixedTopologyGrids));
        Assert.Single(results);
        Assert.Same(hexVoxel, results[0]);

        hexVoxel.GetNeighborsInto(hexGrid, results, VoxelNeighborScope.MixedTopologyGrids);

        Assert.True(hexVoxel.HasNeighbor(hexGrid, VoxelNeighborScope.MixedTopologyGrids));
        Assert.Single(results);
        Assert.Same(rectangularVoxel, results[0]);
        Assert.Equal(0, rectangularGrid.NeighborCount);
        Assert.Equal(0, hexGrid.NeighborCount);
    }

    [Fact]
    public void MixedTopologyNeighbors_ShouldReturnMultipleContactsInDeterministicVoxelOrder()
    {
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);
        GridConfiguration rectangularConfiguration = new(
            Vector3d.Zero,
            new Vector3d(1, 0, 0));
        GridConfiguration hexConfiguration = CreateHexConfiguration(
            new Vector3d(Fixed64.Half, Fixed64.Zero, Fixed64.Zero),
            hexMetrics,
            new VoxelIndex(0, 0, 0));

        Assert.True(_world.TryAddGrid(rectangularConfiguration, out ushort rectangularIndex));
        Assert.True(_world.TryAddGrid(hexConfiguration, out ushort hexIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexIndex];
        Assert.True(hexGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        source.GetNeighborsInto(hexGrid, results, VoxelNeighborScope.MixedTopologyGrids);
        VoxelIndex[] actual = results
            .Select(voxel => voxel.Index)
            .ToArray();

        Assert.Equal(
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 0) },
            actual);
        Assert.All(results, voxel => Assert.Same(rectangularGrid, _world.ActiveGrids[voxel.WorldIndex.GridIndex]));
    }

    [Fact]
    public void MixedTopologyNeighbors_ShouldFilterNonOverlappingAabbCandidates()
    {
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort rectangularIndex));
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(
                new Vector3d(new Fixed64(3) * Fixed64.Half, Fixed64.Zero, Fixed64.Zero),
                hexMetrics,
                new VoxelIndex(0, 0, 0)),
            out _));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularIndex];
        Assert.True(rectangularGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        source.GetNeighborsInto(rectangularGrid, results, VoxelNeighborScope.MixedTopologyGrids);

        Assert.False(source.HasNeighbor(rectangularGrid, VoxelNeighborScope.MixedTopologyGrids));
        Assert.Empty(results);
    }

    [Fact]
    public void MixedTopologyNeighbors_ShouldReturnConfiguredSparseTargetsOnly()
    {
        GridTopologyMetrics rectangularMetrics = GridTopologyMetrics.Rectangular(
            new Fixed64(4),
            Fixed64.One,
            new Fixed64(4));
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);
        GridConfiguration rectangularConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: rectangularMetrics);
        GridConfiguration sparseHexConfiguration = CreateSparseHexConfiguration(
            Vector3d.Zero,
            hexMetrics,
            new VoxelIndex(1, 0, 0));
        VoxelIndex configuredHexIndex = new(1, 0, 0);

        Assert.True(_world.TryAddGrid(rectangularConfiguration, out ushort rectangularIndex));
        Assert.True(_world.TryAddGrid(sparseHexConfiguration, new[] { configuredHexIndex }, out ushort hexIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexIndex];
        Assert.True(rectangularGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));
        Assert.True(hexGrid.TryGetVoxel(configuredHexIndex, out Voxel configuredHex));

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        source.GetNeighborsInto(rectangularGrid, results, VoxelNeighborScope.MixedTopologyGrids);

        Assert.Single(results);
        Assert.Same(configuredHex, results[0]);
    }

    [Fact]
    public void MixedTopologyNeighbors_ShouldReflectRuntimeSparseMutationAndGridUnload()
    {
        GridTopologyMetrics rectangularMetrics = GridTopologyMetrics.Rectangular(
            new Fixed64(4),
            Fixed64.One,
            new Fixed64(4));
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);
        GridConfiguration rectangularConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: rectangularMetrics);
        GridConfiguration sparseHexConfiguration = CreateSparseHexConfiguration(
            Vector3d.Zero,
            hexMetrics,
            new VoxelIndex(1, 0, 0));
        VoxelIndex configuredHexIndex = new(1, 0, 0);

        Assert.True(_world.TryAddGrid(rectangularConfiguration, out ushort rectangularIndex));
        Assert.True(_world.TryAddGrid(sparseHexConfiguration, out ushort hexIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexIndex];
        Assert.True(rectangularGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        source.GetNeighborsInto(rectangularGrid, results, VoxelNeighborScope.MixedTopologyGrids);
        Assert.Empty(results);

        Assert.True(hexGrid.TryAddVoxel(configuredHexIndex, out Voxel addedHex));
        source.GetNeighborsInto(rectangularGrid, results, VoxelNeighborScope.MixedTopologyGrids);
        Assert.Single(results);
        Assert.Same(addedHex, results[0]);

        Assert.True(hexGrid.TryRemoveVoxel(configuredHexIndex));
        source.GetNeighborsInto(rectangularGrid, results, VoxelNeighborScope.MixedTopologyGrids);
        Assert.Empty(results);

        Assert.True(hexGrid.TryAddVoxel(configuredHexIndex, out _));
        Assert.True(_world.TryRemoveGrid(hexIndex));
        source.GetNeighborsInto(rectangularGrid, results, VoxelNeighborScope.MixedTopologyGrids);
        Assert.Empty(results);
    }

    [Fact]
    public void MixedTopologyNeighbors_ShouldRespectVerticalFootprintSeparation()
    {
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.FlatTop);

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort rectangularIndex));
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(new Vector3d(1, 2, 0), hexMetrics, new VoxelIndex(0, 0, 0)),
            out _));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularIndex];
        Assert.True(rectangularGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        source.GetNeighborsInto(rectangularGrid, results, VoxelNeighborScope.MixedTopologyGrids);

        Assert.Empty(results);
    }

    private static GridConfiguration CreateHexConfiguration(
        Vector3d boundsMin,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex)
    {
        Vector3d boundsMax = boundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            boundsMin,
            boundsMax,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);
    }

    private static GridConfiguration CreateSparseHexConfiguration(
        Vector3d boundsMin,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex)
    {
        Vector3d boundsMax = boundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            boundsMin,
            boundsMax,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
    }
}
