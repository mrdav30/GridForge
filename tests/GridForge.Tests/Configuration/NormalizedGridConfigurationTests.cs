using FixedMathSharp;
using GridForge.Diagnostics;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections.Diagnostics;
using Xunit;

namespace GridForge.Configuration.Tests;

[Collection("GridForgeCollection")]
public class NormalizedGridConfigurationTests
{
    [Fact]
    public void TryNormalize_RectangularConfiguration_ReturnsBindingDescriptor()
    {
        GridConfiguration input = new(
            Vector3d.FromDouble(0.25, 0.25, 0.25),
            Vector3d.FromDouble(1.25, 1.25, 1.25),
            scanCellSize: 3,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.Half),
            storageKind: GridStorageKind.Sparse);

        Assert.True(input.TryNormalize(out NormalizedGridConfiguration descriptor));

        Assert.True(descriptor.IsValid);
        Assert.Equal(Vector3d.Zero, descriptor.Configuration.BoundsMin);
        Assert.Equal(Vector3d.FromDouble(1.5, 1.5, 1.5), descriptor.Configuration.BoundsMax);
        Assert.Equal(3, descriptor.Configuration.ScanCellSize);
        Assert.Equal(GridStorageKind.Sparse, descriptor.Configuration.StorageKind);
        Assert.Equal(descriptor.Configuration.ToGridKey(), descriptor.Key);
        Assert.Equal(4, descriptor.Width);
        Assert.Equal(4, descriptor.Height);
        Assert.Equal(4, descriptor.Length);
        Assert.Equal(64, descriptor.AddressCount);
        Assert.True(descriptor.IsValidIndex(default));
        Assert.True(descriptor.IsValidIndex(new VoxelIndex(0, 0, 0)));
        Assert.True(descriptor.IsValidIndex(new VoxelIndex(3, 3, 3)));
        Assert.False(descriptor.IsValidIndex(new VoxelIndex(-1, 0, 0)));
        Assert.False(descriptor.IsValidIndex(new VoxelIndex(4, 0, 0)));
        Assert.False(descriptor.IsValidIndex(new VoxelIndex(0, 4, 0)));
        Assert.False(descriptor.IsValidIndex(new VoxelIndex(0, 0, 4)));

