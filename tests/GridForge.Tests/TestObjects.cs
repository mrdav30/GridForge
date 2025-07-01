using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using System;

namespace GridForge.Grids.Tests
{

    public class TestPartition : IVoxelPartition
    {
        public GlobalVoxelIndex GlobalIndex { get; }

        public void SetParentIndex(GlobalVoxelIndex globalIndex) { }

        public void OnAddToVoxel(Voxel voxel) { }

        public void OnRemoveFromVoxel(Voxel voxel) { }
    }

    public class TestOccupant : IVoxelOccupant
    {
        public Guid GlobalId { get; private set; } = Guid.NewGuid();

        public byte OccupantGroupId { get; private set; }

        public SwiftDictionary<GlobalVoxelIndex, int> OccupyingIndexMap { get; private set; } = new();

        public Vector3d Position { get ; set; }

        public TestOccupant(Vector3d position, byte clusterKey = byte.MaxValue)
        {
            Position = position;
            OccupantGroupId = clusterKey;
        }

        public void SetOccupancy(GlobalVoxelIndex index, int ticket)
        {
            OccupyingIndexMap[index] = ticket;
        }

        public void RemoveOccupancy(GlobalVoxelIndex index)
        {
            OccupyingIndexMap.Remove(index);
        }
    }
}
