using FixedMathSharp;
using GridForge.Spatial;
using System;

namespace GridForge.Grids.Tests;

public class TestPartition : IVoxelPartition
{
    public GlobalVoxelIndex GlobalIndex { get; private set; }

    public void SetParentIndex(GlobalVoxelIndex globalIndex)
    {
        GlobalIndex = globalIndex;
    }

    public void OnAddToVoxel(Voxel voxel) { }

    public void OnRemoveFromVoxel(Voxel voxel) { }
}

public sealed class ThrowOnAddPartition : IVoxelPartition
{
    public GlobalVoxelIndex GlobalIndex { get; private set; }

    public void SetParentIndex(GlobalVoxelIndex globalIndex)
    {
        GlobalIndex = globalIndex;
    }

    public void OnAddToVoxel(Voxel voxel)
    {
        throw new InvalidOperationException("add failure");
    }

    public void OnRemoveFromVoxel(Voxel voxel) { }
}

public sealed class ThrowOnRemovePartition : IVoxelPartition
{
    public GlobalVoxelIndex GlobalIndex { get; private set; }

    public void SetParentIndex(GlobalVoxelIndex globalIndex)
    {
        GlobalIndex = globalIndex;
    }

    public void OnAddToVoxel(Voxel voxel) { }

    public void OnRemoveFromVoxel(Voxel voxel)
    {
        throw new InvalidOperationException("remove failure");
    }
}

public static class PartitionFamilyA
{
    public sealed class SharedPartition : IVoxelPartition
    {
        public GlobalVoxelIndex GlobalIndex { get; private set; }

        public void SetParentIndex(GlobalVoxelIndex globalIndex)
        {
            GlobalIndex = globalIndex;
        }

        public void OnAddToVoxel(Voxel voxel) { }

        public void OnRemoveFromVoxel(Voxel voxel) { }
    }
}

public static class PartitionFamilyB
{
    public sealed class SharedPartition : IVoxelPartition
    {
        public GlobalVoxelIndex GlobalIndex { get; private set; }

        public void SetParentIndex(GlobalVoxelIndex globalIndex)
        {
            GlobalIndex = globalIndex;
        }

        public void OnAddToVoxel(Voxel voxel) { }

        public void OnRemoveFromVoxel(Voxel voxel) { }
    }
}

public class TestOccupant : IVoxelOccupant
{
    public Guid GlobalId { get; private set; } = Guid.NewGuid();

    public byte OccupantGroupId { get; private set; }

    public Vector3d Position { get; set; }

    public TestOccupant(Vector3d position, byte clusterKey = byte.MaxValue)
    {
        Position = position;
        OccupantGroupId = clusterKey;
    }
}
