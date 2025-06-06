using SwiftCollections;
using System.Collections.Generic;
using System.Linq;

namespace GridForge.Spatial
{
    /// <summary>
    /// Provides efficient storage and retrieval of partitions keyed by integer values, supporting type-safe queries and management of partitioned data.
    /// </summary>
    public sealed class PartitionProvider<TPartitionBase> where TPartitionBase : class
    {
        /// <summary>
        /// Backing dictionary that stores partition instances keyed by integer identifiers.
        /// </summary>
        private SwiftDictionary<int, TPartitionBase> _partitions;

        /// <summary>
        /// Returns an enumerable of all partitions currently stored in the provider.
        /// </summary>
        internal IEnumerable<TPartitionBase> Partitions => _partitions?.Values ?? Enumerable.Empty<TPartitionBase>();

        /// <summary>
        /// Indicates whether the provider currently contains any partitions.
        /// Returns true if empty; otherwise, false.
        /// </summary>
        public bool IsEmpty => _partitions == null || _partitions.Count == 0;

        /// <summary>
        /// Gets the current number of partitions stored in the provider.
        /// </summary>
        public int Count => _partitions?.Count ?? 0;

        /// <summary>
        /// Attempts to add a partition to the provider with the specified key.
        /// Returns true if the partition was added; false if a partition with the same key already exists.
        /// </summary>
        public bool TryAdd(int key, TPartitionBase partition)
        {
            _partitions ??= new SwiftDictionary<int, TPartitionBase>();
            return _partitions.Add(key, partition);
        }

        /// <summary>
        /// Attempts to remove a partition associated with the specified key.
        /// If successful, the removed partition is returned in the out parameter.
        /// Returns true if the partition was removed; otherwise, false.
        /// </summary>
        public bool TryRemove(int key, out TPartitionBase partition)
        {
            partition = default;

            if (_partitions == null)
                return false;

            if (!_partitions.TryGetValue(key, out partition))
                return false;

            _partitions.Remove(key);

            if (_partitions.Count == 0)
                _partitions = null; // Auto-clear empty

            return true;
        }

        /// <summary>
        /// Attempts to retrieve a partition of the specified type associated with the given key.
        /// Returns true and sets the out parameter if the partition exists and is of the requested type; otherwise, returns false.
        /// </summary>
        public bool TryGet<T>(int key, out T partition) where T : TPartitionBase
        {
            partition = default;

            if (_partitions == null)
                return false;

            if (_partitions.TryGetValue(key, out TPartitionBase tempPartition) && tempPartition is T typedPartition)
            {
                partition = typedPartition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the provider contains a partition of the specified type associated with the given key.
        /// Returns true if such a partition exists; otherwise, false.
        /// </summary>
        public bool Has<T>(int key) where T : TPartitionBase
        {
            return TryGet<T>(key, out _);
        }

        /// <summary>
        /// Removes all partitions from the provider, clearing its internal storage.
        /// </summary>
        public void Clear()
        {
            _partitions?.Clear();
            _partitions = null;
        }
    }
}
