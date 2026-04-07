using FixedMathSharp;
using GridForge.Grids;
using System;

namespace GridForge.Spatial;

/// <summary>
/// Represents an entity that can occupy a <see cref="Voxel"/>.
/// Occupants can be dynamic entities such as units or items (obstacles).
/// GridForge owns occupancy bookkeeping for registered occupants.
/// </summary>
public interface IVoxelOccupant
{
    /// <summary>
    /// A globally unique identifier for the occupant.
    /// </summary>
    Guid GlobalId { get; }

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
}
