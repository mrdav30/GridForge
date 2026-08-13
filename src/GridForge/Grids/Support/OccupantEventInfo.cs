//=======================================================================
// OccupantEventInfo.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Immutable snapshot describing an occupant mutation on a voxel.
/// </summary>
/// <remarks>
/// Occupant notifications are not part of the world's committed navigation-change stream and
/// therefore do not carry a <see cref="GridChangeStamp"/>.
/// </remarks>
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
    public readonly OccupantTicket Ticket;

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
        OccupantTicket ticket,
        byte occupantCount)
    {
        VoxelIndex = voxelIndex;
        Occupant = occupant;
        Ticket = ticket;
        OccupantCount = occupantCount;
    }
}
