//=======================================================================
// HexCoordinateUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;
using System;
using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

internal static class HexCoordinateUtility
{
    internal const long Sqrt3Raw = 7439101574L;

    internal static readonly Fixed64 Sqrt3 = Fixed64.FromRaw(Sqrt3Raw);

    private static readonly Fixed64 RawRoundingTolerance = Fixed64.FromRaw(4096);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3d AxialToWorldOffset(VoxelIndex index, GridTopologyMetrics metrics)
    {
        Fixed64 q = new(index.x);
        Fixed64 r = new(index.z);
        Fixed64 y = index.y * metrics.LayerHeight;

        if (metrics.HexOrientation == HexOrientation.FlatTop)
        {
            return new Vector3d(
                metrics.CellRadius * Fixed64.Three * Fixed64.Half * q,
                y,
                metrics.CellRadius * Sqrt3 * (r + q * Fixed64.Half));
        }

        return new Vector3d(
            metrics.CellRadius * Sqrt3 * (q + r * Fixed64.Half),
            y,
            metrics.CellRadius * Fixed64.Three * Fixed64.Half * r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WorldOffsetToAxial(
        Fixed64 worldX,
        Fixed64 worldZ,
        GridTopologyMetrics metrics,
        out Fixed64 q,
        out Fixed64 r)
    {
        if (metrics.HexOrientation == HexOrientation.FlatTop)
        {
            q = (Fixed64.Two * worldX / Fixed64.Three) / metrics.CellRadius;
            r = ((Sqrt3 * worldZ / Fixed64.Three) - (worldX / Fixed64.Three)) / metrics.CellRadius;
            return;
        }

        q = ((Sqrt3 * worldX / Fixed64.Three) - (worldZ / Fixed64.Three)) / metrics.CellRadius;
        r = (Fixed64.Two * worldZ / Fixed64.Three) / metrics.CellRadius;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static VoxelIndex RoundAxial(Fixed64 q, Fixed64 y, Fixed64 r)
    {
        RoundCube(q, r, out int roundedQ, out int roundedR);
        return new VoxelIndex(roundedQ, y.FloorToInt(), roundedR);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void RoundCube(Fixed64 q, Fixed64 r, out int roundedQ, out int roundedR)
    {
        Fixed64 s = -q - r;

        int qCandidate = RoundToInt(q);
        int rCandidate = RoundToInt(r);
        int sCandidate = RoundToInt(s);

        Fixed64 qDiff = (new Fixed64(qCandidate) - q).Abs();
        Fixed64 rDiff = (new Fixed64(rCandidate) - r).Abs();
        Fixed64 sDiff = (new Fixed64(sCandidate) - s).Abs();

        if (qDiff > rDiff && qDiff > sDiff)
            qCandidate = -rCandidate - sCandidate;
        else if (rDiff > sDiff)
            rCandidate = -qCandidate - sCandidate;

        roundedQ = qCandidate;
        roundedR = rCandidate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CeilToIntWithTolerance(Fixed64 value)
    {
        int rounded = RoundToInt(value);
        if ((new Fixed64(rounded) - value).Abs() <= RawRoundingTolerance)
            return rounded;

        return value.CeilToInt();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RoundToInt(Fixed64 value) =>
        (int)(FixedMath.Round(value, MidpointRounding.AwayFromZero).m_rawValue >> FixedMath.SHIFT_AMOUNT_I);
}
