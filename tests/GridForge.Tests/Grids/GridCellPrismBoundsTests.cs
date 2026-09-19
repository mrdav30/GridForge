using FixedMathSharp;
using GridForge.Grids.Topology;
using Xunit;

namespace GridForge.Grids.Tests;

public sealed class GridCellPrismBoundsTests
{
    public static TheoryData<Vector3d, Vector3d, Vector3d, Vector3d> RectangularBounds => new()
    {
        {
            new Vector3d(4, 6, 8), new Vector3d(-10, -20, -30),
            new Vector3d(-12, -23, -34), new Vector3d(-8, -17, -26)
        },
        {
            Raw(2, 4, 6), Raw(-4, 10, -7),
            Raw(-5, 8, -10), Raw(-3, 12, -4)
        },
        {
            Raw(2, 4, 6), Raw(long.MinValue + 1, long.MinValue + 2, long.MinValue + 3),
            Raw(long.MinValue, long.MinValue, long.MinValue),
            Raw(long.MinValue + 2, long.MinValue + 4, long.MinValue + 6)
        },
        {
            Raw(2, 4, 6), Raw(long.MaxValue - 1, long.MaxValue - 2, long.MaxValue - 3),
            Raw(long.MaxValue - 2, long.MaxValue - 4, long.MaxValue - 6),
            Raw(long.MaxValue, long.MaxValue, long.MaxValue)
        }
    };

    [Theory]
    [MemberData(nameof(RectangularBounds))]
    public void GetAabb_Rectangle_ShouldPreserveExactIndependentAxisBounds(
        Vector3d edges, Vector3d center, Vector3d expectedMin, Vector3d expectedMax)
    {
        Assert.True(GridCellGeometry.TryCreatePrism(GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(edges.X, edges.Y, edges.Z), center, default, out GridCellPrism prism));

        TopologyVoxelAabb bounds = prism.GetAabb();

        Assert.Equal(expectedMin, bounds.Min);
        Assert.Equal(expectedMax, bounds.Max);
    }

    [Theory]
    // Radius 2 has an apothem of exactly 7,439,101,574 raw Q32.32 units.
    [InlineData(HexOrientation.PointyTop, 5445800314L, 21474836480L, 20324003462L, 38654705664L)]
    [InlineData(HexOrientation.FlatTop, 4294967296L, 22625669498L, 21474836480L, 37503872646L)]
    public void GetAabb_Hexagon_ShouldIncludeAllSixVertices(
        HexOrientation orientation, long minX, long minZ, long maxX, long maxZ)
    {
        Assert.True(GridCellGeometry.TryCreatePrism(GridTopologyKind.HexPrism,
            GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(4), orientation),
            new Vector3d(3, -5, 7), default, out GridCellPrism prism));

        TopologyVoxelAabb bounds = prism.GetAabb();

        Assert.Equal(Raw(minX, -30064771072L, minZ), bounds.Min);
        Assert.Equal(Raw(maxX, -12884901888L, maxZ), bounds.Max);
    }

    [Fact]
    public void GetAabb_DefaultPrism_ShouldRemainZeroBounds()
    {
        TopologyVoxelAabb bounds = default(GridCellPrism).GetAabb();

        Assert.Equal(Vector3d.Zero, bounds.Min);
        Assert.Equal(Vector3d.Zero, bounds.Max);
    }

    private static Vector3d Raw(long x, long y, long z) =>
        new(Fixed64.FromRaw(x), Fixed64.FromRaw(y), Fixed64.FromRaw(z));
}
