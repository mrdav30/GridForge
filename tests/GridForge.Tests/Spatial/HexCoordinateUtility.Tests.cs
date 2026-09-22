using FixedMathSharp;
using GridForge.Grids.Topology;
using Xunit;

namespace GridForge.Spatial.Tests;

public class HexCoordinateUtilityTests
{
    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void QuantizedBasis_ShouldAvoidIdealInverseIntermediateOverflow(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(new Fixed64(300000000), Fixed64.One, orientation);
        foreach (int sign in new[] { -1, 1 })
        {
            VoxelIndex expected = new VoxelIndex(2 * sign, 0, 2 * sign);
            Vector3d center = HexCoordinateUtility.AxialToWorldOffset(expected, metrics);
            HexCoordinateUtility.WorldOffsetToAxial(center.X, center.Z, metrics, out Fixed64 q, out Fixed64 r);
            Assert.Equal(expected, HexCoordinateUtility.RoundAxial(q, Fixed64.Zero, r));
        }
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void QuantizedBasis_ShouldRoundTripSmallMetricCentersAtSignedDistantIndices(HexOrientation orientation)
    {
        for (int radiusRaw = 1; radiusRaw <= 32; radiusRaw++)
        {
            GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(radiusRaw), Fixed64.One, orientation);
            foreach (int qIndex in new[] { -1000001, -101, -1, 0, 1, 100, 1000001 })
            foreach (int rIndex in new[] { -1000001, -101, -1, 0, 1, 100, 1000001 })
            {
                VoxelIndex expected = new VoxelIndex(qIndex, 0, rIndex);
                Vector3d center = HexCoordinateUtility.AxialToWorldOffset(expected, metrics);
                HexCoordinateUtility.WorldOffsetToAxial(center.X, center.Z, metrics, out Fixed64 q, out Fixed64 r);
                Assert.Equal(expected, HexCoordinateUtility.RoundAxial(q, Fixed64.Zero, r));
            }
        }
    }

    [Fact]
    public void AxialToWorldOffset_ShouldProjectPointyTopCenters()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(3),
            HexOrientation.PointyTop);
        VoxelIndex index = new(2, 1, 3);

        Vector3d offset = HexCoordinateUtility.AxialToWorldOffset(index, metrics);

        Assert.Equal(HexCoordinateUtility.Sqrt3 * new Fixed64(7), offset.X);
        Assert.Equal(new Fixed64(3), offset.Y);
        Assert.Equal(new Fixed64(9), offset.Z);
    }

    [Fact]
    public void AxialToWorldOffset_ShouldProjectFlatTopCenters()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(3),
            HexOrientation.FlatTop);
        VoxelIndex index = new(2, 1, 3);

        Vector3d offset = HexCoordinateUtility.AxialToWorldOffset(index, metrics);

        Assert.Equal(new Fixed64(6), offset.X);
        Assert.Equal(new Fixed64(3), offset.Y);
        Assert.Equal(HexCoordinateUtility.Sqrt3 * new Fixed64(8), offset.Z);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void WorldOffsetToAxial_ShouldRoundTripProjectedCenters(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(3),
            orientation);
        VoxelIndex expected = new(3, 2, 4);
        Vector3d offset = HexCoordinateUtility.AxialToWorldOffset(expected, metrics);

        HexCoordinateUtility.WorldOffsetToAxial(offset.X, offset.Z, metrics, out Fixed64 q, out Fixed64 r);
        VoxelIndex rounded = HexCoordinateUtility.RoundAxial(q, offset.Y / metrics.LayerHeight, r);

        Assert.Equal(expected, rounded);
    }
}
