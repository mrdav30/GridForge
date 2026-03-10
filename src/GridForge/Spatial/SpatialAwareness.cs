using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

/// <summary>
/// Provides global spatial awareness utilities to find neighboring grids or voxels
/// </summary>
public static class SpatialAwareness
{
    #region Fields & Properties

    /// <summary>
    /// Predefined offsets for a 3x3x3 neighbor structure, excluding the center position.
    /// </summary>
    public static readonly (int x, int y, int z)[] DirectionOffsets = new (int x, int y, int z)[26]
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

    /// <summary>
    /// All 26 neighbor directions excluding None.
    /// </summary>
    public static readonly SpatialDirection[] AllDirections =
        Enum.GetValues(typeof(SpatialDirection))
            .Cast<SpatialDirection>()
            .Where(d => d != SpatialDirection.None)
            .ToArray();

    /// <summary>
    /// All 6 perpendicular neighbor directions excluding None.
    /// </summary>
    public static readonly SpatialDirection[] PerpendicularDirections
      = AllDirections
          .Where(IsPerpendicularNeighbor)
          .ToArray();

    /// <summary>
    /// All 20 perpendicular neighbor directions excluding None.
    /// </summary>
    public static readonly SpatialDirection[] DiagonalDirections
      = AllDirections
          .Where(IsDiagonalNeighbor)
          .ToArray();

    #endregion

    #region Methods

    /// <summary>True for pure axis-aligned directions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPerpendicularNeighbor(SpatialDirection dir) => (int)dir < 6;

    /// <summary>True for any diagonal step (multiple axes).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDiagonalNeighbor(SpatialDirection dir) => (int)dir >= 6;

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

    #endregion
}
