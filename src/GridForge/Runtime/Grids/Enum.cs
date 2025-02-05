namespace GridForge.Grids
{
    /// <summary>
    /// Represents the 26 possible neighbor directions in a 3x3x3 grid.
    /// These directions are used for spatial relationships between grids and nodes.
    /// </summary>
    public enum LinearDirection
    {
        None = -1,
        West = 0,          // (-1, 0, 0)
        South = 1,         // (0, 0, -1)
        East = 2,          // (0, 0, 1)
        North = 3,         // (1, 0, 0)
        SouthWest = 4,     // (-1, 0, -1)
        NorthWest = 5,     // (-1, 0, 1)
        SouthEast = 6,     // (1, 0, -1)
        NorthEast = 7,     // (1, 0, 1)
        BelowWest = 8,     // (-1, -1, 0)
        BelowSouth = 9,    // (0, -1, -1)
        BelowEast = 10,    // (0, -1, 1)
        BelowNorth = 11,   // (1, -1, 0)
        BelowSouthWest = 12, // (-1, -1, -1)
        BelowNorthWest = 13, // (-1, -1, 1)
        BelowSouthEast = 14, // (1, -1, -1)
        BelowNorthEast = 15, // (1, -1, 1)
        Below = 16,        // (0, -1, 0)
        AboveWest = 17,    // (-1, 1, 0)
        AboveSouth = 18,   // (0, 1, -1)
        AboveEast = 19,    // (0, 1, 1)
        AboveNorth = 20,   // (1, 1, 0)
        AboveSouthWest = 21, // (-1, 1, -1)
        AboveNorthWest = 22, // (-1, 1, 1)
        AboveSouthEast = 23, // (1, 1, -1)
        AboveNorthEast = 24, // (1, 1, 1)
        Above = 25         // (0, 1, 0)
    }

    /// <summary>
    /// Represents different types of grid-related changes that can occur.
    /// Used to track modifications such as adding/removing neighbors, obstacles, or occupants.
    /// </summary>
    public enum GridChange
    {
        None = -1,

        /// <summary>
        /// A new neighbor grid was added.
        /// </summary>
        AddGridNeighbor = 0,

        /// <summary>
        /// A neighboring grid was removed.
        /// </summary>
        RemoveGridNeighbor = 1,

        /// <summary>
        /// A neighboring grid was updated.
        /// </summary>
        UpdateGridNeighbor = 2,

        /// <summary>
        /// An obstacle was added to a node.
        /// </summary>
        AddNodeObstacle = 3,

        /// <summary>
        /// An obstacle was removed from a node.
        /// </summary>
        RemoveNodeObstacle = 4,

        /// <summary>
        /// An obstacle on a node was updated.
        /// </summary>
        UpdateNodeObstacle = 5,

        /// <summary>
        /// An occupant was added to a node.
        /// </summary>
        AddNodeOccupant = 6,

        /// <summary>
        /// An occupant was removed from a node.
        /// </summary>
        RemoveNodeOccupant = 7,

        /// <summary>
        /// An occupant on a node was updated.
        /// </summary>
        UpdateNodeOccupant = 8
    }
}
