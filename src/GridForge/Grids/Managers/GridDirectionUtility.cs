//=======================================================================
// GridDirectionUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;
using System;

namespace GridForge.Grids;

/// <summary>
/// Provides stateless helpers for translating relative grid offsets into directions.
/// </summary>
public static class GridDirectionUtility
{
    private const int OffsetLookupSize = 27;
    private static readonly SpatialDirection[] DirectionLookup = CreateDirectionLookup();

    /// <summary>
    /// Converts a 3D offset into a corresponding <see cref="SpatialDirection"/> in a 3x3x3 grid.
    /// </summary>
    /// <param name="gridOffset">The (x, y, z) offset from the center voxel.</param>
    /// <returns>The corresponding <see cref="SpatialDirection"/>, or <see cref="SpatialDirection.None"/> if invalid.</returns>
    public static SpatialDirection GetNeighborDirectionFromOffset((int x, int y, int z) gridOffset)
    {
        if (!TryGetOffsetLookupKey(gridOffset, out int lookupKey))
        {
            GridForgeLogger.DebugChannel.Info($"Invalid grid offset: {gridOffset}. Offsets must be in the range [-1, 1] for each axis.");
            return SpatialDirection.None;
        }

        return DirectionLookup[lookupKey];
    }

    private static SpatialDirection[] CreateDirectionLookup()
    {
        SpatialDirection[] lookup = new SpatialDirection[OffsetLookupSize];
        Array.Fill(lookup, SpatialDirection.None);

        for (int i = 0; i < SpatialAwareness.DirectionOffsets.Length; i++)
        {
            if (TryGetOffsetLookupKey(SpatialAwareness.DirectionOffsets[i], out int lookupKey))
                lookup[lookupKey] = (SpatialDirection)i;
        }

        return lookup;
    }

    private static bool TryGetOffsetLookupKey((int x, int y, int z) offset, out int lookupKey)
    {
        int x = offset.x + 1;
        int y = offset.y + 1;
        int z = offset.z + 1;

        if ((uint)x > 2u || (uint)y > 2u || (uint)z > 2u)
        {
            lookupKey = -1;
            return false;
        }

        lookupKey = x * 9 + y * 3 + z;
        return true;
    }
}
