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
