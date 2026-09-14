using System;
using FixedMathSharp;
using GridForge.Grids.Topology;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridContactContainmentTests
{
    [Theory]
    [InlineData(-1, false, false)]
    [InlineData(-1, false, true)]
    [InlineData(-1, true, false)]
    [InlineData(-1, true, true)]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    public void NestedRectangles_ShouldKeepExactOrderedContactWithoutCrossingEdges(
        int translation, bool reverse, bool stacked)
    {
        Fixed64 x = translation < 0 ? Fixed64.MinValue + new Fixed64(8)
            : translation > 0 ? Fixed64.MaxValue - new Fixed64(8) : Fixed64.Zero;
        Fixed64 z = -x;
        GridCellPrism outer = Rectangle(new Vector3d(x, new Fixed64(5), z), 8, 10);
        GridCellPrism inner = Rectangle(
            new Vector3d(x + Fixed64.One, new Fixed64(stacked ? 7 : 5), z + Fixed64.One), 2, 4);

        VoxelContactManifold contact = reverse
            ? GridCellGeometry.GetContact(inner, outer)
            : GridCellGeometry.GetContact(outer, inner);

        Assert.Equal(stacked ? VoxelContactKind.Face : VoxelContactKind.VolumeOverlap, contact.Kind);
        Assert.Equal(stacked ? VoxelContactFaceKind.Horizontal : VoxelContactFaceKind.None, contact.FaceKind);
        Assert.Equal(new Fixed64(stacked ? 6 : 4), contact.VerticalMin);
        Assert.Equal(new Fixed64(6), contact.VerticalMax);
        Assert.Equal(new Fixed64(8), contact.CheckedArea);
        Assert.True(contact.IsAreaRepresentable);
        Assert.Equal(stacked, contact.IsPositiveAreaFace);
        Vector3d displacement = new Vector3d(1, stacked ? 2 : 0, 1);
        Assert.Equal(reverse ? -displacement : displacement, contact.SourceToTarget);

        // All four candidates come from containment; the footprint edges never cross.
        Vector2d[] expected =
        {
            new Vector2d(x, z - Fixed64.One),
            new Vector2d(x + new Fixed64(2), z - Fixed64.One),
            new Vector2d(x + new Fixed64(2), z + new Fixed64(3)),
            new Vector2d(x, z + new Fixed64(3))
        };
        Assert.Equal(expected.Length, contact.HorizontalPolygon.VertexCount);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], contact.HorizontalPolygon.GetVertex(i));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OppositeScalarExtremes_ShouldRemainSeparatedWithOverlappingHeight(bool reverse)
    {
        GridCellPrism low = Rectangle(
            new Vector3d(Fixed64.MinValue + new Fixed64(4), Fixed64.Zero, Fixed64.Zero), 2, 4);
        GridCellPrism high = Rectangle(
            new Vector3d(Fixed64.MaxValue - new Fixed64(4), Fixed64.Zero, Fixed64.Zero), 2, 4);

        VoxelContactManifold contact = reverse
            ? GridCellGeometry.GetContact(high, low)
            : GridCellGeometry.GetContact(low, high);

        Assert.Equal(VoxelContactKind.Separated, contact.Kind);
        Assert.Equal(0, contact.HorizontalPolygon.VertexCount);
        Assert.Equal(Fixed64.Zero, contact.CheckedArea);
    }

    [Fact]
    public void DefaultPrism_ShouldPreserveEmptyFootprintFailureAfterVerticalAdmission()
    {
        GridCellPrism overlapping = Rectangle(Vector3d.Zero, 2, 4);
        GridCellPrism separated = Rectangle(new Vector3d(0, 4, 0), 2, 4);

        Assert.Throws<ArgumentException>(() => GridCellGeometry.GetContact(default, overlapping));
        Assert.Throws<ArgumentException>(() => GridCellGeometry.GetContact(overlapping, default));
        Assert.Equal(VoxelContactKind.Separated, GridCellGeometry.GetContact(default, default).Kind);
        Assert.Equal(VoxelContactKind.Separated, GridCellGeometry.GetContact(default, separated).Kind);
        Assert.Equal(VoxelContactKind.Separated, GridCellGeometry.GetContact(separated, default).Kind);
    }

    private static GridCellPrism Rectangle(Vector3d center, int width, int length)
    {
        Assert.True(GridCellGeometry.TryCreatePrism(GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(new Fixed64(width), new Fixed64(2), new Fixed64(length)),
            center, default, out GridCellPrism prism));
        return prism;
    }
}
