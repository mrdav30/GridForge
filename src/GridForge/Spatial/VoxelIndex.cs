using SwiftCollections;
using System;

namespace GridForge.Spatial
{
    /// <summary>
    /// Represents the local coordinates of a voxel within a single grid.
    /// Used to index voxels within a grid's spatial structure.
    /// </summary>
    public struct VoxelIndex : IEquatable<VoxelIndex>
    {
        #region Properties

        /// <summary>
        /// The X position of the voxel in the local grid.
        /// </summary>
        public int x;

        /// <summary>
        /// The Y position of the voxel in the local grid.
        /// </summary>
        public int y;

        /// <summary>
        /// The Z position of the voxel in the local grid.
        /// </summary>
        public int z;

        /// <summary>
        /// Flag to determine is the struct instance was constructed or is default
        /// </summary>
        public bool IsAllocated { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="VoxelIndex"/> with an X and Y coordinate.
        /// Defaults Z to zero.
        /// </summary>
        public VoxelIndex(int xCord, int yCord) : this(xCord, yCord, 0) { }

        /// <summary>
        /// Initializes a new instance of <see cref="VoxelIndex"/> with X, Y, and Z coordinates.
        /// </summary>
        public VoxelIndex(int xCord, int yCord, int zCord)
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
        /// Uses <see cref="HashTools"/> to generate a stable and consistent hash.
        /// </summary>
        public override readonly int GetHashCode() => HashTools.CombineHashCodes(x, y, z);

        /// <inheritdoc/>
        public readonly bool Equals(VoxelIndex other)
        {
            return x == other.x
                && y == other.y
                && z == other.z;
        }

        /// <inheritdoc/>
        public override readonly bool Equals(object obj)
        {
            return obj is VoxelIndex other && Equals(other);
        }

        #endregion
    }
}
