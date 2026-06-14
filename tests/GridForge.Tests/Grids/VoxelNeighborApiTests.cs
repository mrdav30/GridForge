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
    public void GetNeighborsInto_ShouldHandleNoneScopeAndSourceGridShortCircuit()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0)),
            out ushort gridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(10, 0, 0)),
            out ushort isolatedGridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelGrid isolatedGrid = _world.ActiveGrids[isolatedGridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel sourceGridNeighbor));
        Assert.True(isolatedGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel isolated));

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        results.Add(sourceGridNeighbor);

        source.GetNeighborsInto(grid, results, VoxelNeighborScope.None);
        Assert.Empty(results);
        Assert.False(source.HasNeighbor(grid, VoxelNeighborScope.None));

        Assert.True(source.HasNeighbor(grid, VoxelNeighborScope.SourceGrid));
        source.GetNeighborsInto(grid, results, VoxelNeighborScope.SourceGrid);
        Assert.Single(results);
        Assert.Same(sourceGridNeighbor, results[0]);
        Assert.False(isolated.HasNeighbor(isolatedGrid, VoxelNeighborScope.SourceGrid));
    }

    [Fact]
    public void GetNeighborsInto_ShouldSkipStaleSpatialCandidates()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0)),
            out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel expectedNeighbor));
        int spatialCell = _world.GetSpatialGridKey(source.WorldPosition);
        _world.SpatialGridHash[spatialCell].Add(ushort.MaxValue);

        SwiftList<Voxel> results = new SwiftList<Voxel>();
        source.GetNeighborsInto(grid, results, VoxelNeighborScope.All, tolerance: new Fixed64(20));

        Assert.Contains(expectedNeighbor, results.ToArray());

        results.Clear();
        source.GetNeighborsInto(grid, results, VoxelNeighborScope.All, tolerance: Fixed64.Zero);
        Assert.Contains(expectedNeighbor, results.ToArray());
    }

    [Fact]
    public void NeighborListEntryPoints_ShouldClearAndReturnForInvalidOwnerGrid()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0)),
            out ushort ownerGridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(10, 0, 0)),
            out ushort otherGridIndex));

        VoxelGrid ownerGrid = _world.ActiveGrids[ownerGridIndex];
        VoxelGrid otherGrid = _world.ActiveGrids[otherGridIndex];
        Assert.True(ownerGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel source));
        Assert.True(ownerGrid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel existing));

        SwiftList<Voxel> neighbors = new SwiftList<Voxel>();
        neighbors.Add(existing);
        source.GetNeighborsInto(otherGrid, neighbors);
        Assert.Empty(neighbors);

        SwiftList<(RectangularDirection Direction, Voxel Voxel)> rectangular = new SwiftList<(RectangularDirection Direction, Voxel Voxel)>();
        rectangular.Add((RectangularDirection.East, existing));
        source.GetRectangularNeighborsInto(otherGrid, rectangular);
        Assert.Empty(rectangular);

        SwiftList<(HexDirection Direction, Voxel Voxel)> hex = new SwiftList<(HexDirection Direction, Voxel Voxel)>();
        hex.Add((HexDirection.QPositive, existing));
        source.GetHexNeighborsInto(otherGrid, hex);
        Assert.Empty(hex);
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
        Assert.False(rectangularVoxel.TryGetNeighbor(rectangularGrid, (RectangularDirection)int.MaxValue, out _));
        Assert.False(hexVoxel.TryGetNeighbor(hexGrid, (HexDirection)int.MaxValue, out _));
        Assert.False(rectangularVoxel.HasNeighbor(new VoxelGrid()));
        Assert.False(hexVoxel.TryGetNeighbor(new VoxelGrid(), HexDirection.QPositive, out _));

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
    public void DirectionLabeledNeighborQueries_ShouldIgnoreMismatchedTopology()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort rectangularGridIndex));
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(new Vector3d(4, 0, 0), hexMetrics, new VoxelIndex(0, 0, 0)),
            out ushort hexGridIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularGridIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexGridIndex];
        Assert.True(rectangularGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel rectangularVoxel));
        Assert.True(hexGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel hexVoxel));

        SwiftList<(RectangularDirection Direction, Voxel Voxel)> rectangularResults = new();
        SwiftList<(HexDirection Direction, Voxel Voxel)> hexResults = new();

        hexVoxel.GetRectangularNeighborsInto(hexGrid, rectangularResults);
        rectangularVoxel.GetHexNeighborsInto(rectangularGrid, hexResults);

        Assert.Empty(rectangularResults);
        Assert.Empty(hexResults);
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

    [Fact]
    public void DirectionUtilities_ShouldNotExposeMutablePublicArrayMembers()
    {
        Assert.True(RectangularDirectionUtility.Offsets.Length > 0);
        Assert.True(HexDirectionUtility.Offsets.Length > 0);
        AssertNoPublicStaticArrayMembers(typeof(RectangularDirectionUtility));
        AssertNoPublicStaticArrayMembers(typeof(HexDirectionUtility));
    }

    private static void AssertNoPublicStaticArrayMembers(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static;

        Assert.DoesNotContain(
            type.GetFields(Flags),
            field => field.FieldType.IsArray);
        Assert.DoesNotContain(
            type.GetProperties(Flags),
            property => property.PropertyType.IsArray);
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
