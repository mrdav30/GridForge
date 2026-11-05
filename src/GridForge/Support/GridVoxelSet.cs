//=======================================================================
// GridVoxelSet.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids;
using SwiftCollections;

namespace GridForge;

/// <summary>
/// Groups the matching voxels for one grid.
/// </summary>
/// <remarks>
/// Query iterators lend pooled <see cref="Voxels"/> lists until their enumeration
/// completes or is disposed. Consume them inside the enumeration, or copy the
/// contents into caller-owned storage before retaining results.
/// </remarks>
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
