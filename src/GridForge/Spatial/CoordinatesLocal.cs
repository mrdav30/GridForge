using GridForge.Grids;

namespace GridForge.Spatial
{
    /// <summary>
    /// Represents the local coordinates of a node within a single grid.
    /// Used to index nodes within a grid's spatial structure.
    /// </summary>
    public struct CoordinatesLocal
    {
        #region Properties

        /// <summary>
        /// The X position of the node in the local grid.
        /// </summary>
        public int x;

        /// <summary>
        /// The Y position of the node in the local grid.
        /// </summary>
        public int y;

        /// <summary>
        /// The Z position of the node in the local grid.
        /// </summary>
        public int z;

        /// <summary>
        /// Flag to determine is the struct instance was constructed or is default
        /// </summary>
        public bool IsAllocated { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="CoordinatesLocal"/> with an X and Y coordinate.
        /// Defaults Z to zero.
        /// </summary>
        public CoordinatesLocal(int xCord, int yCord) : this(xCord, yCord, 0) { }

        /// <summary>
        /// Initializes a new instance of <see cref="CoordinatesLocal"/> with X, Y, and Z coordinates.
        /// </summary>
        public CoordinatesLocal(int xCord, int yCord, int zCord)
        {
            x = xCord;
            y = yCord;
            z = zCord;

            IsAllocated = true;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Returns a string representation of the coordinates.
        /// </summary>
        public override string ToString()
        {
            return string.Format("({0}, {1}, {2})", x, y, z);
        }

        /// <summary>
        /// Computes a hash code for the coordinates, ensuring uniqueness in hashing collections.
        /// Uses <see cref="GlobalGridManager.GetSpawnHash"/> to generate a stable and consistent hash.
        /// </summary>
        public override int GetHashCode() => GlobalGridManager.GetSpawnHash(x, y, z);

        #endregion
    }
}
