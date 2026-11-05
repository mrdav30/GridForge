//=======================================================================
// OccupantEntry.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Stores one occupant registration and the generation that owns its bucket slot.
/// </summary>
internal readonly struct OccupantEntry
{
    public readonly IVoxelOccupant Occupant;
    public readonly long Generation;

    public OccupantEntry(IVoxelOccupant occupant, long generation)
    {
        Occupant = occupant;
        Generation = generation;
    }
}
