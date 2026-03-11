using GridForge.Grids;
using SwiftCollections;

namespace GridForge;

/// <summary>
/// Used to group query results on a grid to voxel level
/// </summary>
public readonly struct GridVoxelSet
{
    /// <summary>
    /// The grid containing the voxels from the resulting query
    /// </summary>
    public readonly VoxelGrid Grid;

    /// <summary>
    /// A list of voxels that match the provided query
    /// </summary>
    public readonly SwiftList<Voxel> Voxels;

    /// <summary>
    /// Initializes a new grouped grid query result.
    /// </summary>
    /// <param name="grid">The grid containing the matching voxels.</param>
    /// <param name="voxels">The matching voxels for the grid.</param>
    public GridVoxelSet(VoxelGrid grid, SwiftList<Voxel> voxels)
    {
        Grid = grid;
        Voxels = voxels;
    }
}
