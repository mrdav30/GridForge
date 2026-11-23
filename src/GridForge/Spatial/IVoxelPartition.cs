//=======================================================================
// IVoxelPartition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
    /// <value>The world-scoped identity of the parent voxel where this partition is attached.</value>
    WorldVoxelIndex WorldIndex { get; }

    /// <summary>
    /// Called when adding this partition to a <see cref="Voxel"/>
    /// </summary>
    /// <param name="parentVoxelIndex">The world-scoped identity of the parent <see cref="Voxel"/>.</param>
    /// <remarks>Called after storing a newly attached partition and before OnAddToVoxel. If the callback throws, attachment fails and its stored entry is removed; caller-owned object side effects are not undone.</remarks>
    void SetParentIndex(WorldVoxelIndex parentVoxelIndex);

    /// <summary>
    /// Called when adding this partition to a <see cref="Voxel"/>.
    /// </summary>
    /// <param name="voxel">The physical voxel to inspect. The reference is borrowed and must not cross grid removal or pooled reuse.</param>
    /// <remarks>Called after SetParentIndex on successful storage. A thrown exception is logged and removes the stored partition entry, without undoing changes already made by the callback.</remarks>
    void OnAddToVoxel(Voxel voxel);

    /// <summary>
    /// Called when this partition is removed from a <see cref="Voxel"/>.
    /// Cleans up any associated data.
    /// </summary>
    /// <param name="voxel">The physical voxel to inspect. The reference is borrowed and must not cross grid removal or pooled reuse.</param>
    /// <remarks>Called after detachment. Exceptions are logged and do not undo removal. Clear any retained parent state yourself; the library does not call SetParentIndex with a default identity on removal.</remarks>
    void OnRemoveFromVoxel(Voxel voxel);
}
