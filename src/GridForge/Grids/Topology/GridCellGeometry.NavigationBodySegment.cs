//=======================================================================
// GridCellGeometry.NavigationBodySegment.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FixedMathSharp.Geometry;

namespace GridForge.Grids.Topology;

public static partial class GridCellGeometry
{
    /// <summary>
    /// Attempts to certify where one straight body-foot segment traverses an exact directed portal.
    /// </summary>
    /// <remarks>
    /// Vertical portals return a directed source/target enclosure around the exact crossing.
    /// Horizontal portals return the ordered parameters of the exact profile anchors. Both forms
    /// certify the body against the authored source/target prism pair.
    /// </remarks>
    public static bool TryGetNavigationPortalTraversalParameters(
        in GridCellPrism sourcePrism,
        in GridCellPrism targetPrism,
        in GridNavigationPortal portal,
        Vector3d footStart,
        Vector3d footEnd,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        out Fixed64 sourceParameter,
        out Fixed64 targetParameter)
    {
        SwiftThrowHelper.ThrowIfArgument(
            horizontalRadius < Fixed64.Zero,
            nameof(horizontalRadius),
            "Horizontal radius must be nonnegative.");
        SwiftThrowHelper.ThrowIfArgument(
            bodyHeight <= Fixed64.Zero,
            nameof(bodyHeight),
            "Body height must be positive.");

        if (!TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal expectedPortal)
            || !AreSamePortal(portal, expectedPortal)
            || !portal.TryResolveProfile(
                horizontalRadius,
                bodyHeight,
                out Vector3d sourceAnchor,
                out Vector3d targetAnchor))
        {
            sourceParameter = default;
            targetParameter = default;
            return false;
        }

