using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using System;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class ClosestQueryTests : IDisposable
{
    private readonly GridWorld _world;

    public ClosestQueryTests()
    {
        _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 8);
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VoxelGrid_TryGetClosestVoxel_ShouldUseNearestCenterInsteadOfContainingVoxel()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d position = Vector3d.FromDouble(0.75, 0, 0);

        Assert.True(grid.TryGetVoxel(position, out Voxel containingVoxel));
        Assert.Equal(new VoxelIndex(0, 0, 0), containingVoxel.Index);

        Assert.True(grid.TryGetClosestVoxel(position, out Voxel closestVoxel));
        Assert.Equal(new VoxelIndex(1, 0, 0), closestVoxel.Index);
    }

    [Fact]
    public void VoxelGrid_TryGetClosestVoxel_ShouldReturnNearestConfiguredSparseVoxel()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(8, 0, 8),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            configuration,
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(8, 0, 8) },
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d missingSparsePosition = new(3, 0, 3);

        Assert.False(grid.TryGetVoxel(missingSparsePosition, out _));

        Assert.True(grid.TryGetClosestVoxel(missingSparsePosition, out Voxel closestVoxel));
        Assert.Equal(new VoxelIndex(0, 0, 0), closestVoxel.Index);
    }

    [Fact]
    public void VoxelGrid_TryGetClosestVoxel_ShouldTrackRuntimeSparseMutation()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(8, 0, 8),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            configuration,
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(8, 0, 8) },
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelIndex addedIndex = new(4, 0, 0);
        Vector3d missingSparsePosition = new(5, 0, 1);

        Assert.True(grid.TryGetClosestVoxel(missingSparsePosition, out Voxel initialClosestVoxel));
        Assert.Equal(new VoxelIndex(0, 0, 0), initialClosestVoxel.Index);

        Assert.True(grid.TryAddVoxel(addedIndex, out _));

        Assert.True(grid.TryGetClosestVoxel(missingSparsePosition, out Voxel addedClosestVoxel));
        Assert.Equal(addedIndex, addedClosestVoxel.Index);

        Assert.True(grid.TryRemoveVoxel(addedIndex));

        Assert.True(grid.TryGetClosestVoxel(missingSparsePosition, out Voxel removedClosestVoxel));
        Assert.Equal(new VoxelIndex(0, 0, 0), removedClosestVoxel.Index);
    }

    [Fact]
    public void VoxelGrid_TryGetClosestVoxel_ShouldBreakSparseDistanceTiesByVoxelIndex()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(8, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            configuration,
            new[] { new VoxelIndex(8, 0, 0), new VoxelIndex(0, 0, 0) },
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d midpoint = new(4, 0, 0);

        Assert.False(grid.TryGetVoxel(midpoint, out _));

        Assert.True(grid.TryGetClosestVoxel(midpoint, out Voxel closestVoxel));
        Assert.Equal(new VoxelIndex(0, 0, 0), closestVoxel.Index);
    }

    [Fact]
    public void VoxelGrid_TryGetClosestVoxel_ShouldReturnFalseWhenSparseGridHasNoConfiguredVoxels()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(4, 0, 4),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(configuration, Array.Empty<VoxelIndex>(), out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.False(grid.TryGetClosestVoxel(new Vector3d(2, 0, 2), out Voxel closestVoxel));
        Assert.Null(closestVoxel);
    }

    [Fact]
    public void VoxelGrid_TryGetClosestVoxel_ShouldRoundHexLayerByNearestCenter()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(Vector3d.Zero, metrics, new VoxelIndex(1, 2, 1)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d position = Vector3d.FromDouble(0, 1.75, 0);

        Assert.True(grid.TryGetVoxel(position, out Voxel containingVoxel));
        Assert.Equal(new VoxelIndex(0, 1, 0), containingVoxel.Index);

        Assert.True(grid.TryGetClosestVoxel(position, out Voxel closestVoxel));
        Assert.Equal(new VoxelIndex(0, 2, 0), closestVoxel.Index);
    }

    [Fact]
    public void GridWorld_TryGetClosestGrid_ShouldResolveNearestGridBounds()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0)),
            out _));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(12, 0, 0)),
            out ushort nearGridIndex));
        VoxelGrid nearGrid = _world.ActiveGrids[nearGridIndex];

        Assert.True(_world.TryGetClosestGrid(new Vector3d(8, 0, 0), out VoxelGrid closestGrid));
        Assert.Same(nearGrid, closestGrid);
    }

    [Fact]
    public void GridWorld_TryGetClosestQueries_ShouldFilterByTopologyKind()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0)),
            out ushort rectangularGridIndex));
        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularGridIndex];

        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(new Vector3d(10, 0, 0), hexMetrics, new VoxelIndex(1, 0, 1)),
            out ushort hexGridIndex));
        VoxelGrid hexGrid = _world.ActiveGrids[hexGridIndex];
        Vector3d query = Vector3d.Zero;

        Assert.True(_world.TryGetClosestGrid(query, out VoxelGrid unfilteredGrid));
        Assert.Same(rectangularGrid, unfilteredGrid);

        Assert.True(_world.TryGetClosestGrid(query, out VoxelGrid filteredGrid, GridTopologyKind.HexPrism));
        Assert.Same(hexGrid, filteredGrid);

        Assert.True(_world.TryGetClosestGridAndVoxel(
            query,
            out VoxelGrid filteredVoxelGrid,
            out Voxel filteredVoxel,
            GridTopologyKind.HexPrism));
        Assert.Same(hexGrid, filteredVoxelGrid);
        Assert.Equal(new VoxelIndex(0, 0, 0), filteredVoxel.Index);

        Assert.True(_world.TryGetClosestVoxel(query, out Voxel filteredVoxelOnly, GridTopologyKind.HexPrism));
        Assert.Same(filteredVoxel, filteredVoxelOnly);
    }

    [Fact]
    public void GridWorld_TryGetClosestGridAndVoxel_ShouldChooseClosestPhysicalVoxelAcrossGrids()
    {
        GridConfiguration largeSparseConfiguration = new(
            Vector3d.Zero,
            new Vector3d(100, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            largeSparseConfiguration,
            new[] { new VoxelIndex(100, 0, 0) },
            out ushort containingGridIndex));
        VoxelGrid containingGrid = _world.ActiveGrids[containingGridIndex];

        GridConfiguration smallerSparseConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            smallerSparseConfiguration,
            new[] { new VoxelIndex(0, 0, 0) },
            out ushort closestVoxelGridIndex));
        VoxelGrid closestVoxelGrid = _world.ActiveGrids[closestVoxelGridIndex];

        Vector3d query = new(1, 0, 0);
        Assert.True(_world.TryGetClosestGrid(query, out VoxelGrid closestBoundsGrid));
        Assert.Same(containingGrid, closestBoundsGrid);

        Assert.True(_world.TryGetClosestGridAndVoxel(query, out VoxelGrid resolvedGrid, out Voxel closestVoxel));
        Assert.Same(closestVoxelGrid, resolvedGrid);
        Assert.Equal(new VoxelIndex(0, 0, 0), closestVoxel.Index);
    }

    [Fact]
    public void GridWorld_TryGetClosestGridAndVoxel_ShouldBreakVoxelDistanceTiesByGridIndex()
    {
        GridConfiguration lowerIndexConfiguration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            lowerIndexConfiguration,
            new[] { new VoxelIndex(0, 0, 0) },
            out ushort lowerGridIndex));

        GridConfiguration closerBoundsConfiguration = new(
            new Vector3d(1, 0, 0),
            new Vector3d(3, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            closerBoundsConfiguration,
            new[] { new VoxelIndex(2, 0, 0) },
            out ushort higherGridIndex));

        VoxelGrid lowerGrid = _world.ActiveGrids[lowerGridIndex];
        VoxelGrid higherGrid = _world.ActiveGrids[higherGridIndex];
        Vector3d query = Vector3d.FromDouble(1.5, 0, 0);

        Assert.True(_world.TryGetClosestGrid(query, out VoxelGrid closestBoundsGrid));
        Assert.Same(higherGrid, closestBoundsGrid);

        Assert.True(_world.TryGetClosestGridAndVoxel(query, out VoxelGrid resolvedGrid, out Voxel closestVoxel));
        Assert.Same(lowerGrid, resolvedGrid);
        Assert.Equal(new VoxelIndex(0, 0, 0), closestVoxel.Index);
    }

    [Fact]
    public void GridWorld_TryGetClosestGridAndVoxel_ShouldSkipEmptyClosestBoundsGrid()
    {
        GridConfiguration emptySparseConfiguration = new(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(emptySparseConfiguration, out ushort emptyGridIndex));

        GridConfiguration configuredSparseConfiguration = new(
            new Vector3d(4, 0, 0),
            new Vector3d(4, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            configuredSparseConfiguration,
            new[] { new VoxelIndex(0, 0, 0) },
            out ushort configuredGridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(100, 0, 0),
                new Vector3d(100, 0, 0),
                storageKind: GridStorageKind.Sparse),
            new[] { new VoxelIndex(0, 0, 0) },
            out _));

        VoxelGrid emptyGrid = _world.ActiveGrids[emptyGridIndex];
        VoxelGrid configuredGrid = _world.ActiveGrids[configuredGridIndex];
        Vector3d query = new Vector3d(1, 0, 0);

        Assert.True(_world.TryGetClosestGrid(query, out VoxelGrid closestBoundsGrid));
        Assert.Same(emptyGrid, closestBoundsGrid);

        Assert.True(_world.TryGetClosestGridAndVoxel(query, out VoxelGrid resolvedGrid, out Voxel closestVoxel));
        Assert.Same(configuredGrid, resolvedGrid);
        Assert.Equal(new VoxelIndex(0, 0, 0), closestVoxel.Index);
    }

    [Fact]
    public void ClosestQuery2DOverloads_ShouldProjectToRequestedLayer()
    {
        GridConfiguration configuration = new(
            new Vector3d(0, 2, 0),
            new Vector3d(2, 2, 0));
        Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector2d position = Vector2d.FromDouble(0.75, 0);
        Fixed64 layerY = new(2);

        Assert.True(grid.TryGetClosestVoxel(position, layerY, out Voxel gridVoxel));
        Assert.Equal(new VoxelIndex(1, 0, 0), gridVoxel.Index);

        Assert.True(grid.TryGetClosestVoxel(position, out Voxel defaultLayerGridVoxel));
        Assert.Same(gridVoxel, defaultLayerGridVoxel);

        Assert.True(_world.TryGetClosestGrid(position, out VoxelGrid defaultLayerWorldGrid));
        Assert.Same(grid, defaultLayerWorldGrid);

        Assert.True(_world.TryGetClosestGrid(position, layerY, out VoxelGrid worldGrid));
        Assert.Same(grid, worldGrid);

        Assert.True(_world.TryGetClosestGrid(position, layerY, out VoxelGrid filteredWorldGrid, GridTopologyKind.RectangularPrism));
        Assert.Same(grid, filteredWorldGrid);

        Assert.True(_world.TryGetClosestVoxel(position, layerY, out Voxel worldVoxel));
        Assert.Same(gridVoxel, worldVoxel);

        Assert.True(_world.TryGetClosestVoxel(position, out Voxel defaultLayerWorldVoxel));
        Assert.Same(defaultLayerGridVoxel, defaultLayerWorldVoxel);

        Assert.True(_world.TryGetClosestVoxel(position, layerY, out Voxel filteredWorldVoxel, GridTopologyKind.RectangularPrism));
        Assert.Same(gridVoxel, filteredWorldVoxel);

        Assert.True(_world.TryGetClosestGridAndVoxel(position, out VoxelGrid defaultLayerResolvedGrid, out Voxel defaultLayerResolvedVoxel));
        Assert.Same(grid, defaultLayerResolvedGrid);
        Assert.Same(defaultLayerGridVoxel, defaultLayerResolvedVoxel);

        Assert.True(_world.TryGetClosestGridAndVoxel(position, layerY, out VoxelGrid resolvedGrid, out Voxel resolvedVoxel));
        Assert.Same(grid, resolvedGrid);
        Assert.Same(gridVoxel, resolvedVoxel);

        Assert.True(_world.TryGetClosestGridAndVoxel(
            position,
            layerY,
            out VoxelGrid filteredResolvedGrid,
            out Voxel filteredResolvedVoxel,
            GridTopologyKind.RectangularPrism));
        Assert.Same(grid, filteredResolvedGrid);
        Assert.Same(gridVoxel, filteredResolvedVoxel);
    }

    [Fact]
    public void ClosestQueries_ShouldReturnFalseWhenWorldOrGridIsInactive()
    {
        GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();

        Assert.False(inactiveWorld.TryGetClosestGrid(Vector3d.Zero, out VoxelGrid inactiveGrid));
        Assert.False(inactiveWorld.TryGetClosestVoxel(Vector3d.Zero, out Voxel inactiveVoxel));
        Assert.False(inactiveWorld.TryGetClosestGridAndVoxel(Vector3d.Zero, out VoxelGrid inactivePairGrid, out Voxel inactivePairVoxel));
        Assert.Null(inactiveGrid);
        Assert.Null(inactiveVoxel);
        Assert.Null(inactivePairGrid);
        Assert.Null(inactivePairVoxel);

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(_world.TryRemoveGrid(gridIndex));
        Assert.False(grid.TryGetClosestVoxel(Vector3d.Zero, out Voxel removedGridVoxel));
        Assert.Null(removedGridVoxel);
    }

    [Fact]
    public void GridWorld_TryGetClosestQueries_ShouldReturnFalseWhenWorldHasNoPhysicalVoxels()
    {
        Assert.False(_world.TryGetClosestGrid(Vector3d.Zero, out VoxelGrid closestGrid));
        Assert.False(_world.TryGetClosestVoxel(Vector3d.Zero, out Voxel closestVoxel));
        Assert.False(_world.TryGetClosestGridAndVoxel(Vector3d.Zero, out VoxelGrid voxelGrid, out Voxel voxel));
        Assert.Null(closestGrid);
        Assert.Null(closestVoxel);
        Assert.Null(voxelGrid);
        Assert.Null(voxel);
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
}
