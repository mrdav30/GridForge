//=======================================================================
// GridCellGeometry.NavigationBodyAnchor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FixedMathSharp.Geometry;

namespace GridForge.Grids.Topology;

public static partial class GridCellGeometry
{
    /// <summary>
    /// Determines whether one cylindrical body anchor fits an exact cell prism, optionally through
    /// one selected navigation portal.
    /// </summary>
    /// <remarks>
    /// This is the degenerate-segment form of <see cref="IsNavigationBodySegmentValid"/>. The
    /// shared swept-body authority therefore owns all wall, opening, height, and equality behavior.
    /// </remarks>
    public static bool IsNavigationBodyAnchorValid(
        in GridCellPrism prism,
        Vector3d foot,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        in GridNavigationPortal selectedPortal)
    {
        return IsNavigationBodySegmentValid(
            prism,
            foot,
            foot,
            horizontalRadius,
            bodyHeight,
            selectedPortal,
            default,
            GridNavigationBodySegmentEndpointAllowance.None);
    }

    private static bool IsPortalCertifiedOnEdge(
        FixedSegment2d edge,
        in GridNavigationPortal portal)
    {
        FixedSegment2d segmentStart = new(
            portal.VerticalFaceSegmentStart,
            portal.VerticalFaceSegmentStart);
        FixedSegment2d segmentEnd = new(
            portal.VerticalFaceSegmentEnd,
            portal.VerticalFaceSegmentEnd);
        return segmentStart.TryGetUniqueIntersection(edge, out _)
            && segmentEnd.TryGetUniqueIntersection(edge, out _);
    }
}
