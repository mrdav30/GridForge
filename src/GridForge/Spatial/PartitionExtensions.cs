using GridForge.Grids;

namespace GridForge.Spatial
{
    public static class PartitionExtensions
    {
        public static void AddToNode(this INodePartition partition, Node node)
        {
            partition.ParentCoordinate = node.GlobalCoordinates;
            partition.IsPartitioned = true;
            partition.OnAddToNode(node);
        }

        public static void RemoveFromNode(this INodePartition partition, Node node)
        {
            partition.ParentCoordinate = default;
            partition.IsPartitioned = false;
            partition.OnRemoveFromNode(node);
        }
    }
}
