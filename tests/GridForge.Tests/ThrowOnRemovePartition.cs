using System;
using GridForge.Spatial;

namespace GridForge.Grids.Tests;

public sealed class ThrowOnRemovePartition : IVoxelPartition
{
    public WorldVoxelIndex WorldIndex { get; private set; }

    public void SetParentIndex(WorldVoxelIndex worldIndex)
    {
        WorldIndex = worldIndex;
    }

    public void OnAddToVoxel(Voxel voxel) { }

    public void OnRemoveFromVoxel(Voxel voxel)
    {
        throw new InvalidOperationException("remove failure");
    }
}
