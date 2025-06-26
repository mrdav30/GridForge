using System;
using System.Linq;

namespace GridForge.Grids
{
    /// <summary>
    /// Provides global spatial awareness utilities to find neighboring grids or voxels
    /// </summary>
    public static class SpatialAwareness
    {
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


        /// <summary>
        /// Predefined offsets for a 3x3x3 neighbor structure, excluding the center position.
        /// </summary>
        public static readonly (int x, int y, int z)[] DirectionOffsets = new (int x, int y, int z)[26]
        {
            (-1, 0, 0),
            (0, 0, -1),
            (0, 0, 1),
            (1, 0, 0),
            (0, -1, 0),
            (0, 1, 0),
            (-1, 0, -1),
            (-1, 0, 1),
            (1, 0, -1),
            (1, 0, 1),
            (-1, -1, 0),
            (0, -1, -1),
            (0, -1, 1),
            (1, -1, 0),
            (-1, 1, 0),
            (0, 1, -1),
            (0, 1, 1),
            (1, 1, 0),
            (-1, -1, -1),
            (-1, -1, 1),
            (1, -1, -1),
            (1, -1, 1),
            (-1, 1, -1),
            (-1, 1, 1),
            (1, 1, -1),
            (1, 1, 1)
        };

        /// <summary>True for pure axis-aligned directions.</summary>
        public static bool IsPerpendicularNeighbor(SpatialDirection dir) => (int)dir < 6;

        /// <summary>True for any diagonal step (multiple axes).</summary>
        public static bool IsDiagonalNeighbor(SpatialDirection dir) => (int)dir >= 6;
    }
}
