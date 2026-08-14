using System;
using System.Linq;
using System.Numerics;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridCellGeometryTests : IDisposable
{
    private readonly GridWorld _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RectangularPrism_ShouldExposeExactFootprintVerticalBoundsAndInradius()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(4),
            new Fixed64(6),
            new Fixed64(8));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(10, 20, 30),
                new Vector3d(10, 20, 30),
                topologyMetrics: metrics),
            out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(GridCellGeometry.TryGetPrism(grid, new VoxelIndex(0, 0, 0), out GridCellPrism prism));

        Assert.Equal(GridTopologyKind.RectangularPrism, prism.TopologyKind);
        Assert.Equal(new Fixed64(15), prism.VerticalMin);
        Assert.Equal(new Fixed64(21), prism.VerticalMax);
        Assert.Equal(new Fixed64(2), prism.PlanarInradius);
        Assert.Equal(
            new[]
            {
                new Vector2d(6, 20),
                new Vector2d(10, 20),
                new Vector2d(10, 28),
                new Vector2d(6, 28)
            },
            GetFootprint(prism));
    }

    [Fact]
    public void TryCreatePrism_ShouldFailClosedWhenMetricsCannotBeBisectedExactly()
    {
        Fixed64 oddRaw = Fixed64.FromRaw(Fixed64.One.m_rawValue + 1L);

        Assert.False(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(oddRaw, Fixed64.One, Fixed64.One),
            Vector3d.Zero,
            default,
            out _));
        Assert.False(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(Fixed64.One, oddRaw, Fixed64.One),
            Vector3d.Zero,
            default,
            out _));
        Assert.False(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, oddRaw),
            Vector3d.Zero,
            default,
            out _));

        foreach (HexOrientation orientation in new[] { HexOrientation.PointyTop, HexOrientation.FlatTop })
        {
            Assert.False(GridCellGeometry.TryCreatePrism(
                GridTopologyKind.HexPrism,
                GridTopologyMetrics.Hex(oddRaw, Fixed64.One, orientation),
                Vector3d.Zero,
                default,
                out _));
            Assert.False(GridCellGeometry.TryCreatePrism(
                GridTopologyKind.HexPrism,
                GridTopologyMetrics.Hex(Fixed64.One, oddRaw, orientation),
                Vector3d.Zero,
                default,
                out _));
        }
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexPrism_ShouldExposeStrictlyConvexOrientationSpecificFootprint(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(4), orientation);
        GridConfiguration configuration = CreateHexConfiguration(Vector3d.Zero, metrics, new VoxelIndex(0, 0, 0));
        Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(GridCellGeometry.TryGetPrism(grid, new VoxelIndex(0, 0, 0), out GridCellPrism prism));
        Vector2d[] footprint = GetFootprint(prism);

        Assert.Equal(6, footprint.Length);
        Assert.True(FixedMathSharp.Geometry.FixedConvex2dRelations.IsStrictlyConvex(footprint));
        Assert.Equal(new Fixed64(-2), prism.VerticalMin);
        Assert.Equal(new Fixed64(2), prism.VerticalMax);
        Assert.Equal(HexCoordinateUtility.Sqrt3, prism.PlanarInradius);
        if (orientation == HexOrientation.PointyTop)
        {
            Assert.Equal(new Vector2d(0, -2), footprint[0]);
            Assert.Equal(new Vector2d(HexCoordinateUtility.Sqrt3, new Fixed64(-1)), footprint[1]);
        }
        else
        {
            Assert.Equal(new Vector2d(-2, 0), footprint[0]);
            Assert.Equal(new Vector2d(new Fixed64(-1), -HexCoordinateUtility.Sqrt3), footprint[1]);
        }
    }

    [Fact]
    public void RectangularContacts_ShouldClassifyFaceEdgePointVolumeAndSeparation()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(new Fixed64(2));
        GridCellPrism origin = CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, Vector3d.Zero);

        Assert.Equal(VoxelContactKind.Face, ContactAt(new Vector3d(2, 0, 0)).Kind);
        Assert.Equal(VoxelContactKind.Edge, ContactAt(new Vector3d(2, 2, 0)).Kind);
        Assert.Equal(VoxelContactKind.Point, ContactAt(new Vector3d(2, 2, 2)).Kind);
        Assert.Equal(VoxelContactKind.VolumeOverlap, ContactAt(new Vector3d(1, 0, 0)).Kind);
        Assert.Equal(VoxelContactKind.Separated, ContactAt(new Vector3d(3, 0, 0)).Kind);

        VoxelContactManifold face = ContactAt(new Vector3d(2, 0, 0));
        Assert.Equal(VoxelContactFaceKind.Vertical, face.FaceKind);
        Assert.Equal(new Fixed64(2), face.VerticalFaceWidth);
        Assert.Equal(new Fixed64(2), face.VerticalFaceHeight);
        Assert.Equal(new Fixed64(4), face.CheckedArea);
        Assert.True(face.IsPositiveAreaFace);

        VoxelContactManifold ContactAt(Vector3d targetCenter) =>
            GridCellGeometry.GetContact(
                origin,
                CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, targetCenter));
    }

    [Fact]
    public void RectangularHorizontalContact_ShouldCarryExactOverlapPolygon()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(new Fixed64(2));
        GridCellPrism lower = CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, Vector3d.Zero);
        GridCellPrism upper = CreateOfflinePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(0, 2, 0));

        VoxelContactManifold manifold = GridCellGeometry.GetContact(lower, upper);

        Assert.Equal(VoxelContactKind.Face, manifold.Kind);
        Assert.Equal(VoxelContactFaceKind.Horizontal, manifold.FaceKind);
        Assert.Equal(new Fixed64(1), manifold.VerticalMin);
        Assert.Equal(manifold.VerticalMin, manifold.VerticalMax);
        Assert.Equal(new Fixed64(4), manifold.CheckedArea);
        Assert.Equal(4, manifold.HorizontalPolygon.VertexCount);
    }

    [Fact]
    public void NavigationPortal_RectangularVerticalFace_ShouldExposeExactCapacityBoundary()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(4),
            new Fixed64(6),
            new Fixed64(8));
        GridCellPrism source = CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, Vector3d.Zero);
        GridCellPrism target = CreateOfflinePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(4, 0, 0));

        Assert.True(GridCellGeometry.TryCreateNavigationPortal(source, target, out GridNavigationPortal portal));

        Assert.True(portal.IsValid);
        Assert.Equal(VoxelContactFaceKind.Vertical, portal.FaceKind);
        Assert.Equal(new Vector3d(4, 0, 0), portal.SourceToTarget);
        Assert.Equal(new Vector3d(2, -3, 0), portal.CanonicalFacePoint);
        Assert.Equal(new Fixed64(4), portal.MaximumHorizontalRadius);
        Assert.Equal(new Fixed64(6), portal.MaximumBodyHeight);
        Assert.True(portal.TryResolveProfile(
            new Fixed64(4),
            new Fixed64(6),
            out Vector3d sourceAnchor,
            out Vector3d targetAnchor));
        Assert.Equal(new Vector3d(2, -3, 0), sourceAnchor);
        Assert.Equal(sourceAnchor, targetAnchor);

        Assert.False(portal.TryResolveProfile(
            new Fixed64(4) + Fixed64.MinIncrement,
            new Fixed64(6),
            out _,
            out _));
        Assert.False(portal.TryResolveProfile(
            new Fixed64(4),
            new Fixed64(6) + Fixed64.MinIncrement,
            out _,
            out _));
    }

    [Fact]
    public void NavigationPortal_HorizontalFace_ShouldResolveDirectedUpwardAndDownwardAnchors()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(4),
            new Fixed64(6),
            new Fixed64(8));
        GridCellPrism lower = CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, Vector3d.Zero);
        GridCellPrism upper = CreateOfflinePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(0, 6, 0));

        Assert.True(GridCellGeometry.TryCreateNavigationPortal(lower, upper, out GridNavigationPortal upward));
        Assert.True(GridCellGeometry.TryCreateNavigationPortal(upper, lower, out GridNavigationPortal downward));
        Assert.Equal(VoxelContactFaceKind.Horizontal, upward.FaceKind);
        Assert.Equal(new Fixed64(2), upward.MaximumHorizontalRadius);
        Assert.Equal(new Fixed64(6), upward.MaximumBodyHeight);
        Assert.Equal(upward.MaximumHorizontalRadius, downward.MaximumHorizontalRadius);
        Assert.Equal(upward.MaximumBodyHeight, downward.MaximumBodyHeight);
        Assert.Equal(-upward.SourceToTarget, downward.SourceToTarget);

        Assert.True(upward.TryResolveProfile(
            new Fixed64(2),
            new Fixed64(2),
            out Vector3d upwardSource,
            out Vector3d upwardTarget));
        Assert.True(downward.TryResolveProfile(
            new Fixed64(2),
            new Fixed64(2),
            out Vector3d downwardSource,
            out Vector3d downwardTarget));
        Assert.Equal(new Vector3d(0, 1, 0), upwardSource);
        Assert.Equal(new Vector3d(0, 3, 0), upwardTarget);
        Assert.Equal(upwardTarget, downwardSource);
        Assert.Equal(upwardSource, downwardTarget);

        Assert.False(upward.TryResolveProfile(
            new Fixed64(2) + Fixed64.MinIncrement,
            new Fixed64(2),
            out _,
            out _));
        Assert.False(upward.TryResolveProfile(
            new Fixed64(2),
            new Fixed64(6) + Fixed64.MinIncrement,
            out _,
            out _));
    }

    [Fact]
    public void NavigationPortal_ObliqueHorizontalFace_ShouldNotExceedWideEdgeClearance()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(20_000),
            new Fixed64(2),
            HexOrientation.PointyTop);
        GridCellPrism source = CreateOfflinePrism(GridTopologyKind.HexPrism, metrics, Vector3d.Zero);
        GridCellPrism target = CreateOfflinePrism(
            GridTopologyKind.HexPrism,
            metrics,
            new Vector3d(1_000, 2, 15_000));
        Span<Vector2d> polygon = stackalloc Vector2d[GridConvexPolygon2d.MaxVertexCount];

        Assert.True(GridCellGeometry.TryCreateNavigationPortal(source, target, out GridNavigationPortal portal));
        VoxelContactManifold contact = GridCellGeometry.GetContact(source, target);
        contact.HorizontalPolygon.CopyTo(polygon);
        ReadOnlySpan<Vector2d> footprint = polygon[..contact.HorizontalPolygon.VertexCount];
        Fixed64 exactClearance = GetExactConservativeClearance(
            new Vector2d(portal.CanonicalFacePoint.X, portal.CanonicalFacePoint.Z),
            footprint);

        Assert.Equal(exactClearance, portal.MaximumHorizontalRadius);
    }

    [Fact]
    public void TryCreateNavigationPortal_ShouldRejectNonFacesVolumeOverlapAndDefaults()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(new Fixed64(2));
        GridCellPrism source = CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, Vector3d.Zero);

        Assert.False(GridCellGeometry.TryCreateNavigationPortal(default, source, out _));
        Assert.False(GridCellGeometry.TryCreateNavigationPortal(source, default, out _));
        Assert.False(TryCreateAt(new Vector3d(3, 0, 0)));
        Assert.False(TryCreateAt(new Vector3d(2, 2, 2)));
        Assert.False(TryCreateAt(new Vector3d(2, 2, 0)));
        Assert.False(TryCreateAt(new Vector3d(1, 0, 0)));

        bool TryCreateAt(Vector3d targetCenter) => GridCellGeometry.TryCreateNavigationPortal(
            source,
            CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, targetCenter),
            out _);
    }

    [Fact]
    public void TryCreateNavigationPortal_ShouldFailClosedForNonRepresentableFaceArea()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(1_000_000),
            new Fixed64(1_000_000),
            new Fixed64(1_000_000));
        GridCellPrism source = CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, Vector3d.Zero);
        GridCellPrism target = CreateOfflinePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(1_000_000, 0, 0));

        Assert.False(GridCellGeometry.TryCreateNavigationPortal(source, target, out GridNavigationPortal portal));
        Assert.False(portal.IsValid);
    }

    [Fact]
    public void NavigationPortal_ProfileResolution_ShouldRejectInvalidArgumentsAndAllocateNothingAfterWarmup()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(new Fixed64(4));
        GridCellPrism source = CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, Vector3d.Zero);
        GridCellPrism target = CreateOfflinePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(4, 0, 0));
        Assert.True(GridCellGeometry.TryCreateNavigationPortal(source, target, out GridNavigationPortal portal));
        Assert.False(default(GridNavigationPortal).TryResolveProfile(Fixed64.Zero, Fixed64.One, out _, out _));
        Assert.Throws<ArgumentException>(() =>
        {
            _ = portal.TryResolveProfile(-Fixed64.One, Fixed64.One, out _, out _);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = portal.TryResolveProfile(Fixed64.Zero, Fixed64.Zero, out _, out _);
        });
        Assert.True(portal.TryResolveProfile(Fixed64.One, Fixed64.One, out _, out _));

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool resolved = portal.TryResolveProfile(
            Fixed64.One,
            Fixed64.One,
            out Vector3d sourceAnchor,
            out Vector3d targetAnchor);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(resolved);
        Assert.Equal(sourceAnchor, targetAnchor);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void UnderflowedPositiveArea_ShouldRemainFaceButFailAutomaticPortalCheck()
    {
        Fixed64 twoRawQuanta = Fixed64.FromRaw(2);
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(twoRawQuanta);
        GridCellPrism source = CreateOfflinePrism(GridTopologyKind.RectangularPrism, metrics, Vector3d.Zero);
        GridCellPrism target = CreateOfflinePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(Fixed64.FromRaw(1), twoRawQuanta, Fixed64.Zero));

        VoxelContactManifold manifold = GridCellGeometry.GetContact(source, target);

        Assert.Equal(VoxelContactKind.Face, manifold.Kind);
        Assert.Equal(VoxelContactFaceKind.Horizontal, manifold.FaceKind);
        Assert.Equal(Fixed64.Zero, manifold.CheckedArea);
        Assert.False(manifold.IsAreaRepresentable);
        Assert.False(manifold.IsPositiveAreaFace);
        Assert.Equal(4, manifold.HorizontalPolygon.VertexCount);
        Assert.False(GridCellGeometry.TryCreateNavigationPortal(source, target, out _));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexContacts_ShouldClassifyPrimaryFaceCornerAndVolume(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(4), orientation);
        GridCellPrism source = CreateOfflinePrism(GridTopologyKind.HexPrism, metrics, Vector3d.Zero);
        Vector3d primaryOffset = HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(1, 0, 0), metrics);
        Vector2d corner = source.GetFootprintVertex(0);
        Vector3d cornerOffset = new Vector3d(corner.X * new Fixed64(2), Fixed64.Zero, corner.Y * new Fixed64(2));

        VoxelContactManifold primary = GridCellGeometry.GetContact(
            source,
            CreateOfflinePrism(GridTopologyKind.HexPrism, metrics, primaryOffset));
        VoxelContactManifold verticalEdge = GridCellGeometry.GetContact(
            source,
            CreateOfflinePrism(GridTopologyKind.HexPrism, metrics, cornerOffset));
        VoxelContactManifold point = GridCellGeometry.GetContact(
            source,
            CreateOfflinePrism(
                GridTopologyKind.HexPrism,
                metrics,
                cornerOffset + new Vector3d(0, 4, 0)));
        VoxelContactManifold volume = GridCellGeometry.GetContact(
            source,
            CreateOfflinePrism(GridTopologyKind.HexPrism, metrics, primaryOffset * Fixed64.Half));

        Assert.Equal(VoxelContactKind.Face, primary.Kind);
        Assert.Equal(VoxelContactFaceKind.Vertical, primary.FaceKind);
        Assert.Equal(new Fixed64(2), primary.VerticalFaceWidth);
        Assert.Equal(new Fixed64(4), primary.VerticalFaceHeight);
        Assert.Equal(new Fixed64(8), primary.CheckedArea);
        Assert.Equal(VoxelContactKind.Edge, verticalEdge.Kind);
        Assert.Equal(VoxelContactKind.Point, point.Kind);
        Assert.Equal(VoxelContactKind.VolumeOverlap, volume.Kind);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexDifferingMetrics_ShouldClassifyPositiveFace(HexOrientation orientation)
    {
        GridTopologyMetrics sourceMetrics = GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(4), orientation);
        GridTopologyMetrics targetMetrics = GridTopologyMetrics.Hex(new Fixed64(3), new Fixed64(6), orientation);
        GridCellPrism source = CreateOfflinePrism(GridTopologyKind.HexPrism, sourceMetrics, Vector3d.Zero);
        Fixed64 centerDistance = source.PlanarInradius
            + CreateOfflinePrism(GridTopologyKind.HexPrism, targetMetrics, Vector3d.Zero).PlanarInradius;
        Vector3d targetCenter = orientation == HexOrientation.PointyTop
            ? new Vector3d(centerDistance, Fixed64.Zero, Fixed64.Zero)
            : new Vector3d(Fixed64.Zero, Fixed64.Zero, centerDistance);

        VoxelContactManifold manifold = GridCellGeometry.GetContact(
            source,
            CreateOfflinePrism(GridTopologyKind.HexPrism, targetMetrics, targetCenter));

        Assert.Equal(VoxelContactKind.Face, manifold.Kind);
        Assert.Equal(VoxelContactFaceKind.Vertical, manifold.FaceKind);
        Assert.Equal(new Fixed64(2), manifold.VerticalFaceWidth);
        Assert.Equal(new Fixed64(4), manifold.VerticalFaceHeight);
        Assert.Equal(new Fixed64(8), manifold.CheckedArea);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void MixedTopologyEqualScale_ShouldClassifyPositiveFace(HexOrientation orientation)
    {
        GridCellPrism rectangular = CreateOfflinePrism(
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(new Fixed64(2)),
            Vector3d.Zero);
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, new Fixed64(2), orientation);
        GridCellPrism centeredHex = CreateOfflinePrism(GridTopologyKind.HexPrism, hexMetrics, Vector3d.Zero);
        Fixed64 centerDistance = Fixed64.One + centeredHex.PlanarInradius;
        Vector3d targetCenter = orientation == HexOrientation.PointyTop
            ? new Vector3d(centerDistance, Fixed64.Zero, Fixed64.Zero)
            : new Vector3d(Fixed64.Zero, Fixed64.Zero, centerDistance);

        GridCellPrism hex = CreateOfflinePrism(GridTopologyKind.HexPrism, hexMetrics, targetCenter);
        VoxelContactManifold manifold = GridCellGeometry.GetContact(rectangular, hex);
        VoxelContactManifold reverse = GridCellGeometry.GetContact(hex, rectangular);

        Assert.Equal(VoxelContactKind.Face, manifold.Kind);
        Assert.Equal(VoxelContactFaceKind.Vertical, manifold.FaceKind);
        Assert.True(manifold.IsPositiveAreaFace);
        Assert.Equal(VoxelContactKind.Face, reverse.Kind);
        Assert.Equal(-manifold.SourceToTarget, reverse.SourceToTarget);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void MixedTopologyDifferingMetrics_ShouldRejectAabbCornerAndClassifyPositiveFace(HexOrientation orientation)
    {
        GridCellPrism rectangular = CreateOfflinePrism(
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(new Fixed64(2), new Fixed64(3), new Fixed64(4)),
            Vector3d.Zero);
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(new Fixed64(3), new Fixed64(5), orientation);
        Fixed64 hexHalfX = orientation == HexOrientation.FlatTop
            ? new Fixed64(3)
            : HexCoordinateUtility.Sqrt3 * new Fixed64(3) * Fixed64.Half;
        Fixed64 hexHalfZ = orientation == HexOrientation.FlatTop
            ? HexCoordinateUtility.Sqrt3 * new Fixed64(3) * Fixed64.Half
            : new Fixed64(3);
        GridCellPrism aabbCorner = CreateOfflinePrism(
            GridTopologyKind.HexPrism,
            hexMetrics,
            new Vector3d(new Fixed64(1) + hexHalfX, Fixed64.Zero, new Fixed64(8)));
        GridCellPrism overlapping = CreateOfflinePrism(
            GridTopologyKind.HexPrism,
            hexMetrics,
            orientation == HexOrientation.FlatTop
                ? new Vector3d(Fixed64.Zero, Fixed64.Zero, new Fixed64(2) + hexHalfZ)
                : new Vector3d(new Fixed64(1) + hexHalfX, Fixed64.Zero, Fixed64.Zero));

        VoxelContactManifold corner = GridCellGeometry.GetContact(rectangular, aabbCorner);
        VoxelContactManifold face = GridCellGeometry.GetContact(rectangular, overlapping);
        VoxelContactManifold reverseFace = GridCellGeometry.GetContact(overlapping, rectangular);

        Assert.Equal(VoxelContactKind.Separated, corner.Kind);
        Assert.Equal(VoxelContactKind.Face, face.Kind);
        Assert.True(face.IsPositiveAreaFace);
        Assert.Equal(VoxelContactKind.Face, reverseFace.Kind);
        Assert.Equal(-face.SourceToTarget, reverseFace.SourceToTarget);
        Assert.Equal(face.CheckedArea, reverseFace.CheckedArea);
    }

    [Fact]
    public void TryGetPrimaryFace_ShouldRejectDiagonalAndAcceptNativePrimaryDirections()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 1, 1)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(GridCellGeometry.TryGetPrimaryFace(
            grid,
            new VoxelIndex(0, 0, 0),
            new VoxelIndex(1, 0, 0),
            out VoxelContactManifold primary));
        Assert.Equal(VoxelContactKind.Face, primary.Kind);
        Assert.False(GridCellGeometry.TryGetPrimaryFace(
            grid,
            new VoxelIndex(0, 0, 0),
            new VoxelIndex(1, 1, 0),
            out _));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void TryGetPrimaryFace_ShouldExposeAllHexPlanarAndVerticalFaces(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation);
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(Vector3d.Zero, metrics, new VoxelIndex(2, 1, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelIndex source = new VoxelIndex(1, 0, 1);
        VoxelIndex[] primaryTargets =
        {
            new VoxelIndex(2, 0, 1),
            new VoxelIndex(2, 0, 0),
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(0, 0, 1),
            new VoxelIndex(0, 0, 2),
            new VoxelIndex(1, 0, 2),
            new VoxelIndex(1, 1, 1)
        };

        foreach (VoxelIndex target in primaryTargets)
        {
            Assert.True(GridCellGeometry.TryGetPrimaryFace(grid, source, target, out VoxelContactManifold face));
            Assert.Equal(VoxelContactKind.Face, face.Kind);
            Assert.True(face.IsPositiveAreaFace);
        }

        Assert.False(GridCellGeometry.TryGetPrimaryFace(
            grid,
            source,
            new VoxelIndex(2, 1, 1),
            out _));
    }

    [Fact]
    public void BulkContacts_ShouldBeCanonicalSparseAwareExactAndAllocationFreeAfterWarmup()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0)),
            out ushort sourceIndex));
        GridConfiguration sparseTarget = new GridConfiguration(
            new Vector3d(2, 0, 0),
            new Vector3d(2, 0, 1),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            sparseTarget,
            new[] { new VoxelIndex(0, 0, 1), new VoxelIndex(0, 0, 0) },
            out ushort targetIndex));

        VoxelGrid source = _world.ActiveGrids[sourceIndex];
        VoxelGrid target = _world.ActiveGrids[targetIndex];
        SwiftList<VoxelContactManifold> results = new SwiftList<VoxelContactManifold>(4);
        GridContactQueryScratch scratch = new GridContactQueryScratch(4, 8);
        GridCellGeometry.GetExactBoundaryContactsInto(source, target, results, scratch);

        Assert.Equal(2, results.Count);
        Assert.Equal(
            new[] { VoxelContactKind.Face, VoxelContactKind.Edge },
            results.Select(result => result.Kind));
        Assert.Equal(new VoxelIndex(1, 0, 0), results[0].Source.VoxelIndex);
        Assert.Equal(new VoxelIndex(0, 0, 0), results[0].Target.VoxelIndex);
        Assert.Equal(new VoxelIndex(0, 0, 1), results[1].Target.VoxelIndex);

        long before = GC.GetAllocatedBytesForCurrentThread();
        GridCellGeometry.GetExactBoundaryContactsInto(source, target, results, scratch);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BulkContacts_ShouldIncludeAllocatedInteriorSparseSourceVoxels()
    {
        GridConfiguration sparseSource = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(4, 4, 4),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            sparseSource,
            new[] { new VoxelIndex(2, 2, 2) },
            out ushort sourceIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(3, 2, 2), new Vector3d(3, 2, 2)),
            out ushort targetIndex));

        VoxelGrid source = _world.ActiveGrids[sourceIndex];
        VoxelGrid target = _world.ActiveGrids[targetIndex];
        SwiftList<VoxelContactManifold> results = new SwiftList<VoxelContactManifold>(1);
        GridContactQueryScratch scratch = new GridContactQueryScratch(1, 2);

        Assert.Equal(1, GridCellGeometry.GetExactBoundaryContactsInto(source, target, results, scratch));
        Assert.Equal(VoxelContactKind.Face, results[0].Kind);
        Assert.Equal(new VoxelIndex(2, 2, 2), results[0].Source.VoxelIndex);
    }

    [Fact]
    public void BulkContacts_ShouldIncludeEmbeddedDenseContactsInCanonicalOrderBothWays()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(4, 4, 4)),
            out ushort largeGridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(2, 2, 2), new Vector3d(2, 2, 2)),
            out ushort embeddedGridIndex));

        VoxelGrid largeGrid = _world.ActiveGrids[largeGridIndex];
        VoxelGrid embeddedGrid = _world.ActiveGrids[embeddedGridIndex];
        SwiftList<VoxelContactManifold> forward = new SwiftList<VoxelContactManifold>(32);
        SwiftList<VoxelContactManifold> reverse = new SwiftList<VoxelContactManifold>(32);
        GridContactQueryScratch forwardScratch = new GridContactQueryScratch(32, 32);
        GridContactQueryScratch reverseScratch = new GridContactQueryScratch(32, 32);

        Assert.Equal(27, GridCellGeometry.GetExactBoundaryContactsInto(
            largeGrid,
            embeddedGrid,
            forward,
            forwardScratch));
        Assert.Equal(27, GridCellGeometry.GetExactBoundaryContactsInto(
            embeddedGrid,
            largeGrid,
            reverse,
            reverseScratch));

        Assert.Contains(
            forward,
            contact => contact.Source.VoxelIndex == new VoxelIndex(2, 2, 2)
                && contact.Target.VoxelIndex == default
                && contact.Kind == VoxelContactKind.VolumeOverlap);
        AssertCanonicalOrder(forward);
        AssertCanonicalOrder(reverse);

        for (int i = 0; i < forward.Count; i++)
        {
            VoxelContactManifold forwardContact = forward[i];
            VoxelContactManifold reverseContact = reverse[i];
            Assert.Equal(forwardContact.Source.VoxelIndex, reverseContact.Target.VoxelIndex);
            Assert.Equal(forwardContact.Target.VoxelIndex, reverseContact.Source.VoxelIndex);
            Assert.Equal(forwardContact.Kind, reverseContact.Kind);
            Assert.Equal(-forwardContact.SourceToTarget, reverseContact.SourceToTarget);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        GridCellGeometry.GetExactBoundaryContactsInto(largeGrid, embeddedGrid, forward, forwardScratch);
        GridCellGeometry.GetExactBoundaryContactsInto(embeddedGrid, largeGrid, reverse, reverseScratch);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static void AssertCanonicalOrder(SwiftList<VoxelContactManifold> contacts)
    {
        for (int i = 1; i < contacts.Count; i++)
        {
            VoxelContactManifold previous = contacts[i - 1];
            VoxelContactManifold current = contacts[i];
            int sourceComparison = previous.Source.VoxelIndex.CompareTo(current.Source.VoxelIndex);
            Assert.True(
                sourceComparison < 0
                || sourceComparison == 0
                && previous.Target.VoxelIndex.CompareTo(current.Target.VoxelIndex) < 0);
        }
    }

    private static GridCellPrism CreateOfflinePrism(
        GridTopologyKind kind,
        GridTopologyMetrics metrics,
        Vector3d center)
    {
        Assert.True(GridCellGeometry.TryCreatePrism(kind, metrics, center, default, out GridCellPrism prism));
        return prism;
    }

    private static Fixed64 GetExactConservativeClearance(
        Vector2d point,
        ReadOnlySpan<Vector2d> polygon)
    {
        long minimumRaw = long.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2d start = polygon[i];
            Vector2d end = polygon[(i + 1) % polygon.Length];
            BigInteger edgeX = (BigInteger)end.X.m_rawValue - start.X.m_rawValue;
            BigInteger edgeY = (BigInteger)end.Y.m_rawValue - start.Y.m_rawValue;
            BigInteger pointX = (BigInteger)point.X.m_rawValue - start.X.m_rawValue;
            BigInteger pointY = (BigInteger)point.Y.m_rawValue - start.Y.m_rawValue;
            BigInteger cross = BigInteger.Abs((edgeX * pointY) - (edgeY * pointX));
            BigInteger edgeSquared = (edgeX * edgeX) + (edgeY * edgeY);
            BigInteger crossSquared = cross * cross;
            long low = 0;
            long high = minimumRaw;
            while (low < high)
            {
                long difference = high - low;
                long middle = low + (difference >> 1) + (difference & 1L);
                BigInteger scaledEdgeSquared = (BigInteger)middle * middle * edgeSquared;
                if (scaledEdgeSquared <= crossSquared)
                    low = middle;
                else
                    high = middle - 1L;
            }

            minimumRaw = low;
        }

        return Fixed64.FromRaw(minimumRaw);
    }

    private static Vector2d[] GetFootprint(GridCellPrism prism) =>
        Enumerable.Range(0, prism.FootprintVertexCount)
            .Select(prism.GetFootprintVertex)
            .ToArray();

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
