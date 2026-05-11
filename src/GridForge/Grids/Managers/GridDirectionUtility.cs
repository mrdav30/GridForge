using GridForge.Spatial;
using System.Diagnostics;

namespace GridForge.Grids;

/// <summary>
/// Provides stateless helpers for translating relative grid offsets into directions.
/// </summary>
public static class GridDirectionUtility
{
    /// <summary>
    /// Converts a 3D offset into a corresponding <see cref="SpatialDirection"/> in a 3x3x3 grid.
    /// </summary>
    /// <param name="gridOffset">The (x, y, z) offset from the center voxel.</param>
    /// <returns>The corresponding <see cref="SpatialDirection"/>, or <see cref="SpatialDirection.None"/> if invalid.</returns>
    public static SpatialDirection GetNeighborDirectionFromOffset((int x, int y, int z) gridOffset)
    {
        if(IsValidOffset(gridOffset) == false)
        {
            GridForgeLogger.DebugChannel.Info($"Invalid grid offset: {gridOffset}. Offsets must be in the range [-1, 1] for each axis.");
            return SpatialDirection.None;
        }

        if (gridOffset == (0, 0, 0)) // The center voxel does not correspond to any direction.
            return SpatialDirection.None;

        for (int i = 0; i < SpatialAwareness.DirectionOffsets.Length; i++)
        {
            if (SpatialAwareness.DirectionOffsets[i] == gridOffset)
                return (SpatialDirection)i;
        }

        return SpatialDirection.None;
    }

    private static bool IsValidOffset((int x, int y, int z) offset)
    {
        return offset.x >= -1 && offset.x <= 1 &&
               offset.y >= -1 && offset.y <= 1 &&
               offset.z >= -1 && offset.z <= 1;
    }
}
