//=======================================================================
// RectangularDirectionUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

/// <summary>
/// Provides rectangular-prism neighbor direction utilities.
/// </summary>
public static class RectangularDirectionUtility
{
    #region Fields & Properties

    private const int OffsetLookupSize = 27;

    /// <summary>
    /// Predefined offsets for a rectangular-prism 3x3x3 neighbor structure, excluding the center position.
    /// </summary>
    public static ReadOnlySpan<(int x, int y, int z)> Offsets => OffsetValues;

    private static readonly (int x, int y, int z)[] OffsetValues = new (int x, int y, int z)[26]
    {
        (-1, 0, 0),
        (0, 0, -1),
        (1, 0, 0),
        (0, 0, 1),
        (0, -1, 0),
        (0, 1, 0),
        (-1, 0, -1),
        (-1, 0, 1),
        (1, 0, -1),
        (1, 0, 1),
        (-1, -1, 0),
        (0, -1, -1),
        (1, -1, 0),
        (0, -1, 1),
        (-1, 1, 0),
        (0, 1, -1),
        (1, 1, 0),
        (0, 1, 1),
        (-1, -1, -1),
        (-1, -1, 1),
        (1, -1, -1),
        (1, -1, 1),
        (-1, 1, -1),
        (-1, 1, 1),
        (1, 1, -1),
        (1, 1, 1)
    };

    private static readonly RectangularDirection[] DirectionLookup = CreateDirectionLookup();

    /// <summary>
    /// All 26 neighbor directions excluding None.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> All => AllValues;

    private static readonly RectangularDirection[] AllValues =
    {
        RectangularDirection.West,
        RectangularDirection.South,
        RectangularDirection.East,
        RectangularDirection.North,
        RectangularDirection.Below,
        RectangularDirection.Above,
        RectangularDirection.SouthWest,
        RectangularDirection.NorthWest,
        RectangularDirection.SouthEast,
        RectangularDirection.NorthEast,
        RectangularDirection.BelowWest,
        RectangularDirection.BelowSouth,
        RectangularDirection.BelowEast,
        RectangularDirection.BelowNorth,
        RectangularDirection.AboveWest,
        RectangularDirection.AboveSouth,
        RectangularDirection.AboveEast,
        RectangularDirection.AboveNorth,
        RectangularDirection.BelowSouthWest,
        RectangularDirection.BelowNorthWest,
        RectangularDirection.BelowSouthEast,
        RectangularDirection.BelowNorthEast,
        RectangularDirection.AboveSouthWest,
        RectangularDirection.AboveNorthWest,
        RectangularDirection.AboveSouthEast,
        RectangularDirection.AboveNorthEast
    };

    /// <summary>
    /// The 6 face-adjacent rectangular-prism directions.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> Primary => PrimaryValues;

    private static readonly RectangularDirection[] PrimaryValues =
    {
        RectangularDirection.West,
        RectangularDirection.South,
        RectangularDirection.East,
        RectangularDirection.North,
        RectangularDirection.Below,
        RectangularDirection.Above
    };

    /// <summary>
    /// All 6 perpendicular neighbor directions excluding None.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> Perpendicular => PerpendicularValues;

    private static readonly RectangularDirection[] PerpendicularValues =
    {
        RectangularDirection.West,
        RectangularDirection.South,
        RectangularDirection.East,
        RectangularDirection.North,
        RectangularDirection.Below,
        RectangularDirection.Above
    };

    /// <summary>
    /// All 8 same-layer XZ-plane directions excluding None.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> Planar => PlanarValues;

    private static readonly RectangularDirection[] PlanarValues =
    {
        RectangularDirection.West,
        RectangularDirection.South,
        RectangularDirection.East,
        RectangularDirection.North,
        RectangularDirection.SouthWest,
        RectangularDirection.NorthWest,
        RectangularDirection.SouthEast,
        RectangularDirection.NorthEast
    };

    /// <summary>
    /// All 2 vertical directions.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> Vertical => VerticalValues;

    private static readonly RectangularDirection[] VerticalValues =
    {
        RectangularDirection.Below,
        RectangularDirection.Above
    };

    /// <summary>
    /// All 9 neighbor directions on the layer below.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> BelowLayer => BelowLayerValues;

    private static readonly RectangularDirection[] BelowLayerValues =
    {
        RectangularDirection.Below,
        RectangularDirection.BelowWest,
        RectangularDirection.BelowSouth,
        RectangularDirection.BelowEast,
        RectangularDirection.BelowNorth,
        RectangularDirection.BelowSouthWest,
        RectangularDirection.BelowNorthWest,
        RectangularDirection.BelowSouthEast,
        RectangularDirection.BelowNorthEast
    };

    /// <summary>
    /// All 9 neighbor directions on the layer above.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> AboveLayer => AboveLayerValues;

    private static readonly RectangularDirection[] AboveLayerValues =
    {
        RectangularDirection.Above,
        RectangularDirection.AboveWest,
        RectangularDirection.AboveSouth,
        RectangularDirection.AboveEast,
        RectangularDirection.AboveNorth,
        RectangularDirection.AboveSouthWest,
        RectangularDirection.AboveNorthWest,
        RectangularDirection.AboveSouthEast,
        RectangularDirection.AboveNorthEast
    };

