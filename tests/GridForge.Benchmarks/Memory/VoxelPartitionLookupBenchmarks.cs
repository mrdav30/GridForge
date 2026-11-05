using System;
using BenchmarkDotNet.Attributes;
using GridForge.Grids;
using GridForge.Spatial;

namespace GridForge.Benchmarks;

/// <summary>
/// Separates voxel synchronization from generic and type-key provider lookup.
/// </summary>
[MemoryDiagnoser]
public class VoxelPartitionLookupBenchmarks
{
    private const int LookupCount = 256;
    private Voxel[] _voxels;
    private PartitionProvider<IVoxelPartition>[] _providers;
    private FirstEntry[] _first;
    private SecondEntry[] _second;
    private ThirdEntry[] _third;

    [Params(1, LookupCount)]
    public int VoxelCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _voxels = new Voxel[VoxelCount];
        _providers = new PartitionProvider<IVoxelPartition>[VoxelCount];
        _first = new FirstEntry[VoxelCount];
        _second = new SecondEntry[VoxelCount];
        _third = new ThirdEntry[VoxelCount];
        for (int i = 0; i < VoxelCount; i++)
        {
            _voxels[i] = new Voxel();
            _providers[i] = new PartitionProvider<IVoxelPartition>();
            _first[i] = new FirstEntry();
            _second[i] = new SecondEntry();
            _third[i] = new ThirdEntry();
            Add(i, _first[i]);
            Add(i, _second[i]);
            Add(i, _third[i]);
        }

        Verify(VoxelFirstSlot(), nameof(VoxelFirstSlot));
        Verify(ProviderFirstSlot(), nameof(ProviderFirstSlot));
        Verify(ProviderTypeKeyFirstSlot(), nameof(ProviderTypeKeyFirstSlot));
        Verify(VoxelSecondSlot(), nameof(VoxelSecondSlot));
        Verify(ProviderSecondSlot(), nameof(ProviderSecondSlot));
        Verify(ProviderTypeKeySecondSlot(), nameof(ProviderTypeKeySecondSlot));
        Verify(VoxelOverflow(), nameof(VoxelOverflow));
        Verify(ProviderOverflow(), nameof(ProviderOverflow));
        Verify(ProviderTypeKeyOverflow(), nameof(ProviderTypeKeyOverflow));
        Verify(VoxelMiss(), nameof(VoxelMiss));
        Verify(ProviderMiss(), nameof(ProviderMiss));
        Verify(ProviderTypeKeyMiss(), nameof(ProviderTypeKeyMiss));
        Verify(VoxelHasFirst(), nameof(VoxelHasFirst));
        Verify(ProviderHasFirst(), nameof(ProviderHasFirst));
        Verify(VoxelDefaultFirst(), nameof(VoxelDefaultFirst));
    }

    private static void Verify(int checksum, string name)
    {
        if (checksum != LookupCount)
            throw new InvalidOperationException($"{name} returned {checksum}; expected {LookupCount}.");
    }

    private void Add(int index, IVoxelPartition partition)
    {
        // Both paths contain the very same payload references, in the same order.
        if (!_voxels[index].TryAddPartition(partition)
            || !_providers[index].TryAdd(partition.GetType(), partition))
            throw new InvalidOperationException("Unable to populate partition lookup fixture.");
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int VoxelFirstSlot() => LookupVoxels(_first);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderFirstSlot() => LookupProviders(_first);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderTypeKeyFirstSlot() => LookupProvidersByType(typeof(FirstEntry), _first);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int VoxelSecondSlot() => LookupVoxels(_second);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderSecondSlot() => LookupProviders(_second);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderTypeKeySecondSlot() => LookupProvidersByType(typeof(SecondEntry), _second);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int VoxelOverflow() => LookupVoxels(_third);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderOverflow() => LookupProviders(_third);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderTypeKeyOverflow() => LookupProvidersByType(typeof(ThirdEntry), _third);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int VoxelMiss() => LookupVoxels<MissingEntry>(null);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderMiss() => LookupProviders<MissingEntry>(null);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderTypeKeyMiss() => LookupProvidersByType(typeof(MissingEntry), null);

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int VoxelHasFirst()
    {
        int checksum = 0;
        int mask = VoxelCount - 1;
        for (int i = 0; i < LookupCount; i++)
            if (_voxels[i & mask].HasPartition<FirstEntry>())
                checksum++;
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int ProviderHasFirst()
    {
        int checksum = 0;
        int mask = VoxelCount - 1;
        for (int i = 0; i < LookupCount; i++)
            if (_providers[i & mask].Has<FirstEntry>())
                checksum++;
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int VoxelDefaultFirst()
    {
        int checksum = 0;
        int mask = VoxelCount - 1;
        for (int i = 0; i < LookupCount; i++)
        {
            int index = i & mask;
            if (ReferenceEquals(_voxels[index].GetPartitionOrDefault<FirstEntry>(), _first[index]))
                checksum++;
        }
        return checksum;
    }

    private int LookupVoxels<T>(T[] expected) where T : LookupEntry
    {
        int checksum = 0;
        int mask = VoxelCount - 1;
        for (int i = 0; i < LookupCount; i++)
        {
            int index = i & mask;
            bool found = _voxels[index].TryGetPartition(out T partition);
            if (found == (expected != null) && ReferenceEquals(partition, expected?[index]))
                checksum++;
        }
        return checksum;
    }

    private int LookupProviders<T>(T[] expected) where T : LookupEntry
    {
        int checksum = 0;
        int mask = VoxelCount - 1;
        for (int i = 0; i < LookupCount; i++)
        {
            int index = i & mask;
            bool found = _providers[index].TryGet(out T partition);
            if (found == (expected != null) && ReferenceEquals(partition, expected?[index]))
                checksum++;
        }
        return checksum;
    }

    private int LookupProvidersByType(Type type, LookupEntry[] expected)
    {
        int checksum = 0;
        int mask = VoxelCount - 1;
        for (int i = 0; i < LookupCount; i++)
        {
            int index = i & mask;
            bool found = _providers[index].TryGet(type, out IVoxelPartition partition);
            if (found == (expected != null) && ReferenceEquals(partition, expected?[index]))
                checksum++;
        }
        return checksum;
    }

    private abstract class LookupEntry : IVoxelPartition
    {
        public WorldVoxelIndex WorldIndex { get; private set; }

        public void SetParentIndex(WorldVoxelIndex index) => WorldIndex = index;

        public void OnAddToVoxel(Voxel voxel) { }

        public void OnRemoveFromVoxel(Voxel voxel) { }
    }

    private sealed class FirstEntry : LookupEntry { }

    private sealed class SecondEntry : LookupEntry { }

    private sealed class ThirdEntry : LookupEntry { }

    private sealed class MissingEntry : LookupEntry { }
}
