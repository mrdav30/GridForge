using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class VoxelNeighborApiTests : IDisposable
{
    private readonly GridWorld _world;

    public VoxelNeighborApiTests()
    {
        _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetNeighborsInto_ShouldResolveRequestedScopesWithoutTopologyBranching()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0)),
            out ushort sourceGridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(2, 0, 0), new Vector3d(2, 0, 0)),
            out ushort sameTopologyGridIndex));
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(new Vector3d(2, 0, 0), hexMetrics, new VoxelIndex(0, 0, 0)),
            out ushort mixedTopologyGridIndex));

        VoxelGrid sourceGrid = _world.ActiveGrids[sourceGridIndex];
        VoxelGrid sameTopologyGrid = _world.ActiveGrids[sameTopologyGridIndex];
        VoxelGrid mixedTopologyGrid = _world.ActiveGrids[mixedTopologyGridIndex];
        Assert.True(sourceGrid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel source));
        Assert.True(sourceGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel sourceGridNeighbor));
        Assert.True(sameTopologyGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel sameTopologyNeighbor));
        Assert.True(mixedTopologyGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel mixedTopologyNeighbor));

        SwiftList<Voxel> results = new SwiftList<Voxel>();

        source.GetNeighborsInto(sourceGrid, results, VoxelNeighborScope.SourceGrid);
        Assert.Single(results);
        Assert.Same(sourceGridNeighbor, results[0]);

        source.GetNeighborsInto(sourceGrid, results, VoxelNeighborScope.SameTopologyGrids);
        Assert.Single(results);
        Assert.Same(sameTopologyNeighbor, results[0]);

        source.GetNeighborsInto(sourceGrid, results, VoxelNeighborScope.MixedTopologyGrids);
        Assert.Single(results);
        Assert.Same(mixedTopologyNeighbor, results[0]);

        source.GetNeighborsInto(sourceGrid, results);
        Assert.Equal(new[] { sourceGridNeighbor, sameTopologyNeighbor, mixedTopologyNeighbor }, results.ToArray());
        Assert.True(source.HasNeighbor(sourceGrid));
    }

    [Fact]
    public void TryGetNeighbor_ShouldKeepDirectedLookupSameTopologyOnly()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort rectangularGridIndex));
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(new Vector3d(1, 0, 0), hexMetrics, new VoxelIndex(0, 0, 0)),
            out ushort hexGridIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularGridIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexGridIndex];
        Assert.True(rectangularGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel rectangularVoxel));
        Assert.True(hexGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel hexVoxel));

        Assert.False(rectangularVoxel.TryGetNeighbor(rectangularGrid, RectangularDirection.East, out _));

        SwiftList<Voxel> contacts = new SwiftList<Voxel>();
        rectangularVoxel.GetNeighborsInto(rectangularGrid, contacts, VoxelNeighborScope.MixedTopologyGrids);
        Assert.Single(contacts);
        Assert.Same(hexVoxel, contacts[0]);
    }

    [Fact]
    public void GetNeighborsInto_ShouldResolveContactWhenTargetCenterFallsOutsideSourceFootprint()
    {
        GridTopologyMetrics wideMetrics = GridTopologyMetrics.Rectangular(new Fixed64(4), Fixed64.One, Fixed64.One);

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort sourceGridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(2, 0, 0),
                new Vector3d(2, 0, 0),
                topologyMetrics: wideMetrics),
            out ushort targetGridIndex));

        VoxelGrid sourceGrid = _world.ActiveGrids[sourceGridIndex];
        VoxelGrid targetGrid = _world.ActiveGrids[targetGridIndex];
        Assert.True(sourceGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));
        Assert.True(targetGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel target));

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        source.GetNeighborsInto(sourceGrid, results, VoxelNeighborScope.SameTopologyGrids);

        Assert.Single(results);
        Assert.Same(target, results[0]);
    }

    [Fact]
    public void GetRectangularNeighborsInto_ShouldFillDirectionLabeledCallerStorage()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 1, 1), out Voxel voxel));

        SwiftList<(RectangularDirection Direction, Voxel Voxel)> results = new SwiftList<(RectangularDirection Direction, Voxel Voxel)>();

        voxel.GetRectangularNeighborsInto(grid, results);

        Assert.Equal(RectangularDirectionUtility.All.Length, results.Count);
        for (int i = 0; i < RectangularDirectionUtility.All.Length; i++)
        {
            Assert.Equal(RectangularDirectionUtility.All[i], results[i].Direction);
            Assert.NotSame(voxel, results[i].Voxel);
        }
    }

    [Fact]
    public void TryGetNeighbor_ShouldReflectSparseRuntimeMutationWithoutCacheInvalidation()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex originIndex = new VoxelIndex(0, 0, 0);
        VoxelIndex eastIndex = new VoxelIndex(1, 0, 0);

        Assert.True(_world.TryAddGrid(configuration, new[] { originIndex }, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(originIndex, out Voxel origin));

        Assert.False(origin.TryGetNeighbor(grid, RectangularDirection.East, out _));

        Assert.True(grid.TryAddVoxel(eastIndex, out Voxel east));
        Assert.True(origin.TryGetNeighbor(grid, RectangularDirection.East, out Voxel resolved));
        Assert.Same(east, resolved);

        Assert.True(grid.TryRemoveVoxel(eastIndex));
        Assert.False(origin.TryGetNeighbor(grid, RectangularDirection.East, out _));
    }

    [Fact]
    public void Voxel_ShouldNotCarryLegacyNeighborStateFields()
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.Null(typeof(Voxel).GetField("_cachedNeighbors", Flags));
        Assert.Null(typeof(Voxel).GetField("_isNeighborCacheValid", Flags));
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
