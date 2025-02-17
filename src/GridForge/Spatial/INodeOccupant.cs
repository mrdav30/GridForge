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
        bool IsNodeOccupant { get; set; }

        /// <summary>
        /// The group Id used for grouping occupants within a scan cell.
        /// Occupants with the same group Id belong to the same logical group.
        /// This allows efficient retrieval of related occupants in spatial queries.
        /// </summary>
        byte OccupantGroupId { get; }

        /// <summary>
        /// A unique ticket identifier assigned when this occupant is added to a scan cell.
        /// Used for efficient tracking and removal.
        /// </summary>
        int OccupantTicket { get; set; }

        /// <summary>
        /// The absolute world-space position of the occupant, representing its precise location in the environment.
        /// </summary>
        Vector3d WorldPosition { get; set; }

        /// <summary>
        /// The global grid coordinates of the <see cref="Node"/> this occupant is being added to.
        /// </summary>
        CoordinatesGlobal GridCoordinates { get; set; }
    }
}
