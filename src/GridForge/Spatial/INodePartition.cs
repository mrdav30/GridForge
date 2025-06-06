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
        CoordinatesGlobal NodeCoordinates { get; set; }

        /// <summary>
        /// Flag to determine if this partition is currently attached to a <see cref="Node"/>
        /// </summary>
        bool IsPartitioned { get; set; }

        /// <summary>
        /// Called when adding this partition to a <see cref="Node"/>.
        /// </summary>
        void OnAddToNode(Node node);

        /// <summary>
        /// Called when this partition is removed from a <see cref="Node"/>.
        /// Cleans up any associated data.
        /// </summary>
        void OnRemoveFromNode(Node node);
    }
}
