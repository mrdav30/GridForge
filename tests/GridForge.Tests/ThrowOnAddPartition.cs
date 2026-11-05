using System;
using GridForge.Spatial;

namespace GridForge.Grids.Tests;

public sealed class ThrowOnAddPartition : IVoxelPartition
{
    public WorldVoxelIndex WorldIndex { get; private set; }

    public void SetParentIndex(WorldVoxelIndex worldIndex)
    {
        WorldIndex = worldIndex;
    }

    public void OnAddToVoxel(Voxel voxel)
    {
        throw new InvalidOperationException("add failure");
    }

    public void OnRemoveFromVoxel(Voxel voxel) { }
}
