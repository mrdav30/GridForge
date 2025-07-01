using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace GridForge.Grids
{
    /// <summary>
    /// Represents a spatial partition within a grid, managing occupants at a finer granularity than grid voxels.
    /// Handles efficient tracking, retrieval, and removal of occupants within a designated scan cell area.
    /// </summary>
    public class ScanCell
    {
        #region Properties

        /// <summary>
        /// The global index of the grid this scan cell belongs to.
        /// </summary>
        public ushort GridIndex { get; private set; }

        /// <summary>
        /// A unique identifier for this scan cell in the grid, derived from spatial hashing.
        /// </summary>
        public int CellKey { get; private set; }


        /// <summary>
        /// Unique token identifying this scan cell instance.
        /// </summary>
        public int SpawnToken { get; private set; }

        /// <summary>
        /// Maps a <see cref="Voxel.GlobalIndex"/> to a bucket of associated <see cref="IVoxelOccupant"/> instances.
        /// </summary>
        private SwiftDictionary<GlobalVoxelIndex, SwiftBucket<IVoxelOccupant>> _voxelOccupants;

        /// <summary>
        /// The total number of occupants in this scan cell.
        /// </summary>
        public int CellOccupantCount { get; private set; }

        /// <summary>
        /// Indicates whether this scan cell is currently allocated in the grid.
        /// </summary>
        public bool IsAllocated { get; private set; }

        /// <summary>
        /// Determines whether this scan cell is occupied by any occupants.
        /// A scan cell is only considered occupied if it is allocated and contains at least one occupant.
        /// </summary>
        public bool IsOccupied => IsAllocated && CellOccupantCount > 0;

        #endregion

        #region Initialization & Reset

        /// <summary>
        /// Initializes the scan cell with the specified grid index and unique cell key.
        /// </summary>
        internal void Initialize(ushort gridIndex, int cellKey)
        {
            GridIndex = gridIndex;
            CellKey = cellKey;
            SpawnToken = GetHashCode();
            IsAllocated = true;
        }

        /// <summary>
        /// Resets the scan cell, clearing all occupants and returning memory to object pools.
        /// This effectively marks the scan cell as deallocated and removes all references.
        /// </summary>
        internal void Reset()
        {
            if (!IsAllocated)
                return;

            if (_voxelOccupants != null)
            {
                foreach (var kvp in _voxelOccupants)
                {
                    SwiftBucket<IVoxelOccupant> bucket = kvp.Value;
                    foreach (IVoxelOccupant occupant in bucket)
                        occupant.RemoveOccupancy(kvp.Key);
                    bucket.Clear();
                }

                _voxelOccupants = null;
            }

            CellOccupantCount = 0;

            GridIndex = ushort.MaxValue;
            CellKey = byte.MaxValue;

            IsAllocated = false;
        }

        #endregion

        #region Occupant Management

        /// <summary>
        /// Adds an occupant to this scan cell and tracks its presence.
        /// </summary>
        /// <param name="index">The global index of the voxel where the occupant resides.</param>
        /// <param name="occupant">The occupant instance to add.</param>
        /// <returns>An integer ticket representing the occupant's position in the data structure.</returns>
        internal void AddOccupant(GlobalVoxelIndex index, IVoxelOccupant occupant)
        {
            _voxelOccupants ??= new SwiftDictionary<GlobalVoxelIndex, SwiftBucket<IVoxelOccupant>>();
            if (!_voxelOccupants.TryGetValue(index, out SwiftBucket<IVoxelOccupant> bucket))
            {
                bucket = new SwiftBucket<IVoxelOccupant>();
                _voxelOccupants[index] = bucket;
            }

            int ticket = bucket.Add(occupant);
            occupant.SetOccupancy(index, ticket);
            CellOccupantCount++;
        }

        /// <summary>
        /// Removes an occupant from this scan cell.
        /// </summary>
        /// <param name="index">The global index of the voxel the occupant was assigned to.</param>
        /// <param name="occupant">The occupant instance to remove.</param>
        /// <param name="ticket">The ticket assigned to the occupant instance from this scancell.</param>
        /// <returns>True if the occupant was successfully removed; otherwise, false.</returns>
        internal bool TryRemoveOccupant(
            GlobalVoxelIndex index, 
            IVoxelOccupant occupant,
            int ticket)
        {
            // Reset occupant data regardless of success
            occupant.RemoveOccupancy(index);

            if (!IsOccupied || !_voxelOccupants.TryGetValue(index, out var bucket))
                return false;

            if (!bucket.TryRemoveAt(ticket))
                return false;

            // If the occupant was the last in its bucket, remove the entire bucket
            if (bucket.Count == 0)
                _voxelOccupants.Remove(index);

            CellOccupantCount--;

            return true;
        }

        #endregion

        #region Occupant Retrieval

        /// <summary>
        /// Retrieves all occupants associated with this ScanCell.
        /// </summary>
        /// <returns>An enumerable of occupants within this scan cell.</returns>
        internal IEnumerable<IVoxelOccupant> GetOccupants()
        {
            foreach (SwiftBucket<IVoxelOccupant> bucket in _voxelOccupants.Values)
            {
                foreach (IVoxelOccupant voxelOccupant in bucket)
                    yield return voxelOccupant;
            }
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        internal IEnumerable<IVoxelOccupant> GetConditionalOccupants(Func<byte, bool> groupConditional)
        {
            // Loop through each voxel's bucket and filter by the cluster condition
            foreach (var bucket in _voxelOccupants.Values)
            {
                foreach (var occupant in bucket)
                {
                    if (groupConditional(occupant.OccupantGroupId))
                        yield return occupant;
                }
            }
        }

        /// <summary>
        /// Retrieves all occupants associated with a specific voxel spawn token within this scan cell.
        /// </summary>
        /// <param name="index">The global index of the voxel.</param>
        /// <returns>An enumerable collection of occupants assigned to the voxel.</returns>
        internal IEnumerable<IVoxelOccupant> GetOccupantsFor(GlobalVoxelIndex index)
        {
            if (!_voxelOccupants.TryGetValue(index, out SwiftBucket<IVoxelOccupant> voxelOccupants))
                yield break;

            foreach (IVoxelOccupant voxelOccupant in voxelOccupants)
                yield return voxelOccupant;
        }

        /// <summary>
        /// Attempts to retrieve a specific occupant in this scan cell using a voxel's spawn key and occupant ticket.
        /// </summary>
        /// <param name="index">The global index of the voxel the occupant belongs to.</param>
        /// <param name="occupantTicket">The unique ticket identifying the occupant.</param>
        /// <param name="voxelOccupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        internal bool TryGetOccupantAt(
            GlobalVoxelIndex index, 
            int occupantTicket, 
            out IVoxelOccupant voxelOccupant)
        {
            voxelOccupant = null;
            if (!_voxelOccupants.TryGetValue(index, out SwiftBucket<IVoxelOccupant> voxelOccupants)
                || !voxelOccupants.IsAllocated(occupantTicket))
            {
                return false;
            }

            voxelOccupant = voxelOccupants[occupantTicket];
            return true;
        }

        #endregion

        /// <inheritdoc/>
        public override int GetHashCode() => GlobalGridManager.GetSpawnHash(
                GridIndex,
                CellKey,
                31
            );
    }
}
