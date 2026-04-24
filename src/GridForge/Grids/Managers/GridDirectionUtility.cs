using GridForge.Spatial;
using System.Diagnostics;

namespace GridForge.Grids;

/// <summary>
/// Provides stateless helpers for translating relative grid offsets into directions.
/// </summary>
internal static class GridDirectionUtility
{
    /// <summary>
    /// Converts a 3D offset into a corresponding <see cref="SpatialDirection"/> in a 3x3x3 grid.
    /// </summary>
    /// <param name="gridOffset">The (x, y, z) offset from the center voxel.</param>
    /// <returns>The corresponding <see cref="SpatialDirection"/>, or <see cref="SpatialDirection.None"/> if invalid.</returns>
    internal static SpatialDirection GetNeighborDirectionFromOffset((int x, int y, int z) gridOffset)
    {
        Debug.Assert(gridOffset.x >= -1 && gridOffset.x <= 1, "Invalid x offset.");
        Debug.Assert(gridOffset.y >= -1 && gridOffset.y <= 1, "Invalid y offset.");
        Debug.Assert(gridOffset.z >= -1 && gridOffset.z <= 1, "Invalid z offset.");

        if (gridOffset == (0, 0, 0))
            return SpatialDirection.None;

        for (int i = 0; i < SpatialAwareness.DirectionOffsets.Length; i++)
        {
            if (SpatialAwareness.DirectionOffsets[i] == gridOffset)
                return (SpatialDirection)i;
        }

        return SpatialDirection.None;
    }
}
