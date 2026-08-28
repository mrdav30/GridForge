//=======================================================================
// GridCellGeometry.NavigationPortal.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics;
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
            || !IsNavigationPrismValid(target))
        {
            return false;
        }

        VoxelContactManifold contact = GetContact(source, target);
        if (!contact.IsPositiveAreaFace)
            return false;
        Vector3d sourceToTarget = contact.SourceToTarget;

        if (contact.FaceKind == VoxelContactFaceKind.Vertical)
        {
            Fixed64 faceHeight = contact.VerticalMax - contact.VerticalMin;

            Vector2d center = GetNearestRepresentableSegmentMidpoint(
                contact.HorizontalSegmentStart,
                contact.HorizontalSegmentEnd);
            var canonicalFacePoint = new Vector3d(center.X, contact.VerticalMin, center.Y);
            Fixed64 startClearance = GetConservativeDistance(
                contact.HorizontalSegmentStart,
                center);
            Fixed64 endClearance = GetConservativeDistance(
                center,
                contact.HorizontalSegmentEnd);

            portal = new GridNavigationPortal(
                VoxelContactFaceKind.Vertical,
                sourceToTarget,
                canonicalFacePoint,
                FixedMath.Min(startClearance, endClearance),
                faceHeight,
                contact.HorizontalSegmentStart,
                contact.HorizontalSegmentEnd);
            return true;
        }

        Fixed64 sourceHeight = source.VerticalMax - source.VerticalMin;
        Fixed64 targetHeight = target.VerticalMax - target.VerticalMin;

        Span<Vector2d> polygon = stackalloc Vector2d[GridConvexPolygon2d.MaxVertexCount];
        contact.HorizontalPolygon.CopyTo(polygon);
        ReadOnlySpan<Vector2d> footprint = polygon[..contact.HorizontalPolygon.VertexCount];
        bool hasCentroid = FixedConvex2dRelations.TryGetAreaAndCentroid(
            footprint,
            out _,
            out Vector2d centroid);
        Debug.Assert(hasCentroid);

        Fixed64 maximumRadius;
        if (IsPlanarPointContained(source, centroid) && IsPlanarPointContained(target, centroid))
        {
            maximumRadius = GetMinimumPolygonClearance(footprint, centroid);
        }
        else
        {
            centroid = footprint[0];
            maximumRadius = Fixed64.Zero;
        }

        portal = new GridNavigationPortal(
            VoxelContactFaceKind.Horizontal,
            sourceToTarget,
            new Vector3d(centroid.X, contact.VerticalMin, centroid.Y),
            maximumRadius,
            FixedMath.Min(sourceHeight, targetHeight),
            default,
            default);
        return true;
    }

    private static bool IsNavigationPrismValid(in GridCellPrism prism)
        => prism.FootprintVertexCount is 4 or 6;

    private static bool IsPlanarPointContained(in GridCellPrism prism, Vector2d point) =>
        prism.Contains(new Vector3d(point.X, prism.Center.Y, point.Y));

    private static Fixed64 GetMinimumPolygonClearance(
        ReadOnlySpan<Vector2d> polygon,
        Vector2d point)
    {
        long minimumRaw = long.MaxValue;
        Span<ulong> firstProduct = stackalloc ulong[2];
        Span<ulong> secondProduct = stackalloc ulong[2];
        Span<ulong> cross = stackalloc ulong[3];
        Span<ulong> edgeSquared = stackalloc ulong[3];
        Span<ulong> crossSquared = stackalloc ulong[6];
        Span<ulong> radiusSquared = stackalloc ulong[2];
        Span<ulong> scaledEdgeSquared = stackalloc ulong[6];
        for (int i = 0; i < polygon.Length; i++)
        {
            // A convex polygon is the intersection of its edge half-planes. Compare
            // radius^2 * edgeLength^2 <= cross^2 in raw integer space so neither a
            // projected point nor a normalized edge direction can round outward.
            Vector2d start = polygon[i];
            Vector2d end = polygon[(i + 1) % polygon.Length];
            GetSignedDifference(end.X.m_rawValue, start.X.m_rawValue, out bool edgeXNegative, out ulong edgeX);
            GetSignedDifference(end.Y.m_rawValue, start.Y.m_rawValue, out bool edgeYNegative, out ulong edgeY);
            GetSignedDifference(point.X.m_rawValue, start.X.m_rawValue, out bool pointXNegative, out ulong pointX);
            GetSignedDifference(point.Y.m_rawValue, start.Y.m_rawValue, out bool pointYNegative, out ulong pointY);

            Multiply64(edgeX, pointY, firstProduct);
            Multiply64(edgeY, pointX, secondProduct);
            GetSignedDifferenceMagnitude(
                firstProduct,
                edgeXNegative ^ pointYNegative,
                secondProduct,
                !(edgeYNegative ^ pointXNegative),
                cross);

            Multiply64(edgeX, edgeX, firstProduct);
            Multiply64(edgeY, edgeY, secondProduct);
            Add128(firstProduct, secondProduct, edgeSquared);

            MultiplyWords(cross, cross, crossSquared);
            long low = 0L;
            long high = minimumRaw;
            while (low < high)
            {
                long difference = high - low;
                long middle = low + (difference >> 1) + (difference & 1L);
                Multiply64((ulong)middle, (ulong)middle, radiusSquared);
                MultiplyWords(radiusSquared, edgeSquared, scaledEdgeSquared);
                if (CompareWords(scaledEdgeSquared, crossSquared) <= 0)
                    low = middle;
                else
                    high = middle - 1L;
            }

            minimumRaw = low;
        }

        return Fixed64.FromRaw(minimumRaw);
    }

    private static void GetSignedDifference(
        long end,
        long start,
        out bool negative,
        out ulong magnitude)
    {
        negative = end < start;
        magnitude = negative
            ? unchecked((ulong)start - (ulong)end)
            : unchecked((ulong)end - (ulong)start);
    }

    private static void GetSignedDifferenceMagnitude(
        ReadOnlySpan<ulong> first,
        bool firstNegative,
        ReadOnlySpan<ulong> second,
        bool secondNegative,
        Span<ulong> magnitude)
    {
        magnitude.Clear();
        if (firstNegative == secondNegative)
        {
            Add128(first, second, magnitude);
            return;
        }

        int comparison = CompareWords(first, second);
        if (comparison >= 0)
            Subtract128(first, second, magnitude);
        else
            Subtract128(second, first, magnitude);
    }

    private static void Add128(
        ReadOnlySpan<ulong> first,
        ReadOnlySpan<ulong> second,
        Span<ulong> result)
    {
        result.Clear();
        result[0] = unchecked(first[0] + second[0]);
        ulong carry = result[0] < first[0] ? 1UL : 0UL;
        result[1] = unchecked(first[1] + second[1] + carry);
    }

    private static void Subtract128(
        ReadOnlySpan<ulong> minuend,
        ReadOnlySpan<ulong> subtrahend,
        Span<ulong> result)
    {
        result.Clear();
        result[0] = unchecked(minuend[0] - subtrahend[0]);
        ulong borrow = minuend[0] < subtrahend[0] ? 1UL : 0UL;
        result[1] = unchecked(minuend[1] - subtrahend[1] - borrow);
    }

    private static void Multiply64(ulong first, ulong second, Span<ulong> result)
    {
        ulong firstLow = (uint)first;
        ulong firstHigh = first >> 32;
        ulong secondLow = (uint)second;
        ulong secondHigh = second >> 32;
        ulong lowProduct = firstLow * secondLow;
        ulong firstCross = firstHigh * secondLow;
        ulong secondCross = firstLow * secondHigh;
        ulong carry = (lowProduct >> 32) + (uint)firstCross + (uint)secondCross;
        result[0] = (lowProduct & uint.MaxValue) | (carry << 32);
        result[1] = (firstHigh * secondHigh)
            + (firstCross >> 32)
            + (secondCross >> 32)
            + (carry >> 32);
    }

    private static void MultiplyWords(
        ReadOnlySpan<ulong> first,
        ReadOnlySpan<ulong> second,
        Span<ulong> result)
    {
        result.Clear();
        Span<ulong> product = stackalloc ulong[2];
        for (int firstIndex = 0; firstIndex < first.Length; firstIndex++)
        {
            for (int secondIndex = 0; secondIndex < second.Length; secondIndex++)
            {
                Multiply64(first[firstIndex], second[secondIndex], product);
                AddWord(result, firstIndex + secondIndex, product[0]);
                AddWord(result, firstIndex + secondIndex + 1, product[1]);
            }
        }
    }

    private static void AddWord(Span<ulong> value, int index, ulong addend)
    {
        while (addend != 0UL && index < value.Length)
        {
            ulong current = value[index];
            value[index] = unchecked(current + addend);
            addend = value[index] < current ? 1UL : 0UL;
            index++;
        }
    }

    private static int CompareWords(ReadOnlySpan<ulong> first, ReadOnlySpan<ulong> second)
    {
        Debug.Assert(first.Length == second.Length);
        int index = first.Length - 1;
        while (index >= 0)
        {
            ulong firstWord = first[index];
            ulong secondWord = second[index];
            if (firstWord != secondWord)
                return firstWord < secondWord ? -1 : 1;
            index--;
        }

        return 0;
    }

    private static Fixed64 GetConservativeDistance(
        Vector2d start,
        Vector2d end)
    {
        bool hasDistance = Vector2d.TryGetDistance(start, end, out Fixed64 distance);
        Debug.Assert(hasDistance);

        Vector2d representedDistance = new Vector2d(distance, Fixed64.Zero);
        if (Vector2d.CompareDistanceSquared(start, end, Vector2d.Zero, representedDistance) < 0)
            distance = Fixed64.FromRaw(distance.m_rawValue - 1L);

        return distance;
    }

    private static Vector2d GetNearestRepresentableSegmentMidpoint(Vector2d start, Vector2d end)
    {
        ulong xDelta = unchecked((ulong)end.X.m_rawValue - (ulong)start.X.m_rawValue);
        GetSignedDifference(end.Y.m_rawValue, start.Y.m_rawValue, out bool yNegative, out ulong yDelta);
        ulong divisor = GetGreatestCommonDivisor(xDelta, yDelta);
        ulong latticeIndex = divisor >> 1;
        ulong xOffset = (xDelta / divisor) * latticeIndex;
        ulong yOffset = (yDelta / divisor) * latticeIndex;
        long x = unchecked((long)((ulong)start.X.m_rawValue + xOffset));
        long y = unchecked((long)(yNegative
            ? (ulong)start.Y.m_rawValue - yOffset
            : (ulong)start.Y.m_rawValue + yOffset));
        return new Vector2d(Fixed64.FromRaw(x), Fixed64.FromRaw(y));
    }

    private static ulong GetGreatestCommonDivisor(ulong first, ulong second)
    {
        while (second != 0UL)
        {
            ulong remainder = first % second;
            first = second;
            second = remainder;
        }

        return first;
    }
}
