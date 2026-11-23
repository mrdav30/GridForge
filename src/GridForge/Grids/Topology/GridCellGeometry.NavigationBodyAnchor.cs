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
    /// <param name="prism">The exact cell prism. A default prism has no valid footprint.</param>
    /// <param name="foot">The body foot position in world space.</param>
    /// <param name="horizontalRadius">The nonnegative upright body radius in X/Z world units. Zero represents a point footprint.</param>
    /// <param name="bodyHeight">The strictly positive upright body height in world units, extending upward from the foot.</param>
    /// <param name="selectedPortal">The selected portal used for face clearance at this anchor.</param>
    /// <returns>True when the body fits the prism and selected portal clearance; false for invalid, unrepresentable, or insufficient geometry.</returns>
    /// <exception cref="System.ArgumentException">The horizontal radius is negative or body height is not positive.</exception>
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
