using GridForge.Grids;


using GridForge.Spatial;


/// <summary>
/// Provides extension methods for managing <see cref="IVoxelPartition"/> instances on <see cref="Voxel"/> objects.
/// This allows adding and removing partitions while maintaining their state.
/// </summary>
public static class PartitionExtensions
{
    /// <summary>
    /// Attaches a partition to a specified <see cref="Voxel"/>, updating its state and invoking initialization logic.
    /// </summary>
    /// <param name="partition">The partition to attach.</param>
    /// <param name="voxel">The target voxel where the partition will be added.</param>
    public static void AddToVoxel(this IVoxelPartition partition, Voxel voxel)
    {
        partition.VoxelCoordinates = voxel.GlobalCoordinates;
        partition.IsPartitioned = true;
        partition.OnAddToVoxel(voxel);
    }

    /// <summary>
    /// Detaches a partition from a specified <see cref="Voxel"/>, resetting its state and invoking cleanup logic.
    /// </summary>
    /// <param name="partition">The partition to detach.</param>
    /// <param name="voxel">The target voxel from which the partition will be removed.</param>
    public static void RemoveFromVoxel(this IVoxelPartition partition, Voxel voxel)
    {
        partition.VoxelCoordinates = default;
        partition.IsPartitioned = false;
        partition.OnRemoveFromVoxel(voxel);
    }
}
