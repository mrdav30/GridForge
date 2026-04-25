using GridForge.Grids;

namespace GridForge.Spatial;

/// <summary>
/// Represents a partition within a <see cref="Voxel"/> that modifies its behavior.
/// Partitions can define special attributes or metadata for a voxel, such as terrain type or navigation properties.
/// </summary>
public interface IVoxelPartition
{
    /// <summary>
    /// The world-scoped identity of the parent voxel where this partition is attached.
    /// </summary>
    WorldVoxelIndex WorldIndex { get; }

    /// <summary>
    /// Called when adding this partition to a <see cref="Voxel"/>
    /// </summary>
    /// <param name="parentVoxelIndex">The world-scoped identity of the parent <see cref="Voxel"/>.</param>
    void SetParentIndex(WorldVoxelIndex parentVoxelIndex);

    /// <summary>
    /// Called when adding this partition to a <see cref="Voxel"/>.
    /// </summary>
    void OnAddToVoxel(Voxel voxel);

    /// <summary>
    /// Called when this partition is removed from a <see cref="Voxel"/>.
    /// Cleans up any associated data.
    /// </summary>
    void OnRemoveFromVoxel(Voxel voxel);
}