        return TryGetCompiledNavigationPortalTraversalParameters(
            sourcePrism,
            targetPrism,
            portal,
            sourceAnchor,
            targetAnchor,
            footStart,
            footEnd,
            horizontalRadius,
            bodyHeight,
            out sourceParameter,
            out targetParameter);
    }

    internal static bool TryGetCompiledNavigationPortalTraversalParameters(
        in GridCellPrism sourcePrism,
        in GridCellPrism targetPrism,
        in GridNavigationPortal portal,
        Vector3d sourceAnchor,
        Vector3d targetAnchor,
        Vector3d footStart,
        Vector3d footEnd,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        out Fixed64 sourceParameter,
        out Fixed64 targetParameter)
    {
        sourceParameter = default;
        targetParameter = default;
        if (portal.FaceKind == VoxelContactFaceKind.Vertical)
        {
            FixedSegment2d path = new(
                new Vector2d(footStart.X, footStart.Z),
                new Vector2d(footEnd.X, footEnd.Z));
            FixedSegment2d opening = new(
                portal.VerticalFaceSegmentStart,
                portal.VerticalFaceSegmentEnd);
            if (!IsDirectedPortalCrossing(sourcePrism, path, opening)
                || !path.TryGetUniqueIntersectionParameterEnclosure(
                    opening,
                    out _,
                    out sourceParameter,
                    out targetParameter))
            {
                sourceParameter = default;
                targetParameter = default;
                return false;
            }

            Vector3d sourcePoint = Vector3d.Lerp(footStart, footEnd, sourceParameter);
            Vector3d targetPoint = Vector3d.Lerp(footStart, footEnd, targetParameter);
            FixedSegment2d traversalGap = new(
                new Vector2d(sourcePoint.X, sourcePoint.Z),
                new Vector2d(targetPoint.X, targetPoint.Z));
            path.TryGetCapsuleIntersectionParameterEnclosure(
                opening,
                horizontalRadius,
                out Fixed64 overlapEntry,
                out Fixed64 overlapExit);
            if (!IsPortalTraversalGapPlanarValid(
                    sourcePrism,
                    traversalGap,
                    horizontalRadius,
                    portal)
                || !IsPortalTraversalGapPlanarValid(
                    targetPrism,
                    traversalGap,
                    horizontalRadius,
                    portal)
                || !IsPortalHeightValidOverInterval(
                    footStart.Y,
                    footEnd.Y,
                    overlapEntry,
                    overlapExit,
                    bodyHeight,
                    portal))
            {
                sourceParameter = default;
                targetParameter = default;
                return false;
            }

            return true;
        }

        if (!TryGetPointParameter(footStart, footEnd, sourceAnchor, out sourceParameter)
            || !TryGetPointParameter(footStart, footEnd, targetAnchor, out targetParameter)
            || sourceParameter >= targetParameter)
        {
            sourceParameter = default;
            targetParameter = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a cylindrical body can sweep one straight foot segment through an exact
    /// cell prism, optionally approaching an incoming and outgoing vertical portal.
    /// </summary>
    /// <remarks>
    /// The horizontal capsule is compared exactly with every blocked wall span. Selected portal
    /// openings retain their own vertical authority and apply only while the sweep overlaps that
    /// opening. Each selected portal must cover its complete possible overlap; a segment that would
    /// need to switch height authority between two same-wall openings is rejected. A non-default
    /// endpoint allowance clips one exact directed footprint-edge crossing inside GridForge before
    /// validating the retained in-prism segment. The method retains no state and allocates nothing.
    /// </remarks>
    public static bool IsNavigationBodySegmentValid(
        in GridCellPrism prism,
        Vector3d footStart,
        Vector3d footEnd,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        in GridNavigationPortal incomingPortal,
        in GridNavigationPortal outgoingPortal,
        GridNavigationBodySegmentEndpointAllowance endpointAllowance)
    {
        SwiftThrowHelper.ThrowIfArgument(
            horizontalRadius < Fixed64.Zero,
            nameof(horizontalRadius),
            "Horizontal radius must be nonnegative.");
        SwiftThrowHelper.ThrowIfArgument(
            bodyHeight <= Fixed64.Zero,
            nameof(bodyHeight),
            "Body height must be positive.");

        if (endpointAllowance != GridNavigationBodySegmentEndpointAllowance.None
            && endpointAllowance != GridNavigationBodySegmentEndpointAllowance.StartFootprintEdge
            && endpointAllowance != GridNavigationBodySegmentEndpointAllowance.EndFootprintEdge)
        {
            throw new System.ArgumentOutOfRangeException(nameof(endpointAllowance));
        }

        if (!IsNavigationPrismValid(prism))
            return false;

        int allowedEdgeIndex = -1;
        if (endpointAllowance == GridNavigationBodySegmentEndpointAllowance.StartFootprintEdge)
        {
            if (incomingPortal.IsValid
                || !TryClipNavigationBodySegmentEndpoint(
                    prism,
                    footStart,
                    footEnd,
                    bodyHeight,
                    clipStart: true,
                    out footStart,
                    out allowedEdgeIndex))
            {
                return false;
            }
        }
        else if (endpointAllowance == GridNavigationBodySegmentEndpointAllowance.EndFootprintEdge)
        {
            if (outgoingPortal.IsValid
                || !TryClipNavigationBodySegmentEndpoint(
                    prism,
                    footStart,
                    footEnd,
                    bodyHeight,
                    clipStart: false,
                    out footEnd,
                    out allowedEdgeIndex))
            {
                return false;
            }
        }

        return IsNavigationBodySegmentValidCore(
            prism,
            footStart,
            footEnd,
            horizontalRadius,
            bodyHeight,
            incomingPortal,
            outgoingPortal,
            allowedEdgeIndex);
    }

    private static bool IsNavigationBodySegmentValidCore(
        in GridCellPrism prism,
        Vector3d footStart,
        Vector3d footEnd,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        in GridNavigationPortal incomingPortal,
        in GridNavigationPortal outgoingPortal,
        int allowedEdgeIndex)
    {
        if (!prism.Contains(footStart)
            || !prism.Contains(footEnd)
            || !Fixed64.TryAdd(footStart.Y, bodyHeight, out Fixed64 startTop)
            || !Fixed64.TryAdd(footEnd.Y, bodyHeight, out Fixed64 endTop)
            || startTop > prism.VerticalMax
            || endTop > prism.VerticalMax)
        {
            return false;
        }

        FixedSegment2d path = new(
            new Vector2d(footStart.X, footStart.Z),
            new Vector2d(footEnd.X, footEnd.Z));
        for (int edgeIndex = 0; edgeIndex < prism.FootprintVertexCount; edgeIndex++)
        {
            if (edgeIndex == allowedEdgeIndex)
                continue;
            Vector2d edgeStart = prism.GetFootprintVertex(edgeIndex);
            Vector2d edgeEnd = prism.GetFootprintVertex(
                (edgeIndex + 1) % prism.FootprintVertexCount);
            FixedSegment2d edge = new(edgeStart, edgeEnd);
            if (path.IsDistanceAtLeast(edge, horizontalRadius))
                continue;

            bool hasFirst = TryGetActiveOpening(
                edge,
                path,
                footStart.Y,
                footEnd.Y,
                horizontalRadius,
                bodyHeight,
                incomingPortal,
                edgeStart,
                out Vector2d firstStart,
                out Vector2d firstEnd);
            bool hasSecond = TryGetActiveOpening(
                edge,
                path,
                footStart.Y,
                footEnd.Y,
                horizontalRadius,
                bodyHeight,
                outgoingPortal,
                edgeStart,
                out Vector2d secondStart,
                out Vector2d secondEnd);
            if (!hasFirst && !hasSecond)
                return false;
            if (!hasFirst)
            {
                firstStart = secondStart;
                firstEnd = secondEnd;
                hasSecond = false;
            }
            else if (hasSecond
                && CompareDistanceFrom(edgeStart, secondStart, firstStart) < 0)
            {
                Swap(ref firstStart, ref secondStart);
                Swap(ref firstEnd, ref secondEnd);
            }

            if (!path.IsDistanceAtLeast(
                    new FixedSegment2d(edgeStart, firstStart),
                    horizontalRadius))
            {
                return false;
            }

            if (!hasSecond
                || CompareDistanceFrom(edgeStart, secondStart, firstEnd) <= 0)
            {
                if (hasSecond
                    && CompareDistanceFrom(edgeStart, secondEnd, firstEnd) > 0)
                {
                    firstEnd = secondEnd;
                }

                if (!path.IsDistanceAtLeast(
                        new FixedSegment2d(firstEnd, edgeEnd),
                        horizontalRadius))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryClipNavigationBodySegmentEndpoint(
        in GridCellPrism prism,
        Vector3d footStart,
        Vector3d footEnd,
        Fixed64 bodyHeight,
        bool clipStart,
        out Vector3d clippedEndpoint,
        out int allowedEdgeIndex)
    {
        clippedEndpoint = default;
        allowedEdgeIndex = -1;
        FixedSegment2d path = new(
            new Vector2d(footStart.X, footStart.Z),
            new Vector2d(footEnd.X, footEnd.Z));
        if (path.Start == path.End)
            return false;

        Vector2d center = new(prism.Center.X, prism.Center.Z);
        Fixed64 lowerParameter = default;
        Fixed64 upperParameter = default;
        for (int edgeIndex = 0; edgeIndex < prism.FootprintVertexCount; edgeIndex++)
        {
            Vector2d edgeStart = prism.GetFootprintVertex(edgeIndex);
            Vector2d edgeEnd = prism.GetFootprintVertex(
                (edgeIndex + 1) % prism.FootprintVertexCount);
            FixedSegment2d edge = new(edgeStart, edgeEnd);
            int centerSide = Vector2d.OrientationSign(edgeStart, edgeEnd, center);
            int startSide = Vector2d.OrientationSign(edgeStart, edgeEnd, path.Start);
            int endSide = Vector2d.OrientationSign(edgeStart, edgeEnd, path.End);
            bool directed = clipStart
                ? endSide == centerSide && startSide != centerSide
                : startSide == centerSide && endSide != centerSide;
            if (!directed)
                continue;
            if (!path.TryGetUniqueIntersectionParameterEnclosure(
                    edge,
                    out _,
                    out Fixed64 candidateLower,
                    out Fixed64 candidateUpper))
            {
                continue;
            }
            if (new FixedSegment2d(edgeStart, edgeStart).TryGetUniqueIntersection(path, out _)
                || new FixedSegment2d(edgeEnd, edgeEnd).TryGetUniqueIntersection(path, out _))
            {
                return false;
            }
            allowedEdgeIndex = edgeIndex;
            lowerParameter = candidateLower;
            upperParameter = candidateUpper;
        }

        if (allowedEdgeIndex < 0
            || !IsBodyHeightValidOverInterval(
                footStart.Y,
                footEnd.Y,
                lowerParameter,
                upperParameter,
                bodyHeight,
                prism.VerticalMin,
                prism.VerticalMax))
        {
            return false;
        }

        Fixed64 containedParameter = clipStart ? upperParameter : lowerParameter;
        clippedEndpoint = Vector3d.Lerp(footStart, footEnd, containedParameter);
        return prism.Contains(clippedEndpoint);
    }

    private static bool TryGetActiveOpening(
        FixedSegment2d edge,
        FixedSegment2d path,
        Fixed64 footStartY,
        Fixed64 footEndY,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        in GridNavigationPortal portal,
        Vector2d edgeStart,
        out Vector2d openingStart,
        out Vector2d openingEnd)
    {
        openingStart = default;
        openingEnd = default;
        if (!portal.IsValid
            || portal.FaceKind != VoxelContactFaceKind.Vertical
            || horizontalRadius > portal.MaximumHorizontalRadius
            || bodyHeight > portal.MaximumBodyHeight
            || !IsPortalCertifiedOnEdge(edge, portal))
        {
            return false;
        }

        openingStart = portal.VerticalFaceSegmentStart;
        openingEnd = portal.VerticalFaceSegmentEnd;
        if (CompareDistanceFrom(edgeStart, openingEnd, openingStart) < 0)
            Swap(ref openingStart, ref openingEnd);

        FixedSegment2d opening = new(openingStart, openingEnd);
        if (!path.TryGetCapsuleIntersectionParameterEnclosure(
                opening,
                horizontalRadius,
                out Fixed64 entry,
                out Fixed64 exit))
        {
            return false;
        }

        return IsPortalHeightValidOverInterval(
            footStartY,
            footEndY,
            entry,
            exit,
            bodyHeight,
            portal);
    }

    private static bool IsDirectedPortalCrossing(
        in GridCellPrism sourcePrism,
        FixedSegment2d path,
        FixedSegment2d opening)
    {
        if (path.Start == path.End)
            return false;

        Vector2d sourceCenter = new(sourcePrism.Center.X, sourcePrism.Center.Z);
        int sourceSide = Vector2d.OrientationSign(opening.Start, opening.End, sourceCenter);
        int startSide = Vector2d.OrientationSign(opening.Start, opening.End, path.Start);
        int endSide = Vector2d.OrientationSign(opening.Start, opening.End, path.End);
        return (startSide == 0 || startSide == sourceSide)
            && (endSide == 0 || endSide == -sourceSide)
            && (startSide != 0 || endSide != 0);
    }

    private static bool IsPortalTraversalGapPlanarValid(
        in GridCellPrism prism,
        FixedSegment2d traversalGap,
        Fixed64 horizontalRadius,
        in GridNavigationPortal portal)
    {
        for (int edgeIndex = 0; edgeIndex < prism.FootprintVertexCount; edgeIndex++)
        {
            Vector2d edgeStart = prism.GetFootprintVertex(edgeIndex);
            Vector2d edgeEnd = prism.GetFootprintVertex(
                (edgeIndex + 1) % prism.FootprintVertexCount);
            FixedSegment2d edge = new(edgeStart, edgeEnd);
            if (!IsPortalCertifiedOnEdge(edge, portal))
            {
                if (!traversalGap.IsDistanceAtLeast(
                        edge,
                        horizontalRadius))
                {
                    return false;
                }
                continue;
            }

            Vector2d openingStart = portal.VerticalFaceSegmentStart;
            Vector2d openingEnd = portal.VerticalFaceSegmentEnd;
            if (CompareDistanceFrom(edgeStart, openingEnd, openingStart) < 0)
                Swap(ref openingStart, ref openingEnd);
            if (!traversalGap.IsDistanceAtLeast(
                    new FixedSegment2d(edgeStart, openingStart),
                    horizontalRadius)
                || !traversalGap.IsDistanceAtLeast(
                    new FixedSegment2d(openingEnd, edgeEnd),
                    horizontalRadius))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPortalHeightValidOverInterval(
        Fixed64 footStartY,
        Fixed64 footEndY,
        Fixed64 entryParameter,
        Fixed64 exitParameter,
        Fixed64 bodyHeight,
        in GridNavigationPortal portal)
    {
        Fixed64 portalTop = portal.CanonicalFacePoint.Y + portal.MaximumBodyHeight;
        return IsBodyHeightValidOverInterval(
                footStartY,
                footEndY,
                entryParameter,
                exitParameter,
                bodyHeight,
                portal.CanonicalFacePoint.Y,
                portalTop);
    }

    private static bool IsBodyHeightValidOverInterval(
        Fixed64 footStartY,
        Fixed64 footEndY,
        Fixed64 entryParameter,
        Fixed64 exitParameter,
        Fixed64 bodyHeight,
        Fixed64 verticalMin,
        Fixed64 verticalMax)
    {
        GetConservativeLerpBounds(
            footStartY,
            footEndY,
            entryParameter,
            out Fixed64 lowerStartY,
            out Fixed64 upperStartY);
        GetConservativeLerpBounds(
            footStartY,
            footEndY,
            exitParameter,
            out Fixed64 lowerEndY,
            out Fixed64 upperEndY);
        Fixed64 minimumFootY = FixedMath.Min(lowerStartY, lowerEndY);
        Fixed64 maximumFootY = FixedMath.Max(upperStartY, upperEndY);
        return Fixed64.TryAdd(maximumFootY, bodyHeight, out Fixed64 maximumTop)
            && minimumFootY >= verticalMin
            && maximumTop <= verticalMax;
    }

    private static void GetConservativeLerpBounds(
        Fixed64 start,
        Fixed64 end,
        Fixed64 parameter,
        out Fixed64 lower,
        out Fixed64 upper)
    {
        lower = FixedMath.Lerp(start, end, parameter);
        upper = lower;
        if (start == end || parameter == Fixed64.Zero || parameter == Fixed64.One)
            return;
        if (lower > Fixed64.MinValue)
            lower = Fixed64.FromRaw(lower.m_rawValue - 1L);
        if (upper < Fixed64.MaxValue)
            upper = Fixed64.FromRaw(upper.m_rawValue + 1L);
    }

    private static bool TryGetPointParameter(
        Vector3d segmentStart,
        Vector3d segmentEnd,
        Vector3d point,
        out Fixed64 parameter)
    {
        parameter = default;
        FixedSegment segment = new(segmentStart, segmentEnd);
        if (!segment.Contains(point) || segmentStart.Y == segmentEnd.Y)
            return false;

        FixedSegment2d vertical = new(
            new Vector2d(segmentStart.Y, Fixed64.Zero),
            new Vector2d(segmentEnd.Y, Fixed64.Zero));
        FixedSegment2d verticalPoint = new(
            new Vector2d(point.Y, Fixed64.Zero),
            new Vector2d(point.Y, Fixed64.Zero));
        return vertical.TryGetUniqueIntersection(verticalPoint, out parameter);
    }

    private static bool AreSamePortal(
        in GridNavigationPortal first,
        in GridNavigationPortal second)
    {
        return first.FaceKind == second.FaceKind
            && first.SourceToTarget == second.SourceToTarget
            && first.CanonicalFacePoint == second.CanonicalFacePoint
            && first.MaximumHorizontalRadius == second.MaximumHorizontalRadius
            && first.MaximumBodyHeight == second.MaximumBodyHeight
            && first.VerticalFaceSegmentStart == second.VerticalFaceSegmentStart
            && first.VerticalFaceSegmentEnd == second.VerticalFaceSegmentEnd;
    }

    private static int CompareDistanceFrom(
        Vector2d origin,
        Vector2d first,
        Vector2d second)
    {
        return Vector2d.CompareDistanceSquared(origin, first, origin, second);
    }

    private static void Swap(ref Vector2d first, ref Vector2d second)
    {
        Vector2d value = first;
        first = second;
        second = value;
    }

    internal static bool HasPositiveNavigationBodyPrismOverlap(
        in GridCellPrism prism,
        Vector3d footStart,
        Vector3d footEnd,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight)
    {
        Fixed64 prismHalfThickness = prism.VerticalMax - prism.Center.Y;

        Span<Vector2d> offsets = stackalloc Vector2d[6];
        Vector2d planarOrigin = new(prism.Center.X, prism.Center.Z);
        for (int i = 0; i < prism.FootprintVertexCount; i++)
            offsets[i] = prism.GetFootprintVertex(i) - planarOrigin;

        return FixedConvexPrismRelations.IntersectsSweptUprightCylinderStrict(
            footStart,
            footEnd,
            horizontalRadius,
            bodyHeight,
            prism.Center,
            Fixed64.Zero,
            offsets[..prism.FootprintVertexCount],
            prismHalfThickness);
    }

    internal static bool TryGetPlanarSegmentInterval(
        in GridCellPrism prism,
        Vector2d start,
        Vector2d end,
        out Fixed64 overlapEnter,
        out Fixed64 overlapExit)
    {
        Vector2d origin = new(prism.Center.X, prism.Center.Z);
        Span<Vector2d> vertices = stackalloc Vector2d[6];
        Span<Vector2d> offsets = stackalloc Vector2d[6];
        prism.CopyFootprintTo(vertices);
        for (int i = 0; i < prism.FootprintVertexCount; i++)
            offsets[i] = vertices[i] - origin;
        ReadOnlySpan<Vector2d> footprint = offsets[..prism.FootprintVertexCount];

        bool startContained = FixedConvex2dRelations.ContainsPoint(start, origin, footprint);
        if (start == end)
        {
            overlapEnter = Fixed64.Zero;
            overlapExit = Fixed64.One;
            return startContained;
        }

        Span<Fixed64> parameters = stackalloc Fixed64[16];
        int count = 0;
        if (startContained)
            parameters[count++] = Fixed64.Zero;
        if (FixedConvex2dRelations.ContainsPoint(end, origin, footprint))
            parameters[count++] = Fixed64.One;

        FixedSegment2d path = new(start, end);
        for (int i = 0; i < prism.FootprintVertexCount; i++)
        {
            FixedSegment2d edge = new(
                vertices[i],
                vertices[(i + 1) % prism.FootprintVertexCount]);
            if (path.TryGetUniqueIntersection(edge, out Fixed64 parameter))
                AddNavigationBodyParameter(parameters, ref count, parameter);
            if (Vector2d.OrientationSign(start, end, edge.Start) == 0
                && Vector2d.OrientationSign(start, end, edge.End) == 0)
            {
                AddNavigationBodyParameter(parameters, ref count, GetNavigationBodyParameter(path, edge.Start));
                AddNavigationBodyParameter(parameters, ref count, GetNavigationBodyParameter(path, edge.End));
            }
        }

        if (count == 0)
        {
            overlapEnter = default;
            overlapExit = default;
            return false;
        }

        overlapEnter = parameters[0];
        overlapExit = parameters[0];
        for (int i = 1; i < count; i++)
        {
            overlapEnter = FixedMath.Min(overlapEnter, parameters[i]);
            overlapExit = FixedMath.Max(overlapExit, parameters[i]);
        }
        return true;
    }

    private static void AddNavigationBodyParameter(
        Span<Fixed64> parameters,
        ref int count,
        Fixed64 parameter)
    {
        if ((ulong)parameter.m_rawValue > (ulong)Fixed64.One.m_rawValue)
            return;
        for (int i = 0; i < count; i++)
        {
            if (parameters[i] == parameter)
                return;
        }
        parameters[count++] = parameter;
    }

    private static Fixed64 GetNavigationBodyParameter(FixedSegment2d path, Vector2d point)
    {
        Vector2d delta = path.Delta;
        return FixedMath.Abs(delta.X) >= FixedMath.Abs(delta.Y)
            ? (point.X - path.Start.X) / delta.X
            : (point.Y - path.Start.Y) / delta.Y;
    }

}
