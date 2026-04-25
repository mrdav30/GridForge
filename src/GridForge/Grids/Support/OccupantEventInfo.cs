using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Immutable snapshot describing an occupant mutation on a voxel.
/// </summary>
public readonly struct OccupantEventInfo
{
    /// <summary>
    /// The voxel affected by the occupant mutation.
    /// </summary>
    public readonly WorldVoxelIndex VoxelIndex;

    /// <summary>
    /// The occupant that was added to or removed from the voxel.
    /// </summary>
    public readonly IVoxelOccupant Occupant;

    /// <summary>
    /// The scan-cell ticket assigned to the occupant for this voxel.
    /// </summary>
    public readonly int Ticket;

    /// <summary>
    /// The number of occupants on the voxel after the mutation completes.
    /// </summary>
    public readonly byte OccupantCount;

    /// <summary>
    /// The grid index containing <see cref="VoxelIndex"/>.
    /// </summary>
    public readonly ushort GridIndex => VoxelIndex.GridIndex;

    /// <summary>
    /// Initializes a new immutable occupant mutation snapshot.
    /// </summary>
    public OccupantEventInfo(
        WorldVoxelIndex voxelIndex,
        IVoxelOccupant occupant,
        int ticket,
        byte occupantCount)
    {
        VoxelIndex = voxelIndex;
        Occupant = occupant;
        Ticket = ticket;
        OccupantCount = occupantCount;
    }
}
