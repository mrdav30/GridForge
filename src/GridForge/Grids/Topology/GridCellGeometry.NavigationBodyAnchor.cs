//=======================================================================
// GridCellGeometry.NavigationBodyAnchor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace GridForge.Grids.Topology;

public static partial class GridCellGeometry
{
    private enum SegmentProjection : byte
    {
        Start,
        Interior,
        End
    }

    /// <summary>
    /// Determines whether one cylindrical body anchor fits an exact cell prism, optionally through
    /// one selected navigation portal.
    /// </summary>
    /// <remarks>
    /// A vertical portal exempts only its conservative symmetric opening on its certified face.
    /// A horizontal, foreign, ambiguous, or unusable portal provides no planar-wall exemption.
    /// All clearance comparisons use exact bounded raw-integer products without normalized or
    /// rounded projection intermediates. The method retains no state and allocates nothing.
    /// </remarks>
    /// <param name="prism">The exact cell prism containing the body foot.</param>
    /// <param name="foot">The body's foot position.</param>
    /// <param name="horizontalRadius">The required nonnegative horizontal body radius.</param>
    /// <param name="bodyHeight">The required positive body height.</param>
    /// <param name="selectedPortal">
    /// The selected portal whose certified vertical opening may exempt one wall, or
    /// <see langword="default"/> for ordinary prism clearance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the complete body fits the prism or its selected opening;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsNavigationBodyAnchorValid(
        in GridCellPrism prism,
        Vector3d foot,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        in GridNavigationPortal selectedPortal)
    {
        SwiftThrowHelper.ThrowIfArgument(
            horizontalRadius < Fixed64.Zero,
            nameof(horizontalRadius),
            "Horizontal radius must be nonnegative.");
        SwiftThrowHelper.ThrowIfArgument(
            bodyHeight <= Fixed64.Zero,
            nameof(bodyHeight),
            "Body height must be positive.");

        if (!IsNavigationPrismValid(prism)
            || !prism.Contains(foot)
            || !Fixed64.TryAdd(foot.Y, bodyHeight, out Fixed64 bodyTop)
            || bodyTop > prism.VerticalMax)
        {
            return false;
        }

        Fixed64 portalTop = default;
        int portalEdgeIndex = -1;
        bool canUsePortal = selectedPortal.IsValid
            && selectedPortal.FaceKind == VoxelContactFaceKind.Vertical
            && horizontalRadius <= selectedPortal.MaximumHorizontalRadius
            && bodyHeight <= selectedPortal.MaximumBodyHeight
            && Fixed64.TryAdd(
                selectedPortal.CanonicalFacePoint.Y,
                selectedPortal.MaximumBodyHeight,
                out portalTop)
            && TryGetCertifiedPortalEdge(prism, selectedPortal, out portalEdgeIndex);

        Vector2d point = new Vector2d(foot.X, foot.Z);
        Vector2d portalCenter = new Vector2d(
            selectedPortal.CanonicalFacePoint.X,
            selectedPortal.CanonicalFacePoint.Z);
        for (int i = 0; i < prism.FootprintVertexCount; i++)
        {
            Vector2d start = prism.GetFootprintVertex(i);
            Vector2d end = prism.GetFootprintVertex((i + 1) % prism.FootprintVertexCount);
            if (!IsExactWallClear(
                    point,
                    start,
                    end,
                    horizontalRadius,
                    canUsePortal && i == portalEdgeIndex,
                    foot.Y,
                    bodyTop,
                    selectedPortal.CanonicalFacePoint.Y,
                    portalTop,
                    portalCenter,
                    selectedPortal.MaximumHorizontalRadius))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsExactWallClear(
        Vector2d point,
        Vector2d edgeStart,
        Vector2d edgeEnd,
        Fixed64 radius,
        bool isCertifiedPortalEdge,
        Fixed64 bodyBottom,
        Fixed64 bodyTop,
        Fixed64 portalBottom,
        Fixed64 portalTop,
        Vector2d portalCenter,
        Fixed64 portalRadius)
    {
        Span<ulong> lineSquared = stackalloc ulong[3];
        Span<ulong> normalMagnitude = stackalloc ulong[3];
        if (!TryGetSegmentProjection(
                edgeStart,
                edgeEnd,
                point,
                lineSquared,
                normalMagnitude,
                out SegmentProjection projection,
                out _))
        {
            return false;
        }

        if (projection != SegmentProjection.Interior)
        {
            Vector2d endpoint = projection == SegmentProjection.Start ? edgeStart : edgeEnd;
            return IsExactEndpointClear(point, endpoint, radius);
        }

        Span<ulong> normalSquared = stackalloc ulong[6];
        Span<ulong> radiusSquared = stackalloc ulong[2];
        Span<ulong> radiusLineSquared = stackalloc ulong[6];
        Multiply64((ulong)radius.m_rawValue, (ulong)radius.m_rawValue, radiusSquared);
        if (!TryMultiplyWordsChecked(normalMagnitude, normalMagnitude, normalSquared)
            || !TryMultiplyWordsChecked(radiusSquared, lineSquared, radiusLineSquared))
        {
            return false;
        }

        if (CompareWords(normalSquared, radiusLineSquared) >= 0)
            return true;

        if (!isCertifiedPortalEdge
            || bodyBottom < portalBottom
            || bodyTop > portalTop)
        {
            return false;
        }

        Span<ulong> tangentMagnitude = stackalloc ulong[3];
        if (!TryGetAbsoluteDot(edgeStart, edgeEnd, point, portalCenter, tangentMagnitude))
            return false;

        Span<ulong> tangentSquared = stackalloc ulong[6];
        Span<ulong> portalRadiusSquared = stackalloc ulong[2];
        Span<ulong> portalLineSquared = stackalloc ulong[6];
        Multiply64(
            (ulong)portalRadius.m_rawValue,
            (ulong)portalRadius.m_rawValue,
            portalRadiusSquared);
        if (!TryMultiplyWordsChecked(tangentMagnitude, tangentMagnitude, tangentSquared)
            || !TryMultiplyWordsChecked(portalRadiusSquared, lineSquared, portalLineSquared)
            || CompareWords(tangentSquared, portalLineSquared) > 0)
        {
            return false;
        }

        Span<ulong> requiredOpening = stackalloc ulong[6];
        if (!TrySubtractWords(radiusLineSquared, normalSquared, requiredOpening))
            return false;
        Span<ulong> openingSum = stackalloc ulong[6];
        if (!TryAddWords(portalLineSquared, tangentSquared, openingSum)
            || CompareWords(openingSum, requiredOpening) < 0)
        {
            return false;
        }

        Span<ulong> openingDifference = stackalloc ulong[6];
        if (!TrySubtractWords(openingSum, requiredOpening, openingDifference))
            return false;
        Span<ulong> left = stackalloc ulong[12];
        Span<ulong> right = stackalloc ulong[12];
        if (!TryMultiplyWordsChecked(portalLineSquared, tangentSquared, left)
            || !TryShiftLeftTwo(left)
            || !TryMultiplyWordsChecked(openingDifference, openingDifference, right))
        {
            return false;
        }

        return CompareWords(left, right) <= 0;
    }

    private static bool IsExactEndpointClear(
        Vector2d point,
        Vector2d endpoint,
        Fixed64 radius)
    {
        GetSignedDifference(point.X.m_rawValue, endpoint.X.m_rawValue, out _, out ulong x);
        GetSignedDifference(point.Y.m_rawValue, endpoint.Y.m_rawValue, out _, out ulong y);
        Span<ulong> xSquared = stackalloc ulong[2];
        Span<ulong> ySquared = stackalloc ulong[2];
        Span<ulong> distanceSquared = stackalloc ulong[3];
        Span<ulong> radiusSquared = stackalloc ulong[2];
        Multiply64(x, x, xSquared);
        Multiply64(y, y, ySquared);
        Add128(xSquared, ySquared, distanceSquared);
        Multiply64((ulong)radius.m_rawValue, (ulong)radius.m_rawValue, radiusSquared);
        return CompareWords(distanceSquared, radiusSquared) >= 0;
    }

    private static bool TryGetCertifiedPortalEdge(
        in GridCellPrism prism,
        in GridNavigationPortal portal,
        out int edgeIndex)
    {
        edgeIndex = -1;
        Vector2d center = new Vector2d(
            portal.CanonicalFacePoint.X,
            portal.CanonicalFacePoint.Z);
        Vector2d crossing = new Vector2d(
            portal.SourceToTarget.X,
            portal.SourceToTarget.Z);
        for (int i = 0; i < prism.FootprintVertexCount; i++)
        {
            Vector2d start = prism.GetFootprintVertex(i);
            Vector2d end = prism.GetFootprintVertex((i + 1) % prism.FootprintVertexCount);
            if (!IsExactPointOnSegment(portal.VerticalFaceSegmentStart, start, end)
                || !IsExactPointOnSegment(portal.VerticalFaceSegmentEnd, start, end)
                || !IsExactPointOnSegment(center, start, end)
                || !HasExactCrossingDirection(start, end, crossing))
            {
                continue;
            }

            if (edgeIndex >= 0)
                return false;
            edgeIndex = i;
        }

        return edgeIndex >= 0;
    }

    private static bool IsExactPointOnSegment(
        Vector2d point,
        Vector2d segmentStart,
        Vector2d segmentEnd)
    {
        Span<ulong> lineSquared = stackalloc ulong[3];
        Span<ulong> normalMagnitude = stackalloc ulong[3];
        return TryGetSegmentProjection(
                segmentStart,
                segmentEnd,
                point,
                lineSquared,
                normalMagnitude,
                out _,
                out bool parameterWithinSegment)
            && parameterWithinSegment
            && IsZero(normalMagnitude);
    }

    private static bool HasExactCrossingDirection(
        Vector2d edgeStart,
        Vector2d edgeEnd,
        Vector2d crossing)
    {
        GetSignedDifference(edgeEnd.X.m_rawValue, edgeStart.X.m_rawValue, out bool edgeXNegative, out ulong edgeX);
        GetSignedDifference(edgeEnd.Y.m_rawValue, edgeStart.Y.m_rawValue, out bool edgeYNegative, out ulong edgeY);
        bool crossingXNegative = crossing.X.m_rawValue < 0L;
        bool crossingYNegative = crossing.Y.m_rawValue < 0L;
        ulong crossingX = AbsToUInt64(crossing.X.m_rawValue);
        ulong crossingY = AbsToUInt64(crossing.Y.m_rawValue);
        Span<ulong> first = stackalloc ulong[2];
        Span<ulong> second = stackalloc ulong[2];
        Span<ulong> magnitude = stackalloc ulong[3];
        Multiply64(edgeX, crossingY, first);
        Multiply64(edgeY, crossingX, second);
        GetSignedSumMagnitude(
            first,
            edgeXNegative ^ crossingYNegative,
            second,
            !(edgeYNegative ^ crossingXNegative),
            magnitude,
            out _);
        return !IsZero(magnitude);
    }

    private static bool TryGetSegmentProjection(
        Vector2d segmentStart,
        Vector2d segmentEnd,
        Vector2d point,
        Span<ulong> lineSquared,
        Span<ulong> normalMagnitude,
        out SegmentProjection projection,
        out bool parameterWithinSegment)
    {
        projection = default;
        parameterWithinSegment = false;
        GetSignedDifference(segmentEnd.X.m_rawValue, segmentStart.X.m_rawValue, out bool edgeXNegative, out ulong edgeX);
        GetSignedDifference(segmentEnd.Y.m_rawValue, segmentStart.Y.m_rawValue, out bool edgeYNegative, out ulong edgeY);
        GetSignedDifference(point.X.m_rawValue, segmentStart.X.m_rawValue, out bool pointXNegative, out ulong pointX);
        GetSignedDifference(point.Y.m_rawValue, segmentStart.Y.m_rawValue, out bool pointYNegative, out ulong pointY);

        Span<ulong> first = stackalloc ulong[2];
        Span<ulong> second = stackalloc ulong[2];
        Multiply64(edgeX, edgeX, first);
        Multiply64(edgeY, edgeY, second);
        Add128(first, second, lineSquared);
        if (IsZero(lineSquared))
            return false;

        Span<ulong> parameter = stackalloc ulong[3];
        Multiply64(edgeX, pointX, first);
        Multiply64(edgeY, pointY, second);
        GetSignedSumMagnitude(
            first,
            edgeXNegative ^ pointXNegative,
            second,
            edgeYNegative ^ pointYNegative,
            parameter,
            out bool parameterNegative);
        if (parameterNegative)
        {
            projection = SegmentProjection.Start;
        }
        else if (IsZero(parameter))
        {
            projection = SegmentProjection.Start;
            parameterWithinSegment = true;
        }
        else
        {
            int comparison = CompareWords(parameter, lineSquared);
            if (comparison < 0)
            {
                projection = SegmentProjection.Interior;
                parameterWithinSegment = true;
            }
            else
            {
                projection = SegmentProjection.End;
                parameterWithinSegment = comparison == 0;
            }
        }

        Multiply64(edgeX, pointY, first);
        Multiply64(edgeY, pointX, second);
        GetSignedSumMagnitude(
            first,
            edgeXNegative ^ pointYNegative,
            second,
            !(edgeYNegative ^ pointXNegative),
            normalMagnitude,
            out _);
        return true;
    }

    private static bool TryGetAbsoluteDot(
        Vector2d edgeStart,
        Vector2d edgeEnd,
        Vector2d point,
        Vector2d origin,
        Span<ulong> magnitude)
    {
        GetSignedDifference(edgeEnd.X.m_rawValue, edgeStart.X.m_rawValue, out bool edgeXNegative, out ulong edgeX);
        GetSignedDifference(edgeEnd.Y.m_rawValue, edgeStart.Y.m_rawValue, out bool edgeYNegative, out ulong edgeY);
        GetSignedDifference(point.X.m_rawValue, origin.X.m_rawValue, out bool pointXNegative, out ulong pointX);
        GetSignedDifference(point.Y.m_rawValue, origin.Y.m_rawValue, out bool pointYNegative, out ulong pointY);
        Span<ulong> first = stackalloc ulong[2];
        Span<ulong> second = stackalloc ulong[2];
        Multiply64(edgeX, pointX, first);
        Multiply64(edgeY, pointY, second);
        GetSignedSumMagnitude(
            first,
            edgeXNegative ^ pointXNegative,
            second,
            edgeYNegative ^ pointYNegative,
            magnitude,
            out _);
        return true;
    }

    private static void GetSignedSumMagnitude(
        ReadOnlySpan<ulong> first,
        bool firstNegative,
        ReadOnlySpan<ulong> second,
        bool secondNegative,
        Span<ulong> magnitude,
        out bool negative)
    {
        if (firstNegative == secondNegative)
        {
            Add128(first, second, magnitude);
            negative = firstNegative && !IsZero(magnitude);
            return;
        }

        int comparison = CompareWords(first, second);
        if (comparison >= 0)
        {
            Subtract128(first, second, magnitude);
            negative = firstNegative && comparison != 0;
        }
        else
        {
            Subtract128(second, first, magnitude);
            negative = secondNegative;
        }
    }

    private static bool TryMultiplyWordsChecked(
        ReadOnlySpan<ulong> first,
        ReadOnlySpan<ulong> second,
        Span<ulong> result)
    {
        result.Clear();
        Span<ulong> product = stackalloc ulong[2];
        for (int firstIndex = 0; firstIndex < first.Length; firstIndex++)
        {
            if (first[firstIndex] == 0UL)
                continue;

            for (int secondIndex = 0; secondIndex < second.Length; secondIndex++)
            {
                if (second[secondIndex] == 0UL)
                    continue;

                Multiply64(first[firstIndex], second[secondIndex], product);
                int index = firstIndex + secondIndex;
                if (!TryAddWord(result, index, product[0])
                    || !TryAddWord(result, index + 1, product[1]))
                {
                    result.Clear();
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryAddWords(
        ReadOnlySpan<ulong> first,
        ReadOnlySpan<ulong> second,
        Span<ulong> result)
    {
        result.Clear();
        int length = Math.Max(first.Length, second.Length);
        if (result.Length < length)
            return false;

        ulong carry = 0UL;
        for (int i = 0; i < length; i++)
        {
            ulong left = i < first.Length ? first[i] : 0UL;
            ulong right = i < second.Length ? second[i] : 0UL;
            ulong sum = unchecked(left + right);
            ulong nextCarry = sum < left ? 1UL : 0UL;
            ulong withCarry = unchecked(sum + carry);
            if (withCarry < sum)
                nextCarry = 1UL;
            result[i] = withCarry;
            carry = nextCarry;
        }

        return carry == 0UL;
    }

    private static bool TrySubtractWords(
        ReadOnlySpan<ulong> minuend,
        ReadOnlySpan<ulong> subtrahend,
        Span<ulong> result)
    {
        result.Clear();
        if (result.Length < Math.Max(minuend.Length, subtrahend.Length))
            return false;

        ulong borrow = 0UL;
        for (int i = 0; i < result.Length; i++)
        {
            ulong left = i < minuend.Length ? minuend[i] : 0UL;
            ulong right = i < subtrahend.Length ? subtrahend[i] : 0UL;
            ulong withBorrow = unchecked(right + borrow);
            bool overflow = withBorrow < right;
            result[i] = unchecked(left - withBorrow);
            borrow = overflow || left < withBorrow ? 1UL : 0UL;
        }

        return borrow == 0UL;
    }

    private static bool TryShiftLeftTwo(Span<ulong> value)
    {
        ulong carry = 0UL;
        for (int i = 0; i < value.Length; i++)
        {
            ulong nextCarry = value[i] >> 62;
            value[i] = (value[i] << 2) | carry;
            carry = nextCarry;
        }

        return carry == 0UL;
    }

    private static bool TryAddWord(Span<ulong> value, int index, ulong addend)
    {
        while (addend != 0UL)
        {
            if ((uint)index >= (uint)value.Length)
                return false;
            ulong current = value[index];
            value[index] = unchecked(current + addend);
            addend = value[index] < current ? 1UL : 0UL;
            index++;
        }

        return true;
    }

    private static bool IsZero(ReadOnlySpan<ulong> value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != 0UL)
                return false;
        }

        return true;
    }

    private static ulong AbsToUInt64(long value)
    {
        return value < 0L
            ? unchecked(0UL - (ulong)value)
            : (ulong)value;
    }
}
