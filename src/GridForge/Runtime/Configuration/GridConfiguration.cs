using FixedMathSharp;
using System;

namespace GridForge.Configuration
{
    /// <summary>
    /// Defines the configuration parameters for a grid, including boundaries and scan cell size.
    /// Used to initialize and validate grid properties before creation.
    /// </summary>
    public struct GridConfiguration
    {
        #region Properties

        /// <summary>
        /// The minimum boundary of the grid in world coordinates.
        /// </summary>
        public Vector3d GridMin { get; private set; }

        /// <summary>
        /// The maximum boundary of the grid in world coordinates.
        /// </summary>
        public Vector3d GridMax { get; private set; }

        /// <summary>
        /// The center point of the grid's bounding volume.
        /// </summary>
        public Vector3d GridCenter { get; private set; }

        /// <summary>
        /// The size of each scan cell, determining the granularity of spatial partitioning.
        /// Customizable based on grid density and expected entity distribution.
        /// </summary>
        public int ScanCellSize { get; private set; }

        /// <summary>
        /// Indicates whether this grid configuration has been set.
        /// </summary>
        public bool IsSet { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="GridConfiguration"/> with specified bounds and scan cell size.
        /// Ensures that <see cref="GridMin"/> is always less than or equal to <see cref="GridMax"/>.
        /// </summary>
        /// <param name="min">The minimum boundary of the grid.</param>
        /// <param name="max">The maximum boundary of the grid.</param>
        /// <param name="scanCellSize">The size of scan cells within the grid. Default is 8.</param>
        public GridConfiguration(
            Vector3d min,
            Vector3d max,
            int scanCellSize = 8)
        {
            // Ensure GridMin <= GridMax for each coordinate axis
            GridMin = new Vector3d(
                FixedMath.Min(min.x, max.x),
                FixedMath.Min(min.y, max.y),
                FixedMath.Min(min.z, max.z)
            );

            GridMax = new Vector3d(
                FixedMath.Max(min.x, max.x),
                FixedMath.Max(min.y, max.y),
                FixedMath.Max(min.z, max.z)
            );

            // Calculate the center point of the corrected boundaries
            GridCenter = (GridMin + GridMax) / 2;

            ScanCellSize = scanCellSize;
            IsSet = true;

            // Log a warning if min and max were initially swapped
            if (min.x > max.x || min.y > max.y || min.z > max.z)
                Console.WriteLine($"Warning: GridMin was greater than GridMax, auto-correcting values.");
        }

        #endregion
    }
}