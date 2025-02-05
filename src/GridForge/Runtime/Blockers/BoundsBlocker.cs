using FixedMathSharp;

namespace GridForge.Blockers
{
    /// <summary>
    /// A manually placed blocker that obstructs a defined bounding area.
    /// </summary>
    public class BoundsBlocker : Blocker
    {
        private BoundingArea _blockArea;

        public BoundsBlocker(bool isActive, BoundingArea blockArea)
            : base(isActive)
        {
            _blockArea = blockArea;
        }

        protected override Vector3d GetBoundsMin() => _blockArea.Min;
        protected override Vector3d GetBoundsMax() => _blockArea.Max;
    }
}
