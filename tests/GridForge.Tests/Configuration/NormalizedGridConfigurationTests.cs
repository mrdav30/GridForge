using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
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
