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
public class HexQuantizedLatticeTests
{
    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void NonAuthoredBounds_ShouldNormalizeOutwardAndRemainStable(HexOrientation orientation)
    {
        Vector3d min = new Vector3d(-1000, 0, 1000);
        for (int radiusRaw = 1; radiusRaw <= 32; radiusRaw++)
        for (int xRaw = 797; xRaw <= 800; xRaw++)
        for (int zRaw = 597; zRaw <= 600; zRaw++)
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(radiusRaw), Fixed64.One, orientation);
            Vector3d max = min + new Vector3d(Fixed64.FromRaw(xRaw), Fixed64.Zero, Fixed64.FromRaw(zRaw));
            GridConfiguration input = new GridConfiguration(min, max, topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: metrics, storageKind: GridStorageKind.Sparse);
            Assert.True(input.TryNormalize(out NormalizedGridConfiguration first));
            Assert.True(first.Configuration.BoundsMax.X >= max.X);
            Assert.True(first.Configuration.BoundsMax.Z >= max.Z);
            Assert.True(first.Configuration.TryNormalize(out NormalizedGridConfiguration second));
            Assert.Equal(first.Key, second.Key);
            Assert.Equal(first.AddressCount, second.AddressCount);
        }
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void Normalization_ShouldRespectIntAddressCountLimit(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(6), Fixed64.One, orientation);
        GridConfiguration largest = new GridConfiguration(Vector3d.Zero,
            HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(int.MaxValue - 1, 0, 0), metrics),
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics, storageKind: GridStorageKind.Sparse);
        Assert.True(largest.TryNormalize(out NormalizedGridConfiguration descriptor));
        Assert.Equal(int.MaxValue, descriptor.Width);
        Assert.Equal(int.MaxValue, descriptor.AddressCount);
        GridConfiguration oversized = new GridConfiguration(Vector3d.Zero,
            HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(int.MaxValue, 0, 0), metrics),
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics, storageKind: GridStorageKind.Sparse);
        Assert.False(oversized.TryNormalize(out _));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void AuthoredCenters_ShouldPreserveDimensionsAndNormalization(HexOrientation orientation)
    {
        Vector3d min = new Vector3d(new Fixed64(-1000) + Fixed64.FromRaw(7), Fixed64.Zero,
            new Fixed64(1000) + Fixed64.FromRaw(11));
        for (int radiusRaw = 1; radiusRaw <= 32; radiusRaw++)
        for (int q = 99; q <= 102; q++)
        for (int r = 99; r <= 102; r++)
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(radiusRaw), Fixed64.One, orientation);
            Vector3d max = min + HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(q, 0, r), metrics);
            GridConfiguration input = new GridConfiguration(min, max, topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: metrics, storageKind: GridStorageKind.Sparse);
            Assert.True(input.TryNormalize(out NormalizedGridConfiguration first));
            Assert.Equal(max, first.Configuration.BoundsMax);
            Assert.Equal(q + 1, first.Width);
            Assert.Equal(r + 1, first.Length);
            Assert.True(first.Configuration.TryNormalize(out NormalizedGridConfiguration second));
            Assert.Equal(first.Key, second.Key);
            Assert.Equal(first.AddressCount, second.AddressCount);
        }
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop, 6)]
    [InlineData(HexOrientation.PointyTop, 8)]
    [InlineData(HexOrientation.PointyTop, 14)]
    [InlineData(HexOrientation.FlatTop, 6)]
    [InlineData(HexOrientation.FlatTop, 8)]
    [InlineData(HexOrientation.FlatTop, 14)]
    public void DistantSmallMetricCenter_ShouldResolveThroughLookupSnappingAndCoverage(HexOrientation orientation, int radiusRaw)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(radiusRaw), Fixed64.One, orientation);
        Vector3d min = new Vector3d(-1000, 0, 1000);
        Vector3d max = min + HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(120, 0, 120), metrics);
        GridConfiguration configuration = new GridConfiguration(min, max, scanCellSize: 4,
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        VoxelIndex expected = new VoxelIndex(101, 0, 99);
        Vector3d center = grid.GetWorldPosition(expected);

        Assert.True(grid.TryGetVoxelIndex(center, out VoxelIndex actual));
        Assert.Equal(expected, actual);
        Assert.Equal(expected, grid.Topology.GetClosestVoxelIndex(grid.BoundsMin, grid.Width, grid.Height, grid.Length, center));
        Assert.Equal((25, 0, 24), grid.Topology.SnapToScanCell(grid.BoundsMin, center, 4));
        Assert.Equal(center, grid.FloorToGrid(center));
        Assert.Equal(center, grid.CeilToGrid(center));
        Assert.True(TopologyVoxelRangeUtility.TryGetCandidateRange(grid, center, center, out VoxelIndex lower, out VoxelIndex upper));
        Assert.InRange(expected.x, lower.x, upper.x);
        Assert.InRange(expected.z, lower.z, upper.z);
        SwiftList<Voxel> covered = new SwiftList<Voxel>();
        GridTracer.GetCoveredVoxelsInto(world, center, center, covered);
        Assert.Contains(covered, voxel => voxel.Index == expected);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop, 6)]
    [InlineData(HexOrientation.PointyTop, 8)]
    [InlineData(HexOrientation.PointyTop, 14)]
    [InlineData(HexOrientation.FlatTop, 6)]
    [InlineData(HexOrientation.FlatTop, 8)]
    [InlineData(HexOrientation.FlatTop, 14)]
    public void SparseStationaryTraces_ShouldRetainDistantAddress(HexOrientation orientation, int radiusRaw)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(radiusRaw), Fixed64.One, orientation);
        Vector3d max = HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(120, 0, 120), metrics);
        GridConfiguration configuration = new GridConfiguration(Vector3d.Zero, max,
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics, storageKind: GridStorageKind.Sparse);
        VoxelIndex index = new VoxelIndex(100, 0, 100);
        Assert.True(world.TryAddGrid(configuration, new[] { index }, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Vector3d center = grid.GetWorldPosition(index);
        WorldVoxelIndex address = new WorldVoxelIndex(world.SpawnToken, gridIndex, grid.SpawnToken, index);
        SwiftList<GridTraceInterval> intervals = new SwiftList<GridTraceInterval>(64);
        GridTraceIntervalReport intervalReport = GridTracer.TraceIntervalsInto(world, center, center,
            intervals, new GridTraceIntervalScratch(1, 64), 1, 64, 64, 65);
        Assert.Equal(GridTraceIntervalStatus.Complete, intervalReport.Status);
        Assert.True(intervalReport.HasContinuousPhysicalCoverage);
        Assert.Equal(address, Assert.Single(intervals).Cell);

        SwiftList<GridNavigationBodyTraceCell> bodyCells = new SwiftList<GridNavigationBodyTraceCell>(64);
        Vector3d feet = new Vector3d(center.X, -Fixed64.Half, center.Z);
        GridNavigationBodyTraceReport bodyReport = GridTracer.TraceNavigationBodyInto(world, address, address,
            feet, feet, Fixed64.FromRaw(1), Fixed64.One, bodyCells, new GridNavigationBodyTraceScratch(1, 64),
            1, 64, 64, 65);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, bodyReport.Status);
        Assert.Equal(address, Assert.Single(bodyCells).Cell);
    }
}
