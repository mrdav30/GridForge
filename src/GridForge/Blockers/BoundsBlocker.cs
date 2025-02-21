using FixedMathSharp;

namespace GridForge.Blockers
{
    /// <summary>
    /// A manually placed blocker that obstructs a defined bounding area.
    /// </summary>
    public class BoundsBlocker : Blocker
    {
        private BoundingArea _blockArea;

        /// <summary>
        /// Initializes a new bounds blocker
        /// </summary>
        /// <param name="blockArea">The bounding area to block.</param>
        /// <param name="isActive">Flag whether or not blocker is active.</param>
        /// <param name="cacheCoveredNodes">Flag whether or not to cache covered nodes.</param>
        public BoundsBlocker(
            BoundingArea blockArea, 
            bool isActive = true, 
            bool cacheCoveredNodes = false) : base(isActive, cacheCoveredNodes)
        {
            _blockArea = blockArea;
        }

        /// <inheritdoc cref="Blocker.GetBoundsMin"/>
        protected override Vector3d GetBoundsMin() => _blockArea.Min;

        /// <inheritdoc cref="Blocker.GetBoundsMax"/>
        protected override Vector3d GetBoundsMax() => _blockArea.Max;
    }
}