    /// <summary>
    /// All 16 non-vertical directions on the layers above and below.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> VerticalDiagonal => VerticalDiagonalValues;

    private static readonly RectangularDirection[] VerticalDiagonalValues =
    {
        RectangularDirection.BelowWest,
        RectangularDirection.BelowSouth,
        RectangularDirection.BelowEast,
        RectangularDirection.BelowNorth,
        RectangularDirection.BelowSouthWest,
        RectangularDirection.BelowNorthWest,
        RectangularDirection.BelowSouthEast,
        RectangularDirection.BelowNorthEast,
        RectangularDirection.AboveWest,
        RectangularDirection.AboveSouth,
        RectangularDirection.AboveEast,
        RectangularDirection.AboveNorth,
        RectangularDirection.AboveSouthWest,
        RectangularDirection.AboveNorthWest,
        RectangularDirection.AboveSouthEast,
        RectangularDirection.AboveNorthEast
    };

    /// <summary>
    /// All 20 diagonal neighbor directions excluding None.
    /// </summary>
    public static ReadOnlySpan<RectangularDirection> Diagonal => DiagonalValues;

    private static readonly RectangularDirection[] DiagonalValues =
    {
        RectangularDirection.SouthWest,
        RectangularDirection.NorthWest,
        RectangularDirection.SouthEast,
        RectangularDirection.NorthEast,
        RectangularDirection.BelowWest,
        RectangularDirection.BelowSouth,
        RectangularDirection.BelowEast,
        RectangularDirection.BelowNorth,
        RectangularDirection.AboveWest,
        RectangularDirection.AboveSouth,
        RectangularDirection.AboveEast,
        RectangularDirection.AboveNorth,
        RectangularDirection.BelowSouthWest,
        RectangularDirection.BelowNorthWest,
        RectangularDirection.BelowSouthEast,
        RectangularDirection.BelowNorthEast,
        RectangularDirection.AboveSouthWest,
        RectangularDirection.AboveNorthWest,
        RectangularDirection.AboveSouthEast,
        RectangularDirection.AboveNorthEast
    };

    #endregion

    #region Methods

    /// <summary>True for pure axis-aligned directions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPerpendicularNeighbor(RectangularDirection dir) =>
        (uint)dir <= (uint)RectangularDirection.Above;

    /// <summary>True for any diagonal step (multiple axes).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDiagonalNeighbor(RectangularDirection dir) =>
        (uint)((int)dir - (int)RectangularDirection.SouthWest) < Diagonal.Length;

    /// <summary>
    /// Converts a rectangular local offset in the range [-1, 1] on each axis into a direction.
    /// </summary>
    public static RectangularDirection GetDirectionFromOffset((int x, int y, int z) offset)
    {
        if (!TryGetOffsetLookupKey(offset, out int lookupKey))
        {
            GridForgeLogger.DebugChannel.Info($"Invalid rectangular offset: {offset}. Offsets must be in the range [-1, 1] for each axis.");
            return RectangularDirection.None;
        }

        return DirectionLookup[lookupKey];
    }

    /// <summary>
    /// Gets the boundary range for a given offset and size,
    /// returning (0, 0) for negative offsets, (size-1, size-1) for positive offsets,
    /// and (0, size-1) for zero offsets.
    /// </summary>
    /// <param name="offset">The offset value.</param>
    /// <param name="size">The size of the dimension.</param>
    /// <returns>A tuple representing the start and end of the boundary range.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int start, int end) GetBoundaryRange(int offset, int size)
    {
        return offset switch
        {
            < 0 => (0, 0),
            > 0 => (size - 1, size - 1),
            _ => (0, size - 1)
        };
    }

    /// <summary>
    /// Determines if a coordinate is facing the boundary of an axis given an offset and size.
    /// </summary>
    /// <param name="coordinate">The coordinate value.</param>
    /// <param name="offset">The offset value.</param>
    /// <param name="size">The size of the dimension.</param>
    /// <returns>True if the coordinate is facing the boundary, otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAxisFacingBoundary(int coordinate, int offset, int size)
    {
        return offset switch
        {
            < 0 => coordinate == 0,
            > 0 => coordinate == size - 1,
            _ => true
        };
    }

    private static RectangularDirection[] CreateDirectionLookup()
    {
        RectangularDirection[] lookup = new RectangularDirection[OffsetLookupSize];
        Array.Fill(lookup, RectangularDirection.None);

        for (int i = 0; i < OffsetValues.Length; i++)
        {
            if (TryGetOffsetLookupKey(OffsetValues[i], out int lookupKey))
                lookup[lookupKey] = (RectangularDirection)i;
        }

        return lookup;
    }

    private static bool TryGetOffsetLookupKey((int x, int y, int z) offset, out int lookupKey)
    {
        int x = offset.x + 1;
        int y = offset.y + 1;
        int z = offset.z + 1;

        if ((uint)x > 2u || (uint)y > 2u || (uint)z > 2u)
        {
            lookupKey = -1;
            return false;
        }

        lookupKey = x * 9 + y * 3 + z;
        return true;
    }

    #endregion
}
