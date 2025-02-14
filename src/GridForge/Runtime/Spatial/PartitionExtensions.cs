namespace GridForge.Spatial
{
    public static class PartitionExtensions
    {
        public static void AddToNode(this INodePartition partition, CoordinatesGlobal parentCoordinates)
        {
            partition.ParentCoordinate = parentCoordinates;
            partition.IsPartitioned = true;
            partition.OnAddToNode();
        }

        public static void RemoveFromNode(this INodePartition partition)
        {
            partition.ParentCoordinate = default;
            partition.IsPartitioned = false;
            partition.OnRemoveFromNode();
        }
    }
}
