//=======================================================================
// PartitionProvider.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

/// <summary>
/// Provides efficient storage and retrieval of partitions keyed by their exact concrete <see cref="Type"/>.
/// </summary>
/// <remarks>
/// The first two concrete types are stored inline. Additional types use provider-owned overflow
/// storage that is cleared and retained after compaction so a later promotion can reuse it.
/// Enumeration and compaction preserve registration order.
/// </remarks>
public sealed class PartitionProvider<TPartitionBase> where TPartitionBase : class
{
    /// <summary>
    /// The first inline partition used by the common one- and two-partition paths.
    /// </summary>
    private Type? _firstPartitionType;

    /// <summary>
    /// The first inline partition used by the common one- and two-partition paths.
    /// </summary>
    private TPartitionBase? _firstPartition;

    /// <summary>
    /// The second inline partition used by the common two-partition-per-voxel path.
    /// </summary>
    private Type? _secondPartitionType;

    /// <summary>
    /// The second inline partition used by the common two-partition-per-voxel path.
    /// </summary>
    private TPartitionBase? _secondPartition;

    /// <summary>
    /// Overflow storage used only when a voxel hosts more than two concrete partition types.
    /// The dictionary remains owned by the provider after compaction so later promotions can reuse it.
    /// </summary>
    private SwiftList<OverflowPartition>? _overflowPartitions;

    /// <summary>
    /// Indicates whether the provider currently contains any partitions.
    /// Returns true if empty; otherwise, false.
    /// </summary>
    public bool IsEmpty => _firstPartition == null;

    /// <summary>
    /// Gets the current number of partitions stored in the provider.
    /// </summary>
    public int Count =>
        (_firstPartition != null ? 1 : 0)
        + (_secondPartition != null ? 1 : 0)
        + (_overflowPartitions?.Count ?? 0);

    /// <summary>
    /// Attempts to add a partition to the provider with the specified type key.
    /// Returns true if the partition was added; false if a partition with the same type already exists.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(Type partitionType, TPartitionBase partition)
    {
        // Both checks are side-effect free; evaluating both avoids an extra hot-path branch.
        if (partitionType == null | partition == null)
            return false;

        if (_firstPartition == null)
        {
            _firstPartitionType = partitionType;
            _firstPartition = partition;
            return true;
        }

        if (_firstPartitionType == partitionType)
            return false;

        if (_secondPartition == null)
        {
            _secondPartitionType = partitionType;
            _secondPartition = partition;
            return true;
        }

        if (_secondPartitionType == partitionType)
            return false;

        return TryAddOverflowPartition(partitionType!, partition!);
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

        if (_firstPartitionType == partitionType)
        {
            partition = _firstPartition;
            _firstPartitionType = _secondPartitionType;
            _firstPartition = _secondPartition;
            ClearSecondPartition();
            MoveFirstOverflowPartitionToSecondSlot();
            return true;
        }

        if (_secondPartitionType == partitionType)
        {
            partition = _secondPartition;
            ClearSecondPartition();
            MoveFirstOverflowPartitionToSecondSlot();
            return true;
        }

        return TryRemoveOverflowPartition(partitionType, out partition);
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

        if (_firstPartitionType == partitionType)
        {
            partition = _firstPartition;
            return true;
        }

        if (_secondPartitionType == partitionType)
        {
            partition = _secondPartition;
            return true;
        }

        return TryGetOverflowPartition(partitionType, out partition);
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
        _firstPartitionType = null;
        _firstPartition = null;
        ClearSecondPartition();
        _overflowPartitions?.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearSecondPartition()
    {
        _secondPartitionType = null;
        _secondPartition = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAddOverflowPartition(Type partitionType, TPartitionBase partition)
    {
        if (FindOverflowPartitionIndex(partitionType) >= 0)
            return false;

        _overflowPartitions ??= new SwiftList<OverflowPartition>(4);
        _overflowPartitions.Add(new OverflowPartition(partitionType, partition));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRemoveOverflowPartition(Type partitionType, out TPartitionBase? partition)
    {
        int index = FindOverflowPartitionIndex(partitionType);
        if (index < 0)
        {
            partition = null;
            return false;
        }

        partition = _overflowPartitions!.InnerArray[index].Partition;
        _overflowPartitions.RemoveAt(index);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetOverflowPartition(Type partitionType, out TPartitionBase? partition)
    {
        int index = FindOverflowPartitionIndex(partitionType);
        if (index < 0)
        {
            partition = null;
            return false;
        }

        partition = _overflowPartitions!.InnerArray[index].Partition;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindOverflowPartitionIndex(Type partitionType)
    {
        if (_overflowPartitions == null)
            return -1;

        OverflowPartition[] partitions = _overflowPartitions.InnerArray;
        for (int i = 0; i < _overflowPartitions.Count; i++)
        {
            if (partitions[i].Type == partitionType)
                return i;
        }

        return -1;
    }

    private void MoveFirstOverflowPartitionToSecondSlot()
    {
        if (_overflowPartitions == null || _overflowPartitions.Count == 0)
            return;

        OverflowPartition overflowPartition = _overflowPartitions[0];
        _overflowPartitions.RemoveAt(0);
        _secondPartitionType = overflowPartition.Type;
        _secondPartition = overflowPartition.Partition;
    }

    /// <summary>
    /// Returns an allocation-free enumerator for the provider's current partitions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Enumerator GetEnumerator() => new(this);

    internal struct Enumerator
    {
        private readonly TPartitionBase? _firstPartition;
        private readonly TPartitionBase? _secondPartition;
        private readonly bool _hasOverflow;
        private SwiftList<OverflowPartition>.SwiftListEnumerator _overflowEnumerator;
        private int _inlineState;

        internal Enumerator(PartitionProvider<TPartitionBase> provider)
        {
            _firstPartition = provider._firstPartition;
            _secondPartition = provider._secondPartition;
            _overflowEnumerator = provider._overflowPartitions != null
                ? provider._overflowPartitions.GetEnumerator()
                : default;
            _hasOverflow = provider._overflowPartitions != null
                && provider._overflowPartitions.Count > 0;
            _inlineState = _firstPartition != null ? 0 : 2;
            Current = default!;
        }

        public TPartitionBase Current { get; private set; }

        public bool MoveNext()
        {
            if (_inlineState == 0)
            {
                Current = _firstPartition!;
                _inlineState = 1;
                return true;
            }

            if (_inlineState == 1)
            {
                _inlineState = 2;
                if (_secondPartition != null)
                {
                    Current = _secondPartition;
                    return true;
                }
            }

            if (!_hasOverflow || !_overflowEnumerator.MoveNext())
                return false;

            Current = _overflowEnumerator.Current.Partition;
            return true;
        }
    }

    private readonly struct OverflowPartition
    {
        internal OverflowPartition(Type type, TPartitionBase partition)
        {
            Type = type;
            Partition = partition;
        }

        internal Type Type { get; }

        internal TPartitionBase Partition { get; }
    }
}
