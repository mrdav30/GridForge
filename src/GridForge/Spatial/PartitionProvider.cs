//=======================================================================
// PartitionProvider.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using SwiftCollections;

namespace GridForge.Spatial;

/// <summary>
/// Provides efficient storage and retrieval of partitions keyed by their exact concrete <see cref="Type"/>.
/// </summary>
/// <remarks>
/// The first two concrete types are stored inline. Additional types use provider-owned overflow
/// storage that is cleared and retained after compaction so a later promotion can reuse it.
/// Enumeration and compaction preserve registration order.
/// </remarks>
/// <typeparam name="TPartitionBase">The reference-type base accepted for retained partitions.</typeparam>
public sealed partial class PartitionProvider<TPartitionBase> where TPartitionBase : class
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

    /// <summary>Indicates whether no partitions are stored.</summary>
    /// <value>True when empty; otherwise false.</value>
    public bool IsEmpty => _firstPartition == null;

    /// <summary>
    /// Gets the current number of partitions stored in the provider.
    /// </summary>
    /// <value>Gets the current number of partitions stored in the provider.</value>
    public int Count =>
        (_firstPartition != null ? 1 : 0)
        + (_secondPartition != null ? 1 : 0)
        + (_overflowPartitions?.Count ?? 0);

    /// <summary>
    /// Attempts to add a partition to the provider with the specified type key.
    /// Returns true if the partition was added; false if a partition with the same type already exists.
    /// </summary>
    /// <param name="partitionType">The exact type key to query. Lookup does not search base classes or interfaces.</param>
    /// <param name="partition">The partition instance to retain under its exact type key.</param>
    /// <returns>True when the exact key was absent and the reference was retained; false for null arguments or an existing key.</returns>
    /// <remarks>The supplied Type is an explicit key. The provider does not check that it equals, or is assignable from, the object runtime type. Use a matching concrete type key when you need generic lookup to find the value.</remarks>
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
    /// <param name="partitionType">The exact type key to query. Lookup does not search base classes or interfaces.</param>
    /// <param name="partition">The resolved partition on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the exact key was removed and the detached reference is returned; otherwise false with a null output.</returns>
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
    /// <param name="partitionType">The exact type key to query. Lookup does not search base classes or interfaces.</param>
    /// <param name="partition">The resolved partition on success; default on failure. Use it only when the operation succeeds.</param>
    /// <returns>True when the exact supplied key is stored, returning its retained base-type reference; otherwise false and the output is null. No agreement between the key and runtime object type is checked.</returns>
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
    /// <param name="partition">The resolved partition on success; default on failure. Use it only when the operation succeeds.</param>
    /// <typeparam name="T">The exact partition type to query or remove.</typeparam>
    /// <returns>True when the exact key resolves and the output has the requested type; otherwise false with a default output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet<T>(out T? partition) where T : TPartitionBase
    {
        partition = default;

        if (!TryGet(typeof(T), out TPartitionBase? tempPartition) || tempPartition is not T typedPartition)
            return false;

        partition = typedPartition;
        return true;
    }

    internal bool TryGetWithinInspectionLimit(
        Type partitionType, Type? firstExcludedType, Type? secondExcludedType,
        ulong limit, out ulong work, out TPartitionBase? partition)
    {
        work = limit;
        partition = null;
        ulong attached = (ulong)Count;
        ulong maximumExclusions = firstExcludedType == null ? 0UL : 2UL;
        if (limit == 0 || (attached > maximumExclusions && attached - maximumExclusions > limit))
            return false;

        if (firstExcludedType == null)
        {
            if (TryGet(partitionType, out partition))
            {
                work = attached;
                return true;
            }

            if (attached == limit)
                return false;

            work = attached + 1;
            return true;
        }

        if (attached == 0 || (attached == 1 && _firstPartitionType == partitionType))
        {
            work = 1;
            partition = _firstPartition;
            return true;
        }

        // Reserve the requested type independently of whether it is attached.
        ulong inspected = 1;
        TPartitionBase? found = null;
        if (!Inspect(_firstPartitionType, _firstPartition) || !Inspect(_secondPartitionType, _secondPartition))
            return false;

        if (_overflowPartitions != null)
        {
            OverflowPartition[] overflow = _overflowPartitions.InnerArray;
            for (int i = 0; i < _overflowPartitions.Count; i++)
            {
                if (!Inspect(overflow[i].Type, overflow[i].Partition))
                    return false;
            }
        }

        work = inspected;
        partition = found;
        return true;

        bool Inspect(Type? type, TPartitionBase? value)
        {
            if (type == partitionType)
                found = value;
            else if (type != null && type != firstExcludedType && type != secondExcludedType)
            {
                if (inspected == limit)
                    return false;

                inspected++;
            }

            return true;
        }
    }

    /// <summary>
    /// Determines whether the provider contains a partition associated with the specified concrete type.
    /// </summary>
    /// <param name="partitionType">The exact type key to query. Lookup does not search base classes or interfaces.</param>
    /// <returns>True when the exact type key is stored; otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(Type partitionType)
    {
        return TryGet(partitionType, out _);
    }

    /// <summary>
    /// Determines whether the provider contains a partition of the specified type.
    /// Returns true if such a partition exists; otherwise, false.
    /// </summary>
    /// <typeparam name="T">The exact partition type to query or remove.</typeparam>
    /// <returns>True when the exact type key is stored; otherwise false.</returns>
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
