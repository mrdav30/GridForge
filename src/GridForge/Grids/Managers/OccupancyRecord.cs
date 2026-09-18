//=======================================================================
// OccupancyRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Grids;

/// <summary>
/// Tracks all voxel registrations for a single occupant.
/// </summary>
internal sealed class OccupancyRecord
{
    public readonly IVoxelOccupant Occupant;
    public readonly SwiftDictionary<WorldVoxelIndex, OccupantTicket> Tickets = new SwiftDictionary<WorldVoxelIndex, OccupantTicket>();

    public OccupancyRecord(IVoxelOccupant occupant)
    {
        Occupant = occupant;
    }
}
