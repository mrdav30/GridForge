//=======================================================================
// GridCellGeometry.NavigationCorridor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace GridForge.Grids.Topology;

public static partial class GridCellGeometry
{
    /// <summary>
    /// Validates a canonical, clearance-bearing corridor through an ordered chain of exact cell prisms.
    /// </summary>
    /// <remarks>
    /// Consecutive cells must share a positive-area face. Vertical faces contribute one portal-center
    /// waypoint. Horizontal faces contribute the last point wholly contained by the source cell and the
    /// first point wholly contained by the target cell. The resulting polyline and checked length are
    /// deterministic and independent of runtime grid identity.
    /// </remarks>
    /// <param name="orderedCells">Source cell, zero or more witness cells, and destination cell.</param>
    /// <param name="entryAnchor">Foot position inside the source cell.</param>
    /// <param name="exitAnchor">Foot position inside the destination cell.</param>
    /// <param name="radiusClearance">Required horizontal body radius.</param>
    /// <param name="heightClearance">Required positive body height.</param>
    /// <param name="portalWaypoints">Caller-owned storage with capacity for twice the portal count.</param>
    /// <param name="portalWaypointCount">The number of canonical portal waypoints written.</param>
    /// <param name="geometricCost">The checked fixed-point length of the canonical polyline.</param>
    public static bool TryValidateNavigationCorridor(
        ReadOnlySpan<GridCellPrism> orderedCells,
        Vector3d entryAnchor,
        Vector3d exitAnchor,
        Fixed64 radiusClearance,
        Fixed64 heightClearance,
        Span<Vector3d> portalWaypoints,
        out int portalWaypointCount,
        out Fixed64 geometricCost)
    {
        portalWaypointCount = 0;
        geometricCost = default;
        if (orderedCells.Length < 2
            || radiusClearance < Fixed64.Zero
            || heightClearance <= Fixed64.Zero
            || orderedCells.Length - 1 > portalWaypoints.Length / 2)
        {
            return false;
        }

        for (int i = 0; i < orderedCells.Length; i++)
        {
            if (orderedCells[i].FootprintVertexCount is not 4 and not 6
                || orderedCells[i].VerticalMax < orderedCells[i].VerticalMin)
            {
                return false;
            }
        }

        Vector3d previousPoint = entryAnchor;
        for (int i = 0; i + 1 < orderedCells.Length; i++)
        {
            if (!TryGetNavigationPortal(
                    orderedCells[i],
                    orderedCells[i + 1],
                    radiusClearance,
                    heightClearance,
                    out Vector3d sourcePoint,
                    out Vector3d targetPoint,
                    out int pointCount))
            {
                portalWaypointCount = 0;
                geometricCost = default;
                return false;
            }

            portalWaypoints[portalWaypointCount++] = sourcePoint;
            if (!TryAccumulateDistance(previousPoint, sourcePoint, ref geometricCost))
            {
                portalWaypointCount = 0;
                geometricCost = default;
                return false;
            }

            previousPoint = sourcePoint;
            if (pointCount == 2)
            {
                portalWaypoints[portalWaypointCount++] = targetPoint;
                if (!TryAccumulateDistance(previousPoint, targetPoint, ref geometricCost))
                {
                    portalWaypointCount = 0;
                    geometricCost = default;
                    return false;
                }

                previousPoint = targetPoint;
            }
        }

        if (!TryAccumulateDistance(previousPoint, exitAnchor, ref geometricCost))
        {
            portalWaypointCount = 0;
            geometricCost = default;
            return false;
        }

        for (int i = 0; i < orderedCells.Length; i++)
        {
            Vector3d localStart = i == 0
                ? entryAnchor
                : GetTargetPortalPoint(orderedCells[i - 1], orderedCells[i], radiusClearance, heightClearance);
            Vector3d localEnd = i + 1 == orderedCells.Length
                ? exitAnchor
                : GetSourcePortalPoint(orderedCells[i], orderedCells[i + 1], radiusClearance, heightClearance);
            VoxelContactManifold incoming = i == 0
                ? default
                : GetContact(orderedCells[i - 1], orderedCells[i]);
            VoxelContactManifold outgoing = i + 1 == orderedCells.Length
                ? default
                : GetContact(orderedCells[i], orderedCells[i + 1]);

            if (!IsBodyAnchorValid(
                    orderedCells[i],
                    localStart,
                    radiusClearance,
                    heightClearance,
                    incoming.FaceKind == VoxelContactFaceKind.Vertical ? incoming : default)
                || !IsBodyAnchorValid(
                    orderedCells[i],
                    localEnd,
                    radiusClearance,
                    heightClearance,
                    outgoing.FaceKind == VoxelContactFaceKind.Vertical ? outgoing : default))
            {
                portalWaypointCount = 0;
                geometricCost = default;
                return false;
            }
        }

        return true;
    }

