using FixedMathSharp;

namespace GridForge.Blockers
{
    /// <summary>
    /// A manually placed blocker that obstructs a defined bounding area.
    /// </summary>
    public class BoundsBlocker : Blocker
    {
        private BoundingArea _blockArea;

        public BoundsBlocker(
            BoundingArea blockArea, 
            bool isActive = true, 
            bool cacheCoveredNodes = false) : base(isActive, cacheCoveredNodes)
        {
            _blockArea = blockArea;
        }

        protected override Vector3d GetBoundsMin() => _blockArea.Min;
        protected override Vector3d GetBoundsMax() => _blockArea.Max;
    }
}
