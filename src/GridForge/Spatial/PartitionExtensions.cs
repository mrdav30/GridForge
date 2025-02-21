using GridForge.Grids;


using GridForge.Spatial;


/// <summary>
/// Provides extension methods for managing <see cref="INodePartition"/> instances on <see cref="Node"/> objects.
/// This allows adding and removing partitions while maintaining their state.
/// </summary>
public static class PartitionExtensions
{
    /// <summary>
    /// Attaches a partition to a specified <see cref="Node"/>, updating its state and invoking initialization logic.
    /// </summary>
    /// <param name="partition">The partition to attach.</param>
    /// <param name="node">The target node where the partition will be added.</param>
    public static void AddToNode(this INodePartition partition, Node node)
    {
        partition.ParentCoordinate = node.GlobalCoordinates;
        partition.IsPartitioned = true;
        partition.OnAddToNode(node);
    }

    /// <summary>
    /// Detaches a partition from a specified <see cref="Node"/>, resetting its state and invoking cleanup logic.
    /// </summary>
    /// <param name="partition">The partition to detach.</param>
    /// <param name="node">The target node from which the partition will be removed.</param>
    public static void RemoveFromNode(this INodePartition partition, Node node)
    {
        partition.ParentCoordinate = default;
        partition.IsPartitioned = false;
        partition.OnRemoveFromNode(node);
    }
}
