using FixedMathSharp;
using GridForge.Grids;

namespace GridForge.Blockers
{
    /// <summary>
    /// Base class for all grid blockers that handles applying and removing obstacles.
    /// </summary>
    public abstract class Blocker : IBlocker
    {
        public bool IsActive { get; private set; }

        protected Blocker(bool active = true)
        {
            IsActive = active;
        }

        /// <summary>
        /// Applies the blockage by marking nodes as obstacles.
        /// </summary>
        public virtual void ApplyBlockage()
        {
            if (!IsActive)
                return;

            foreach (Node node in GlobalGridManager.GetCoveredNodes(GetBoundsMin(), GetBoundsMax()))
                node.AddObstacle();
        }

        /// <summary>
        /// Removes the blockage by clearing obstacle markers from nodes.
        /// </summary>
        public virtual void RemoveBlockage()
        {
            foreach (Node node in GlobalGridManager.GetCoveredNodes(GetBoundsMin(), GetBoundsMax()))
                node.RemoveObstacle();
        }

        /// <summary>
        /// Gets the min bounds of the area to block. Must be implemented by subclasses.
        /// </summary>
        protected abstract Vector3d GetBoundsMin();

        /// <summary>
        /// Gets the max bounds of the area to block. Must be implemented by subclasses.
        /// </summary>
        protected abstract Vector3d GetBoundsMax();
    }
}