    private static Vector3d GetSourcePortalPoint(
        in GridCellPrism source,
        in GridCellPrism target,
        Fixed64 radius,
        Fixed64 height)
    {
        _ = TryGetNavigationPortal(source, target, radius, height, out Vector3d point, out _, out _);
        return point;
    }

    private static Vector3d GetTargetPortalPoint(
        in GridCellPrism source,
        in GridCellPrism target,
        Fixed64 radius,
        Fixed64 height)
    {
        _ = TryGetNavigationPortal(source, target, radius, height, out Vector3d sourcePoint, out Vector3d targetPoint, out int count);
        return count == 1 ? sourcePoint : targetPoint;
    }

    private static bool TryGetNavigationPortal(
        in GridCellPrism source,
        in GridCellPrism target,
        Fixed64 radius,
        Fixed64 height,
        out Vector3d sourcePoint,
        out Vector3d targetPoint,
        out int pointCount)
    {
        sourcePoint = default;
        targetPoint = default;
        pointCount = 0;
        if (!TryCreateNavigationPortal(source, target, out GridNavigationPortal portal)
            || !portal.TryResolveProfile(radius, height, out sourcePoint, out targetPoint))
        {
            return false;
        }

        pointCount = portal.FaceKind == VoxelContactFaceKind.Vertical ? 1 : 2;
        return true;
    }

    private static bool IsBodyAnchorValid(
        in GridCellPrism prism,
        Vector3d foot,
        Fixed64 radius,
        Fixed64 height,
        in VoxelContactManifold exemptPortal)
    {
        if (!prism.Contains(foot)
            || !Fixed64.TryAdd(foot.Y, height, out Fixed64 top)
            || top > prism.VerticalMax)
        {
            return false;
        }

        Vector2d point = new(foot.X, foot.Z);
        for (int i = 0; i < prism.FootprintVertexCount; i++)
        {
            Vector2d start = prism.GetFootprintVertex(i);
            Vector2d end = prism.GetFootprintVertex((i + 1) % prism.FootprintVertexCount);
            if (IsExemptPortalEdge(start, end, exemptPortal))
                continue;

            Vector2d closest = Vector2d.ClosestPointOnLineSegment(point, start, end);
            if (!Vector2d.TryGetDistance(point, closest, out Fixed64 distance) || distance < radius)
                return false;
        }

        return true;
    }

    private static bool IsExemptPortalEdge(
        Vector2d edgeStart,
        Vector2d edgeEnd,
        in VoxelContactManifold portal)
    {
        return portal.FaceKind == VoxelContactFaceKind.Vertical
            && IsPointOnSegment(portal.HorizontalSegmentStart, edgeStart, edgeEnd)
            && IsPointOnSegment(portal.HorizontalSegmentEnd, edgeStart, edgeEnd);
    }

    private static bool IsPointOnSegment(Vector2d point, Vector2d start, Vector2d end)
    {
        return Vector2d.ClosestPointOnLineSegment(point, start, end) == point;
    }

    private static bool TryAccumulateDistance(Vector3d start, Vector3d end, ref Fixed64 total)
    {
        return Vector3d.TryGetDistance(start, end, out Fixed64 distance)
            && Fixed64.TryAdd(total, distance, out total);
    }
}
