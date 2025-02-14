using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GridForge.Blockers
{
    /// <summary>
    /// Base class for all grid blockers that handles applying and removing obstacles.
    /// </summary>
    public abstract class Blocker : IBlocker
    {
        /// <summary>
        /// Unique token representing this blockage instance.
        /// </summary>
        public int BlockageToken { get; private set; }

        /// <summary>
        /// Indicates whether the blocker is currently active.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// The cached minimum bounds of the blockage area.
        /// </summary>
        public Vector3d CacheMin { get; private set; }

        /// <summary>
        /// The cached maximum bounds of the blockage area.
        /// </summary>
        public Vector3d CacheMax { get; private set; }

        /// <summary>
        /// Tracks whether the blocker is currently blocking nodes.
        /// </summary>
        public bool IsBlocking { get; private set; }

        public bool CacheCoveredNodes { get; private set; }

        private SwiftList<GridNodeSet> _cachedCoveredNodes;

        /// <summary>
        /// Event triggered when a blockage is added or removed.
        /// </summary>
        public static event Action<GridChange, Vector3d, Vector3d> OnBlockageChanged;

        protected Blocker(bool active = true, bool cacheCoveredNodes = false)
        {
            IsActive = active;
            CacheCoveredNodes = cacheCoveredNodes;
        }

        /// <summary>
        /// Applies the blockage by marking nodes as obstacles.
        /// </summary>
        public virtual void ApplyBlockage()
        {
            if (!IsActive || IsBlocking)
                return;

            CacheMin = GetBoundsMin();
            CacheMax = GetBoundsMax();
            // Generate a unique blockage token based on the min/max bounds
            BlockageToken = GlobalGridManager.GetSpawnHash(
                7,
                CacheMin.GetHashCode(),
                CacheMax.GetHashCode());

            // Iterate over all affected nodes and apply obstacles
            foreach (GridNodeSet covered in GridTracer.GetCoveredNodes(CacheMin, CacheMax))
            {
                foreach (Node node in covered.Nodes)
                    covered.Grid.TryAddObstacle(node, BlockageToken);

                if(CacheCoveredNodes)
                    _cachedCoveredNodes.Add(covered);
            }

            IsBlocking = true;

            NotifyBlockageChanged(GridChange.Add);
        }

        /// <summary>
        /// Removes the blockage by clearing obstacle markers from nodes.
        /// </summary>
        public virtual void RemoveBlockage()
        {
            if (!IsBlocking)
                return;

            IEnumerable<GridNodeSet> coveredNodes = CacheCoveredNodes 
                ? _cachedCoveredNodes 
                : GridTracer.GetCoveredNodes(CacheMin, CacheMax);

            // Clear the obstacle markers from all affected nodes before resetting the blocker state
            foreach (GridNodeSet covered in coveredNodes)
            {
                foreach (Node node in covered.Nodes)
                    covered.Grid.TryRemoveObstacle(node, BlockageToken);
            }

            IsBlocking = false;
            _cachedCoveredNodes = null;

            NotifyBlockageChanged(GridChange.Remove);
        }

        private void NotifyBlockageChanged(GridChange change)
        {
            try
            {
                OnBlockageChanged?.Invoke(change, CacheMin, CacheMax);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Blocker] Error in blockage notification: {ex.Message} | Change: {change} | Bounds: {CacheMin} -> {CacheMax}");
            }
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
