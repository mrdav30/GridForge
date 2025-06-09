namespace GridForge.Grids
{
    /// <summary>
    /// Represents the 26 possible neighbor directions in a 3x3x3 grid.
    /// These directions are used for spatial relationships between grids and voxels.
    /// </summary>
    public enum LinearDirection
    {
        /// <summary>
        /// No linear direction from source
        /// </summary>
        None = -1,
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
        /// (-1, 0, -1)
        /// </summary>
        SouthWest = 4,
        /// <summary>
        /// (-1, 0, 1)
        /// </summary>
        NorthWest = 5,
        /// <summary>
        /// (1, 0, -1)
        /// </summary>
        SouthEast = 6,
        /// <summary>
        /// (1, 0, 1)
        /// </summary>
        NorthEast = 7,
        /// <summary>
        /// (-1, -1, 0)
        /// </summary>
        BelowWest = 8,
        /// <summary>
        /// (0, -1, -1)
        /// </summary>
        BelowSouth = 9,
        /// <summary>
        /// (0, -1, 1)
        /// </summary>
        BelowEast = 10,
        /// <summary>
        /// (1, -1, 0)
        /// </summary>
        BelowNorth = 11,
        /// <summary>
        /// (-1, -1, -1)
        /// </summary>
        BelowSouthWest = 12,
        /// <summary>
        /// (-1, -1, 1)
        /// </summary>
        BelowNorthWest = 13,
        /// <summary>
        /// (1, -1, -1)
        /// </summary>
        BelowSouthEast = 14,
        /// <summary>
        /// (1, -1, 1)
        /// </summary>
        BelowNorthEast = 15,
        /// <summary>
        /// (0, -1, 0)
        /// </summary>
        Below = 16,
        /// <summary>
        /// (-1, 1, 0)
        /// </summary>
        AboveWest = 17,
        /// <summary>
        /// (0, 1, -1)
        /// </summary>
        AboveSouth = 18,
        /// <summary>
        /// (0, 1, 1)
        /// </summary>
        AboveEast = 19,
        /// <summary>
        /// (1, 1, 0)
        /// </summary>
        AboveNorth = 20,
        /// <summary>
        /// (-1, 1, -1)
        /// </summary>
        AboveSouthWest = 21, 
        /// <summary>
        /// (-1, 1, 1)
        /// </summary>
        AboveNorthWest = 22, 
        /// <summary>
        /// (1, 1, -1)
        /// </summary>
        AboveSouthEast = 23,
        /// <summary>
        /// (1, 1, 1)
        /// </summary>
        AboveNorthEast = 24,
        /// <summary>
        /// (0, 1, 0)
        /// </summary>
        Above = 25
    }

    /// <summary>
    /// Represents different types of grid-related changes that can occur.
    /// Used to track modifications such as adding/removing neighbors, obstacles, or occupants.
    /// </summary>
    public enum GridChange
    {
        /// <summary>
        /// Default, nothing happened
        /// </summary>
        None = -1,
        /// <summary>
        /// Signifies an add operation occured
        /// </summary>
        Add = 0,
        /// <summary>
        /// Signifies a remove operation occured
        /// </summary>
        Remove = 1,
        /// <summary>
        /// Signifies an update operation occured
        /// </summary>
        Update = 2
    }

    /// <summary>
    /// Represents the result of adding a grid to 
    /// <see cref="GlobalGridManager.TryAddGrid(Configuration.GridConfiguration, out ushort)"/>
    /// </summary>
    public enum GridAddResult
    {
        /// <summary>
        /// Grid was successfully added
        /// </summary>
        Success = 0,
        /// <summary>
        /// A grid with the same bounds already exists
        /// </summary>
        AlreadyExists = 1,
        /// <summary>
        /// The provided bounds are invalid
        /// </summary>
        InvalidBounds = 2,
        /// <summary>
        /// The maximum number of grids has been reached
        /// </summary>
        MaxGridsReached = 3
    }
}
