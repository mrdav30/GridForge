using FixedMathSharp;
using GridForge.Spatial;

namespace GridForge.Grids.Tests
{

    public class TestPartition : IVoxelPartition
    {
        public GlobalVoxelIndex GlobalIndex { get; set; }

        public bool IsPartitioned { get; set; }

        public void OnAddToVoxel(Voxel voxel) { }
        public void OnRemoveFromVoxel(Voxel voxel) { }
    }

    public class TestOccupant : IVoxelOccupant
    {
        public byte OccupantGroupId { get; set; }

        public bool IsVoxelOccupant { get; set; }

        public int OccupantTicket { get; set; }

        public GlobalVoxelIndex GlobalIndex { get; set; }
        public Vector3d WorldPosition { get ; set; }

        public TestOccupant(Vector3d position, byte clusterKey = byte.MaxValue)
        {
            WorldPosition = position;
            OccupantGroupId = clusterKey;
        }
    }
}
