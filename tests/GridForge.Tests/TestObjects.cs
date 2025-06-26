using FixedMathSharp;
using GridForge.Spatial;

namespace GridForge.Grids.Tests
{

    public class TestPartition : IVoxelPartition
    {
        public GlobalVoxelIndex GlobalIndex { get; set; }

        public void OnAddToVoxel(Voxel voxel) { }

        public void OnRemoveFromVoxel(Voxel voxel) { }
    }

    public class TestOccupant : IVoxelOccupant
    {
        public byte OccupantGroupId { get; set; }

        public int OccupantTicket { get; set; }

        public GlobalVoxelIndex OccupyingIndex { get; set; }

        public Vector3d Position { get ; set; }

        public TestOccupant(Vector3d position, byte clusterKey = byte.MaxValue)
        {
            Position = position;
            OccupantGroupId = clusterKey;
        }
    }
}
