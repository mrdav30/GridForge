using GridForge.Grids;

namespace GridForge.Spatial
{
    /// <summary>
    /// Represents a partition within a <see cref="Node"/> that modifies its behavior.
    /// Partitions can define special attributes or metadata for a node, such as terrain type or navigation properties.
    /// </summary>
    public interface INodePartition
    {
        /// <summary>
        /// The global coordinates of the parent node where this partition is attached.
        /// </summary>
        CoordinatesGlobal ParentCoordinate { get; }

        /// <summary>
        /// Determines if the partition is currently allocated.
        /// </summary>
        bool IsAllocated { get; }

        /// <summary>
        /// Called when adding this partition to a <see cref="Node"/>.
        /// </summary>
        /// <param name="coordinate">The global coordinates of the node this partition is assigned to.</param>
        void Setup(CoordinatesGlobal coordinate);

        /// <summary>
        /// Retrieves the partition's unique identifier key.
        /// This key is used to distinguish different partition types within a node.
        /// </summary>
        int GetPartitionKey();

        /// <summary>
        /// Called when this partition is removed from a <see cref="Node"/>.
        /// Cleans up any associated data.
        /// </summary>
        void Reset();
    }
}
