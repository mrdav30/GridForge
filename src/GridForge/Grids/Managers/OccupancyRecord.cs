//=======================================================================
// OccupancyRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
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
    public readonly WorldVoxelIndex FirstIndex;
    public readonly OccupantTicket FirstTicket;
    public SwiftDictionary<WorldVoxelIndex, OccupantTicket>? MultipleTickets;

    public OccupancyRecord(IVoxelOccupant occupant, WorldVoxelIndex index, OccupantTicket ticket)
    {
        Occupant = occupant;
        FirstIndex = index;
        FirstTicket = ticket;
    }

    public bool TryAddTicket(WorldOccupancyRegistry registry, WorldVoxelIndex index, OccupantTicket ticket)
    {
        if (MultipleTickets == null)
        {
            if (index == FirstIndex)
                return false;

            MultipleTickets = registry.TakeTicketMap();
            MultipleTickets.Add(FirstIndex, FirstTicket);
        }
        return MultipleTickets.Add(index, ticket);
    }

    public bool TryGetTicket(WorldVoxelIndex index, out OccupantTicket ticket)
    {
        if (MultipleTickets != null)
            return MultipleTickets.TryGetValue(index, out ticket);
        if (index == FirstIndex)
        {
            ticket = FirstTicket;
            return true;
        }
        ticket = default;
        return false;
    }
}
