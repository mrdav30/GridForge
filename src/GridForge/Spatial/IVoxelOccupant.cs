using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;

namespace GridForge.Spatial
{
    /// <summary>
    /// Represents an entity that can occupy a <see cref="Voxel"/>.
    /// Occupants can be dynamic entities such as units or items (obstacles).
    /// </summary>
    public interface IVoxelOccupant
    {
        /// <summary>
        /// The absolute world-space position of the occupant, representing its precise location in the environment.
        /// </summary>
        Vector3d Position { get; }

        /// <summary>
        /// The group Id used for grouping occupants within a scan cell.
        /// Occupants with the same group Id belong to the same logical group.
        /// This allows efficient retrieval of related occupants in spatial queries.
        /// </summary>
        byte OccupantGroupId { get; }

        /// <summary>
        /// Maps the global grid coordinates of the occupied <see cref="Voxel"/> 
        /// to the unique ticket identifier assigned when this occupant is added to a scan cell.
        /// </summary>
        SwiftDictionary<GlobalVoxelIndex, int> OccupyingIndexMap { get; }

        /// <summary>
        /// Called when adding/removing the occupant from a voxel.
        /// </summary>
        /// <param name="occupyingIndex"></param>
        /// <param name="ticket"></param>
        void SetOccupancy(GlobalVoxelIndex occupyingIndex, int ticket);
    }
}
