using FixedMathSharp;
using GridForge.Grids;

namespace GridForge.Spatial
{
    /// <summary>
    /// Represents an entity that can occupy a <see cref="Node"/>.
    /// Occupants can be dynamic entities such as units or items (obstacles).
    /// </summary>
    public interface INodeOccupant
    {
        /// <summary>
        /// The cluster key used for grouping occupants within a scan cell.
        /// Occupants with the same cluster key belong to the same logical group.
        /// This allows efficient retrieval of related occupants in spatial queries.
        /// </summary>
        byte ClusterKey { get; }

        /// <summary>
        /// A unique ticket identifier assigned when this occupant is added to a scan cell.
        /// Used for efficient tracking and removal.
        /// </summary>
        int OccupantTicket { get; set; }

        /// <summary>
        /// The world-space position of this occupant.
        /// </summary>
        Vector3d WorldPosition { get; }

        /// <summary>
        /// The global grid coordinates where this occupant is located.
        /// </summary>
        CoordinatesGlobal GridCoordinates { get; set; }
    }
}
