namespace GridForge.Grids
{
    /// <summary>
    /// Represents the 26 possible neighbor directions in a 3x3x3 grid.
    /// The integer ordering is used for neighbor indexing and deterministic preferences.
    /// </summary>
    public enum SpatialDirection
    {
        /// <summary>
        /// No linear direction from source
        /// </summary>
        None = -1,

        // Perpendicular (Axis-aligned)

        /// <summary>
        /// (-1, 0, 0)
        /// </summary>
        West = 0,
        /// <summary>
        ///  (0, 0, -1)
        /// </summary>
        South = 1,
        /// <summary>
        /// (0, 0, 1)
        /// </summary>
        East = 2,
        /// <summary>
        /// (1, 0, 0)
        /// </summary>
        North = 3,
        /// <summary>
        /// (0, -1, 0)
        /// </summary>
        Below = 4,
        /// <summary>
        /// (0, 1, 0)
        /// </summary>
        Above = 5,

        // Diagonals (XY/XZ/YZ)

        /// <summary>
        /// (-1, 0, -1)
        /// </summary>
        SouthWest = 6,
        /// <summary>
        /// (-1, 0, 1)
        /// </summary>
        NorthWest = 7,
        /// <summary>
        /// (1, 0, -1)
        /// </summary>
        SouthEast = 8,
        /// <summary>
        /// (1, 0, 1)
        /// </summary>
        NorthEast = 9,
        /// <summary>
        /// (-1, -1, 0)
        /// </summary>
        BelowWest = 10,
        /// <summary>
        /// (0, -1, -1)
        /// </summary>
        BelowSouth = 11,
        /// <summary>
        /// (0, -1, 1)
        /// </summary>
        BelowEast = 12,
        /// <summary>
        /// (1, -1, 0)
        /// </summary>
        BelowNorth = 13,
        /// <summary>
        /// (-1, 1, 0)
        /// </summary>
        AboveWest = 14,
        /// <summary>
        /// (0, 1, -1)
        /// </summary>
        AboveSouth = 15,
        /// <summary>
        /// (0, 1, 1)
        /// </summary>
        AboveEast = 16,
        /// <summary>
        /// (1, 1, 0)
        /// </summary>
        AboveNorth = 17,

        // Complex 3D Diagonals (all 3 axes)

        /// <summary>
        /// (-1, -1, -1)
        /// </summary>
        BelowSouthWest = 18,
        /// <summary>
        /// (-1, -1, 1)
        /// </summary>
        BelowNorthWest = 19,
        /// <summary>
        /// (1, -1, -1)
        /// </summary>
        BelowSouthEast = 20,
        /// <summary>
        /// (1, -1, 1)
        /// </summary>
        BelowNorthEast = 21,
        /// <summary>
        /// (-1, 1, -1)
        /// </summary>
        AboveSouthWest = 22, 
        /// <summary>
        /// (-1, 1, 1)
        /// </summary>
        AboveNorthWest = 23, 
        /// <summary>
        /// (1, 1, -1)
        /// </summary>
        AboveSouthEast = 24,
        /// <summary>
        /// (1, 1, 1)
        /// </summary>
        AboveNorthEast = 25,
    }
}
