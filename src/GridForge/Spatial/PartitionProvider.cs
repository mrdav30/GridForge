//=======================================================================
// PartitionProvider.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

/// <summary>
/// Provides efficient storage and retrieval of partitions keyed by their exact concrete <see cref="Type"/>.
/// </summary>
public sealed class PartitionProvider<TPartitionBase> where TPartitionBase : class
{
    /// <summary>
    /// The single inline partition used by the common one-partition-per-voxel path.
    /// </summary>
    private Type? _singlePartitionType;

    /// <summary>
    /// The single inline partition used by the common one-partition-per-voxel path.
    /// </summary>
    private TPartitionBase? _singlePartition;

    /// <summary>
    /// Backing dictionary used only when a voxel hosts multiple concrete partition types.
    /// </summary>
    private SwiftDictionary<Type, TPartitionBase>? _partitions;

    /// <summary>
    /// Returns an enumerable of all partitions currently stored in the provider.
    /// </summary>
    internal IEnumerable<TPartitionBase> Partitions
    {
        get
        {
            if (_singlePartition != null)
                return new SinglePartitionEnumerable(_singlePartition);

            return _partitions != null && _partitions.Count > 0
                ? _partitions.Values
                : Array.Empty<TPartitionBase>();
        }
    }

    /// <summary>
    /// Indicates whether the provider currently contains any partitions.
    /// Returns true if empty; otherwise, false.
    /// </summary>
    public bool IsEmpty => _singlePartition == null && (_partitions == null || _partitions.Count == 0);

    /// <summary>
    /// Gets the current number of partitions stored in the provider.
    /// </summary>
    public int Count => _singlePartition != null ? 1 : _partitions?.Count ?? 0;

    /// <summary>
    /// Attempts to add a partition to the provider with the specified type key.
    /// Returns true if the partition was added; false if a partition with the same type already exists.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(Type partitionType, TPartitionBase partition)
    {
        if (partitionType == null || partition == null)
            return false;

        if (_partitions != null)
            return _partitions.Add(partitionType, partition);

        if (_singlePartition != null)
        {
            if (_singlePartitionType == partitionType)
                return false;

            _partitions = new SwiftDictionary<Type, TPartitionBase>(2)
            {
                { _singlePartitionType!, _singlePartition }
            };
            bool added = _partitions.Add(partitionType, partition);
            _singlePartitionType = null;
            _singlePartition = null;
            return added;
        }

        _singlePartitionType = partitionType;
        _singlePartition = partition;
        return true;
    }

    /// <summary>
    /// Attempts to remove a partition associated with the specified type.
    /// If successful, the removed partition is returned in the out parameter.
    /// Returns true if the partition was removed; otherwise, false.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRemove(Type partitionType, out TPartitionBase? partition)
    {
        partition = null;

        if (partitionType == null)
            return false;

        if (_singlePartition != null)
        {
            if (_singlePartitionType != partitionType)
                return false;

            partition = _singlePartition;
            _singlePartitionType = null;
            _singlePartition = null;
            return true;
        }

        if (_partitions == null)
            return false;

        if (!_partitions.TryGetValue(partitionType, out partition))
            return false;

        _partitions.Remove(partitionType);

        return true;
    }

    /// <summary>
    /// Attempts to retrieve a partition associated with the specified concrete type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Type partitionType, out TPartitionBase? partition)
    {
        partition = null;

        if (partitionType == null)
            return false;

        if (_singlePartition != null)
        {
            if (_singlePartitionType != partitionType)
                return false;

            partition = _singlePartition;
            return true;
        }

        if (_partitions == null)
            return false;

        return _partitions.TryGetValue(partitionType, out partition);
    }

    /// <summary>
    /// Attempts to retrieve a partition of the specified type.
    /// Returns true and sets the out parameter if the partition exists and is of the requested type; otherwise, returns false.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet<T>(out T? partition) where T : TPartitionBase
    {
        partition = default;

        if (!TryGet(typeof(T), out TPartitionBase? tempPartition) || tempPartition is not T typedPartition)
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
        _singlePartitionType = null;
        _singlePartition = null;
        _partitions?.Clear();
    }

    /// <summary>
    /// Returns an allocation-free enumerator for the provider's current partitions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Enumerator GetEnumerator() => new(this);

    internal struct Enumerator
    {
        private readonly TPartitionBase? _singlePartition;
        private readonly bool _hasDictionary;
        private SwiftDictionary<Type, TPartitionBase>.SwiftDictionaryEnumerator _dictionaryEnumerator;
        private int _singleState;

        internal Enumerator(PartitionProvider<TPartitionBase> provider)
        {
            _singlePartition = provider._singlePartition;
            _dictionaryEnumerator = provider._partitions != null
                ? provider._partitions.GetEnumerator()
                : default;
            _hasDictionary = provider._partitions != null;
            _singleState = _singlePartition != null ? 0 : 1;
            Current = default!;
        }

        public TPartitionBase Current { get; private set; }

        public bool MoveNext()
        {
            if (_singleState == 0)
            {
                Current = _singlePartition!;
                _singleState = 1;
                return true;
            }

            if (!_hasDictionary)
                return false;

            if (!_dictionaryEnumerator.MoveNext())
                return false;

            Current = _dictionaryEnumerator.Current.Value;
            return true;
        }
    }

    private sealed class SinglePartitionEnumerable : IEnumerable<TPartitionBase>
    {
        private readonly TPartitionBase _partition;

        public SinglePartitionEnumerable(TPartitionBase partition)
        {
            _partition = partition;
        }

        public IEnumerator<TPartitionBase> GetEnumerator()
        {
            yield return _partition;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
