using FixedMathSharp;
using GridForge.Spatial;

namespace GridForge.Grids.Tests
{

    public class TestPartition : INodePartition
    {
        public CoordinatesGlobal ParentCoordinate { get; set; }

        public bool IsPartitioned { get; set; }

        public void OnAddToNode() { }
        public void OnRemoveFromNode() { }
    }

    public class TestOccupant : INodeOccupant
    {
        public byte OccupantGroupId { get; set; }

        public bool IsNodeOccupant { get; set; }

        public int OccupantTicket { get; set; }

        public CoordinatesGlobal GridCoordinates { get; set; }
        public Vector3d WorldPosition { get ; set; }

        public TestOccupant(Vector3d position, byte clusterKey = byte.MaxValue)
        {
            WorldPosition = position;
            OccupantGroupId = clusterKey;
        }
    }
}
