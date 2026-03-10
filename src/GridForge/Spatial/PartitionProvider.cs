using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

/// <summary>
/// Provides efficient storage and retrieval of partitions keyed by their exact concrete <see cref="Type"/>.
/// </summary>
public sealed class PartitionProvider<TPartitionBase> where TPartitionBase : class
{
    /// <summary>
    /// Backing dictionary that stores partition instances keyed by their exact concrete type.
    /// </summary>
    private SwiftDictionary<Type, TPartitionBase> _partitions;

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
    /// Attempts to add a partition to the provider with the specified type key.
    /// Returns true if the partition was added; false if a partition with the same type already exists.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(Type partitionType, TPartitionBase partition)
    {
        if (partitionType == null || partition == null)
            return false;

        _partitions ??= new SwiftDictionary<Type, TPartitionBase>();
        return _partitions.Add(partitionType, partition);
    }

    /// <summary>
    /// Attempts to remove a partition associated with the specified type.
    /// If successful, the removed partition is returned in the out parameter.
    /// Returns true if the partition was removed; otherwise, false.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRemove(Type partitionType, out TPartitionBase partition)
    {
        partition = default;

        if (partitionType == null || _partitions == null)
            return false;

        if (!_partitions.TryGetValue(partitionType, out partition))
            return false;

        _partitions.Remove(partitionType);

        if (_partitions.Count == 0)
            _partitions = null; // Auto-clear empty

        return true;
    }

    /// <summary>
    /// Attempts to retrieve a partition associated with the specified concrete type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Type partitionType, out TPartitionBase partition)
    {
        partition = default;

        if (partitionType == null || _partitions == null)
            return false;

        return _partitions.TryGetValue(partitionType, out partition);
    }

    /// <summary>
    /// Attempts to retrieve a partition of the specified type.
    /// Returns true and sets the out parameter if the partition exists and is of the requested type; otherwise, returns false.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet<T>(out T partition) where T : TPartitionBase
    {
        partition = default;

        if (!TryGet(typeof(T), out TPartitionBase tempPartition) || tempPartition is not T typedPartition)
            return false;

        partition = typedPartition;
        return true;
    }

    /// <summary>
    /// Determines whether the provider contains a partition associated with the specified concrete type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(Type partitionType)
    {
        return TryGet(partitionType, out _);
    }

    /// <summary>
    /// Determines whether the provider contains a partition of the specified type.
    /// Returns true if such a partition exists; otherwise, false.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>() where T : TPartitionBase
    {
        return TryGet<T>(out _);
    }

    /// <summary>
    /// Removes all partitions from the provider, clearing its internal storage.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _partitions?.Clear();
        _partitions = null;
    }
}
