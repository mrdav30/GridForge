using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;
using SwiftCollections;
using System;
using System.Collections.Generic;

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
        /// Tracks whether the blocker is currently blocking voxels.
        /// </summary>
        public bool IsBlocking { get; private set; }

        /// <summary>
        /// Flags whether or not to hold onto a reference of the voxels this blocker covers.
        /// </summary>
        public bool CacheCoveredVoxels { get; private set; }

        /// <summary>
        /// The cached voxels this blocker is currently blocking if <see cref="CacheCoveredVoxels"/> is true.
        /// </summary>
        private SwiftList<GridVoxelSet> _cachedCoveredVoxels;

        /// <summary>
        /// Event triggered when a blockage is added or removed.
        /// </summary>
        public static event Action<GridChange, Vector3d, Vector3d> OnBlockageChanged;

        /// <summary>
        /// Initializes a new blocker instance.
        /// </summary>
        /// <param name="active">Flag whether or not this blocker will block on update.</param>
        /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels that are blocked.</param>
        protected Blocker(bool active = true, bool cacheCoveredVoxels = false)
        {
            IsActive = active;
            CacheCoveredVoxels = cacheCoveredVoxels;
        }

        /// <summary>
        /// Toggles the blocker from inactive to active or active to inactive state
        /// If object is currently blocking, the blocker will be removed.
        /// If object is not active and not blocking, the blocker will be applied.
        /// </summary>
        /// <param name="status"></param>
        public void ToggleStatus(bool status)
        {
            if (!status && IsBlocking)
            {
                RemoveBlockage();
                IsActive = false;
                return;
            }

            if(status && !IsBlocking)
            {
                IsActive = true;
                ApplyBlockage();
            }
        }

        /// <summary>
        /// Applies the blockage by marking voxels as obstacles.
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

            bool hasCoverage = false;
            // Iterate over all affected voxels and apply obstacles
            foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(CacheMin, CacheMax))
            {
                if (covered.Voxels.Count <= 0)
                    continue;

                hasCoverage = true;

                foreach (Voxel voxel in covered.Voxels)
                    covered.Grid.TryAddObstacle(voxel, BlockageToken);

                if (CacheCoveredVoxels)
                    _cachedCoveredVoxels.Add(covered);
            }

            if (hasCoverage)
            {
                IsBlocking = true;
                NotifyBlockageChanged(GridChange.Add);
            }
        }

        /// <summary>
        /// Removes the blockage by clearing obstacle markers from voxels.
        /// </summary>
        public virtual void RemoveBlockage()
        {
            if (!IsBlocking)
                return;

            IEnumerable<GridVoxelSet> coveredVoxels = CacheCoveredVoxels
                ? _cachedCoveredVoxels
                : GridTracer.GetCoveredVoxels(CacheMin, CacheMax);

            // Clear the obstacle markers from all affected voxels before resetting the blocker state
            foreach (GridVoxelSet covered in coveredVoxels)
            {
                foreach (Voxel voxel in covered.Voxels)
                    covered.Grid.TryRemoveObstacle(voxel, BlockageToken);
            }

            IsBlocking = false;
            _cachedCoveredVoxels = null;

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
                GridForgeLogger.Error($"Blockage notification: {ex.Message} | Change: {change} | Bounds: {CacheMin} -> {CacheMax}");
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
