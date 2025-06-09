using FixedMathSharp;
using GridForge.Grids;
using System;

namespace GridForge.Configuration
{
    /// <summary>
    /// Defines the configuration parameters for a grid, including boundaries and scan cell size.
    /// Used to initialize and validate grid properties before creation.
    /// </summary>
    [Serializable]
    public struct GridConfiguration
    {
        /// <summary>
        /// The default size of each scan cell.
        /// </summary>
        public const int DefaultScanCellSize = 8;

        #region Properties

        /// <summary>
        /// The minimum boundary of the grid in world coordinates.
        /// </summary>
        public Vector3d BoundsMin { get; private set; }

        /// <summary>
        /// The maximum boundary of the grid in world coordinates.
        /// </summary>
        public Vector3d BoundsMax { get; private set; }

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
        public bool IsAllocated { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="GridConfiguration"/> with specified bounds and scan cell size.
        /// Ensures that <see cref="BoundsMin"/> is always less than or equal to <see cref="BoundsMax"/>.
        /// </summary>
        /// <param name="min">The minimum boundary of the grid.</param>
        /// <param name="max">The maximum boundary of the grid.</param>
        /// <param name="scanCellSize">The size of scan cells within the grid. Default is 8.</param>
        public GridConfiguration(
            Vector3d min,
            Vector3d max,
            int scanCellSize = DefaultScanCellSize)
        {
            if (min > max)
                GridForgeLogger.Warn("GridMin was greater than GridMax, auto-correcting values.");

            // Ensures GridMin <= GridMax for each coordinate axis
            (BoundsMin, BoundsMax) = GlobalGridManager.SnapBoundsToVoxelSize(min, max);

            // Calculate the center point of the corrected boundaries
            GridCenter = (BoundsMin + BoundsMax) / 2;

            ScanCellSize = scanCellSize > 0 ? scanCellSize : DefaultScanCellSize;

            IsAllocated = true;
        }

        #endregion

        /// <inheritdoc/>
        public override int GetHashCode() => BoundsMin.GetHashCode() ^ BoundsMax.GetHashCode();
    }
}