        using GridWorld world = new();
        Assert.True(world.TryAddGrid(input, out ushort gridIndex));
        VoxelGrid activeGrid = world.ActiveGrids[gridIndex];
        Assert.Equal(descriptor.Key, activeGrid.Configuration.ToGridKey());
        Assert.Equal(descriptor.Width, activeGrid.Width);
        Assert.Equal(descriptor.Height, activeGrid.Height);
        Assert.Equal(descriptor.Length, activeGrid.Length);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void TryNormalize_HexConfiguration_UsesTopologyDimensions(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            Fixed64.One,
            new Fixed64(2),
            orientation);
        Vector3d max = HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(2, 1, 3), metrics);
        GridConfiguration input = new(
            Vector3d.Zero,
            max,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);

        Assert.True(input.TryNormalize(out NormalizedGridConfiguration descriptor));

        Assert.Equal(GridTopologyKind.HexPrism, descriptor.Configuration.TopologyKind);
        Assert.Equal(orientation, descriptor.Configuration.TopologyMetrics.HexOrientation);
        Assert.Equal(3, descriptor.Width);
        Assert.Equal(2, descriptor.Height);
        Assert.Equal(4, descriptor.Length);
        Assert.Equal(24, descriptor.AddressCount);
        Assert.True(descriptor.IsValidIndex(new VoxelIndex(2, 1, 3)));
        Assert.False(descriptor.IsValidIndex(new VoxelIndex(3, 1, 3)));
    }

    [Theory]
    [InlineData(GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    public void TryGetCellPrism_UsesNormalizedTopologyProjection(
        GridTopologyKind topologyKind,
        HexOrientation orientation)
    {
        GridTopologyMetrics metrics = topologyKind == GridTopologyKind.RectangularPrism
            ? GridTopologyMetrics.Rectangular(new Fixed64(2), new Fixed64(4), new Fixed64(6))
            : GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(4), orientation);
        VoxelIndex maxIndex = new(2, 1, 3);
        Vector3d max = topologyKind == GridTopologyKind.RectangularPrism
            ? new Vector3d(4, 4, 18)
            : HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        GridConfiguration input = new(
            Vector3d.Zero,
            max,
            topologyKind: topologyKind,
            topologyMetrics: metrics);

        Assert.True(input.TryNormalize(out NormalizedGridConfiguration descriptor));
        VoxelIndex index = new(1, 1, 1);
        Assert.True(descriptor.TryGetCellPrism(index, out GridCellPrism prism));

        using GridWorld world = new();
        Assert.True(world.TryAddGrid(input, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(index, out Voxel voxel));
        Assert.Equal(voxel.WorldPosition, prism.Center);
        Assert.Equal(topologyKind, prism.TopologyKind);
        Assert.Equal(Fixed64.FromRaw(metrics.LayerHeight.m_rawValue >> 1), prism.Center.Y - prism.VerticalMin);
        Assert.Equal(default, prism.Cell);
        Assert.True(prism.Contains(prism.Center));
        Assert.True(prism.Contains(new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z)));
        Assert.False(prism.Contains(new Vector3d(prism.Center.X, prism.VerticalMax + Fixed64.One, prism.Center.Z)));
        Assert.False(prism.Contains(new Vector3d(
            prism.Center.X + new Fixed64(100),
            prism.Center.Y,
            prism.Center.Z)));

        Assert.False(descriptor.TryGetCellPrism(new VoxelIndex(-1, 0, 0), out _));
        Assert.False(descriptor.TryGetCellPrism(new VoxelIndex(descriptor.Width, 0, 0), out _));
    }

    [Theory]
    [InlineData(GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    public void TryValidateNavigationCorridor_ValidatesCanonicalPrimaryFaceChain(
        GridTopologyKind topologyKind,
        HexOrientation orientation)
    {
        GridTopologyMetrics metrics = topologyKind == GridTopologyKind.RectangularPrism
            ? GridTopologyMetrics.Rectangular(new Fixed64(2), new Fixed64(2), new Fixed64(2))
            : GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(2), orientation);
        VoxelIndex finalIndex = new(2, 0, 0);
        Vector3d max = topologyKind == GridTopologyKind.RectangularPrism
            ? new Vector3d(4, 0, 0)
            : HexCoordinateUtility.AxialToWorldOffset(finalIndex, metrics);
        GridConfiguration configuration = new(
            Vector3d.Zero,
            max,
            topologyKind: topologyKind,
            topologyMetrics: metrics);

        Assert.True(configuration.TryNormalize(out NormalizedGridConfiguration descriptor));
        var prisms = new GridCellPrism[3];
        for (int i = 0; i < prisms.Length; i++)
            Assert.True(descriptor.TryGetCellPrism(new VoxelIndex(i, 0, 0), out prisms[i]));

        var waypoints = new Vector3d[4];
        Assert.True(GridCellGeometry.TryValidateNavigationCorridor(
            prisms,
            prisms[0].Center,
            prisms[2].Center,
            Fixed64.Zero,
            Fixed64.One,
            waypoints,
            out int waypointCount,
            out Fixed64 geometricCost));

        Assert.Equal(2, waypointCount);
        Assert.True(geometricCost > Fixed64.Zero);
        Assert.False(GridCellGeometry.TryValidateNavigationCorridor(
            new[] { prisms[0], prisms[2] },
            prisms[0].Center,
            prisms[2].Center,
            Fixed64.Zero,
            Fixed64.One,
            waypoints,
            out _,
            out _));
        Assert.False(GridCellGeometry.TryValidateNavigationCorridor(
            prisms,
            prisms[0].Center,
            prisms[2].Center,
            new Fixed64(3),
            Fixed64.One,
            waypoints,
            out _,
            out _));
        Assert.False(GridCellGeometry.TryValidateNavigationCorridor(
            prisms,
            new Vector3d(100, 0, 0),
            prisms[2].Center,
            Fixed64.Zero,
            Fixed64.One,
            waypoints,
            out _,
            out _));
    }

    [Fact]
    public void NavigationGeometry_DefaultOrIncompletePrismsFailClosed()
    {
        GridCellPrism invalid = default;

        Assert.False(invalid.Contains(Vector3d.Zero));
        Assert.False(GridCellGeometry.TryValidateNavigationCorridor(
            new[] { invalid },
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One,
            new Vector3d[2],
            out int waypointCount,
            out Fixed64 geometricCost));
        Assert.Equal(0, waypointCount);
        Assert.Equal(Fixed64.Zero, geometricCost);
    }

    [Fact]
    public void TryNormalize_InvalidOrOversizedConfiguration_ReturnsDefaultDescriptor()
    {
        GridConfiguration invalidTopology = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: (GridTopologyKind)int.MaxValue);
        GridConfiguration oversized = new(
            Vector3d.Zero,
            new Vector3d(50_000, 50_000, 0));

        Assert.False(invalidTopology.TryNormalize(out NormalizedGridConfiguration invalidDescriptor));
        Assert.False(invalidDescriptor.IsValid);
        Assert.False(oversized.TryNormalize(out NormalizedGridConfiguration oversizedDescriptor));
        Assert.False(oversizedDescriptor.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TryNormalize_DimensionOverflow_FailsRegardlessOfAxisAndDiagnostics(int axis)
    {
        Vector3d minimum = axis switch
        {
            0 => new Vector3d(int.MinValue, 0, 0),
            1 => new Vector3d(0, int.MinValue, 0),
            _ => new Vector3d(0, 0, int.MinValue)
        };
        Vector3d maximum = axis switch
        {
            0 => new Vector3d(int.MaxValue, 0, 0),
            1 => new Vector3d(0, int.MaxValue, 0),
            _ => new Vector3d(0, 0, int.MaxValue)
        };
        GridConfiguration overflowed = new(
            minimum,
            maximum);
        DiagnosticLevel previousMinimumLevel = GridForgeLogger.MinimumLevel;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Warning;
            Assert.False(overflowed.TryNormalize(out NormalizedGridConfiguration warningDescriptor));
            Assert.False(warningDescriptor.IsValid);

            GridForgeLogger.MinimumLevel = DiagnosticLevel.None;
            Assert.False(overflowed.TryNormalize(out NormalizedGridConfiguration quietDescriptor));
            Assert.False(quietDescriptor.IsValid);
        }
        finally
        {
            GridForgeLogger.MinimumLevel = previousMinimumLevel;
        }
    }

    [Fact]
    public void TryNormalize_EquivalentStorageKinds_ShareBindingKey()
    {
        GridConfiguration dense = new(
            Vector3d.FromDouble(0.25, 0, 0.25),
            Vector3d.FromDouble(2.25, 0, 2.25),
            scanCellSize: 2,
            storageKind: GridStorageKind.Dense);
        GridConfiguration sparse = new(
            dense.BoundsMin,
            dense.BoundsMax,
            scanCellSize: 16,
            storageKind: GridStorageKind.Sparse);

        Assert.True(dense.TryNormalize(out NormalizedGridConfiguration denseDescriptor));
        Assert.True(sparse.TryNormalize(out NormalizedGridConfiguration sparseDescriptor));

        Assert.Equal(denseDescriptor.Key, sparseDescriptor.Key);
        Assert.Equal(denseDescriptor.Width, sparseDescriptor.Width);
        Assert.Equal(denseDescriptor.Height, sparseDescriptor.Height);
        Assert.Equal(denseDescriptor.Length, sparseDescriptor.Length);
        Assert.NotEqual(
            denseDescriptor.Configuration.StorageKind,
            sparseDescriptor.Configuration.StorageKind);
    }

    [Fact]
    public void VoxelIndexComparison_IsLexicographicAndIgnoresAllocationSentinel()
    {
        VoxelIndex[] addresses =
        {
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(0, 1, 0),
            new VoxelIndex(0, 0, 1),
            new VoxelIndex(0, 0, 0)
        };

        System.Array.Sort(addresses);

        Assert.Equal(new VoxelIndex(0, 0, 0), addresses[0]);
        Assert.Equal(new VoxelIndex(0, 0, 1), addresses[1]);
        Assert.Equal(new VoxelIndex(0, 1, 0), addresses[2]);
        Assert.Equal(new VoxelIndex(1, 0, 0), addresses[3]);
        Assert.Equal(0, default(VoxelIndex).CompareTo(new VoxelIndex(0, 0, 0)));
    }
}
