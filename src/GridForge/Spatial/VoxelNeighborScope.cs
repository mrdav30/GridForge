//=======================================================================
// VoxelNeighborScope.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace GridForge.Spatial;

/// <summary>
/// Selects which grid groups are included by voxel contact-neighbor queries.
/// </summary>
[Flags]
public enum VoxelNeighborScope : byte
{
    /// <summary>
    /// No neighbor groups are included.
    /// </summary>
    None = 0,

    /// <summary>
    /// Include touching voxels from only the source voxel's owning grid.
    /// </summary>
    SourceGrid = 1,

    /// <summary>
    /// Include touching voxels from other grids with the same topology kind.
    /// </summary>
    SameTopologyGrids = 2,

    /// <summary>
    /// Include touching voxels from grids with a different topology kind.
    /// </summary>
    MixedTopologyGrids = 4,

    /// <summary>
    /// Include touching voxels from the source grid, same-topology grids, and mixed-topology grids.
    /// </summary>
    All = SourceGrid | SameTopologyGrids | MixedTopologyGrids
}
