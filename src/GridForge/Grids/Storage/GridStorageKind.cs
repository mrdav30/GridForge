//=======================================================================
// GridStorageKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Storage;

/// <summary>
/// Identifies how a grid stores its physical voxels.
/// </summary>
public enum GridStorageKind
{
    /// <summary>
    /// Every topology-local voxel index within the grid address space is physically allocated.
    /// </summary>
    Dense = 0,

    /// <summary>
    /// Only explicitly configured topology-local voxel indices are physically allocated.
    /// </summary>
    Sparse = 1
}
