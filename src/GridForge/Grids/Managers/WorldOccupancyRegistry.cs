//=======================================================================
// WorldOccupancyRegistry.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Grids;

/// <summary>
/// Synchronizes one world's tracked occupant registrations.
/// </summary>
internal sealed class WorldOccupancyRegistry
{
    public readonly object SyncRoot = new object();
    public readonly SwiftDictionary<Guid, OccupancyRecord> Records = new SwiftDictionary<Guid, OccupancyRecord>();

    // Keep at most one value-only table through the dictionary's first native growth.
    private const int MaxReusableCapacity = 4 * SwiftDictionary<WorldVoxelIndex, OccupantTicket>.DefaultCapacity;
    private SwiftDictionary<WorldVoxelIndex, OccupantTicket>? _emptyTickets;

    public SwiftDictionary<WorldVoxelIndex, OccupantTicket> TakeTicketMap()
    {
        var tickets = _emptyTickets ?? new SwiftDictionary<WorldVoxelIndex, OccupantTicket>();
        _emptyTickets = null;
        return tickets;
    }

    public void RemoveTicket(Guid id, OccupancyRecord record, WorldVoxelIndex index)
    {
        if (record.MultipleTickets is { } tickets)
        {
            if (tickets.Count > 1)
            {
                tickets.Remove(index);
                return;
            }
            record.MultipleTickets = null;
            if (_emptyTickets == null && tickets.Capacity <= MaxReusableCapacity)
            {
                tickets.Clear();
                _emptyTickets = tickets;
            }
        }
        Records.Remove(id);
    }

    public void Clear()
    {
        Records.Clear();
        _emptyTickets = null;
    }
}
