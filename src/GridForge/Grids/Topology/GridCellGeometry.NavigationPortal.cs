//=======================================================================
// GridCellGeometry.NavigationPortal.cs
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
    /// Attempts to compile one exact, directed navigation portal from two cell prisms.
    /// </summary>
    /// <remarks>
    /// Contact discovery and convex clipping occur only during compilation. The resulting value
    /// contains no live grid references and resolves body profiles in constant time.
    /// </remarks>
    public static bool TryCreateNavigationPortal(
        in GridCellPrism source,
        in GridCellPrism target,
        out GridNavigationPortal portal)
    {
        portal = default;
        if (!IsNavigationPrismValid(source)
            || !IsNavigationPrismValid(target)
            || !Vector3d.TrySubtract(target.Center, source.Center, out Vector3d sourceToTarget))
        {
            return false;
        }

        VoxelContactManifold contact = GetContact(source, target);
        if (!contact.IsPositiveAreaFace)
            return false;

        if (contact.FaceKind == VoxelContactFaceKind.Vertical)
        {
            if (!TryGetConservativeDistance(
                    contact.HorizontalSegmentStart,
                    contact.HorizontalSegmentEnd,
                    out Fixed64 faceWidth)
                || !Fixed64.TrySubtract(
                    contact.VerticalMax,
                    contact.VerticalMin,
                    out Fixed64 faceHeight)
                || faceWidth <= Fixed64.Zero
                || faceHeight <= Fixed64.Zero)
            {
                return false;
            }

            Vector2d center = Vector2d.Lerp(
                contact.HorizontalSegmentStart,
                contact.HorizontalSegmentEnd,
                Fixed64.Half);
            portal = new GridNavigationPortal(
                VoxelContactFaceKind.Vertical,
                sourceToTarget,
                new Vector3d(center.X, contact.VerticalMin, center.Y),
                Fixed64.FromRaw(faceWidth.m_rawValue >> 1),
                faceHeight);
            return true;
        }

        if (contact.FaceKind != VoxelContactFaceKind.Horizontal
            || sourceToTarget.Y == Fixed64.Zero
            || !Fixed64.TrySubtract(source.VerticalMax, source.VerticalMin, out Fixed64 sourceHeight)
            || !Fixed64.TrySubtract(target.VerticalMax, target.VerticalMin, out Fixed64 targetHeight)
            || sourceHeight <= Fixed64.Zero
            || targetHeight <= Fixed64.Zero)
        {
            return false;
        }

        Span<Vector2d> polygon = stackalloc Vector2d[GridConvexPolygon2d.MaxVertexCount];
        contact.HorizontalPolygon.CopyTo(polygon);
        ReadOnlySpan<Vector2d> footprint = polygon[..contact.HorizontalPolygon.VertexCount];
        if (!FixedConvex2dRelations.TryGetAreaAndCentroid(footprint, out _, out Vector2d centroid)
            || !TryGetMinimumPolygonClearance(footprint, centroid, out Fixed64 maximumRadius))
        {
            return false;
        }

        portal = new GridNavigationPortal(
            VoxelContactFaceKind.Horizontal,
            sourceToTarget,
            new Vector3d(centroid.X, contact.VerticalMin, centroid.Y),
            maximumRadius,
            FixedMath.Min(sourceHeight, targetHeight));
        return true;
    }

    private static bool IsNavigationPrismValid(in GridCellPrism prism)
    {
        if (prism.FootprintVertexCount is not 4 and not 6
            || prism.VerticalMax <= prism.VerticalMin
            || prism.PlanarInradius <= Fixed64.Zero)
        {
            return false;
        }

        Span<Vector2d> footprint = stackalloc Vector2d[6];
        prism.CopyFootprintTo(footprint);
        return FixedConvex2dRelations.IsStrictlyConvex(footprint[..prism.FootprintVertexCount]);
    }

    private static bool TryGetMinimumPolygonClearance(
        ReadOnlySpan<Vector2d> polygon,
        Vector2d point,
        out Fixed64 clearance)
    {
        clearance = Fixed64.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2d closest = Vector2d.ClosestPointOnLineSegment(
                point,
                polygon[i],
                polygon[(i + 1) % polygon.Length]);
            if (!TryGetConservativeDistance(point, closest, out Fixed64 distance))
            {
                clearance = default;
                return false;
            }

            clearance = FixedMath.Min(clearance, distance);
        }

        return clearance != Fixed64.MaxValue;
    }

    private static bool TryGetConservativeDistance(
        Vector2d start,
        Vector2d end,
        out Fixed64 distance)
    {
        if (!Vector2d.TryGetDistance(start, end, out distance))
            return false;

        Vector2d representedDistance = new Vector2d(distance, Fixed64.Zero);
        if (Vector2d.CompareDistanceSquared(start, end, Vector2d.Zero, representedDistance) < 0
            && !Fixed64.TrySubtract(distance, Fixed64.MinIncrement, out distance))
        {
            distance = default;
            return false;
        }

        return true;
    }
}
