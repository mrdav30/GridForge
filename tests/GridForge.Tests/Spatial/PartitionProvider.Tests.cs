using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Tests;
using System;
using Xunit;

namespace GridForge.Spatial.Tests;

[Collection("GridForgeCollection")]
public sealed class PartitionProviderTests
{
    [Fact]
    public void TryAdd_FirstAndSecondConcreteTypes_ShouldNotAllocate()
    {
        PartitionProvider<object> warmup = new();
        _ = warmup.TryAdd(typeof(PartitionA), new PartitionA());
        _ = warmup.TryAdd(typeof(PartitionB), new PartitionB());
        warmup.Clear();

        PartitionProvider<object> provider = new();
        PartitionA first = new();
        PartitionB second = new();

        ForceCollection();

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool firstAdded = provider.TryAdd(typeof(PartitionA), first);
        bool secondAdded = provider.TryAdd(typeof(PartitionB), second);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(firstAdded);
        Assert.True(secondAdded);
        Assert.Equal(0, allocated);
        Assert.Equal(2, provider.Count);
        Assert.True(provider.TryGet(out PartitionA storedFirst));
        Assert.True(provider.TryGet(out PartitionB storedSecond));
        Assert.Same(first, storedFirst);
        Assert.Same(second, storedSecond);
    }

    [Fact]
    public void TryAdd_ThirdConcreteType_ShouldAllocateOnlyForInitialPromotion()
    {
        PartitionProvider<object> warmup = new();
        _ = warmup.TryAdd(typeof(PartitionA), new PartitionA());
        _ = warmup.TryAdd(typeof(PartitionB), new PartitionB());
        _ = warmup.TryAdd(typeof(PartitionC), new PartitionC());
        warmup.Clear();

        PartitionProvider<object> provider = new();
        PartitionA first = new();
        PartitionB second = new();
        PartitionC third = new();

        Assert.True(provider.TryAdd(typeof(PartitionA), first));
        Assert.True(provider.TryAdd(typeof(PartitionB), second));

        ForceCollection();

        long beforePromotion = GC.GetAllocatedBytesForCurrentThread();
        bool promoted = provider.TryAdd(typeof(PartitionC), third);
        long promotionAllocated = GC.GetAllocatedBytesForCurrentThread() - beforePromotion;

        Assert.True(promoted);
        Assert.True(promotionAllocated > 0);
        Assert.Equal(3, provider.Count);
        Assert.True(provider.TryGet(out PartitionC storedThird));
        Assert.Same(third, storedThird);

        Assert.True(provider.TryRemove(typeof(PartitionC), out object removed));
        Assert.Same(third, removed);

        ForceCollection();

        long beforeReusedPromotion = GC.GetAllocatedBytesForCurrentThread();
        bool reusedPromotion = provider.TryAdd(typeof(PartitionC), third);
        long reusedPromotionAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeReusedPromotion;

        Assert.True(reusedPromotion);
        Assert.Equal(0, reusedPromotionAllocated);
    }

    [Fact]
    public void InvalidMissingAndDuplicateTypes_ShouldNotChangeStoredValues()
    {
        PartitionProvider<object> provider = new();
        PartitionA first = new();
        PartitionB second = new();
        PartitionC third = new();

        Assert.True(provider.IsEmpty);
        Assert.False(provider.TryAdd(null, first));
        Assert.False(provider.TryAdd(typeof(PartitionA), null));
        Assert.True(provider.TryAdd(typeof(PartitionA), first));
        Assert.False(provider.TryAdd(typeof(PartitionA), new PartitionA()));
        Assert.True(provider.TryAdd(typeof(PartitionB), second));
        Assert.False(provider.TryAdd(typeof(PartitionB), new PartitionB()));
        Assert.True(provider.TryAdd(typeof(PartitionC), third));
        Assert.False(provider.TryAdd(typeof(PartitionC), new PartitionC()));

        Assert.Equal(3, provider.Count);
        Assert.True(provider.TryGet(out PartitionA storedFirst));
        Assert.True(provider.TryGet(out PartitionB storedSecond));
        Assert.True(provider.TryGet(out PartitionC storedThird));
        Assert.Same(first, storedFirst);
        Assert.Same(second, storedSecond);
        Assert.Same(third, storedThird);
        Assert.True(provider.Has(typeof(PartitionA)));
        Assert.True(provider.Has<PartitionB>());
        Assert.False(provider.TryGet((Type)null, out _));
        Assert.False(provider.TryGet(typeof(PartitionD), out _));
        Assert.False(provider.TryGet(out PartitionD _));
        Assert.False(provider.TryRemove(null, out _));
        Assert.False(provider.TryRemove(typeof(PartitionD), out _));
    }

    [Fact]
    public void FiveConcreteTypes_ShouldCoexistAndCompactInStableOrder()
    {
        PartitionProvider<object> provider = new();
        PartitionA first = new();
        PartitionB second = new();
        PartitionC third = new();
        PartitionD fourth = new();
        PartitionE fifth = new();

        Assert.True(provider.TryAdd(typeof(PartitionA), first));
        Assert.True(provider.TryAdd(typeof(PartitionB), second));
        Assert.True(provider.TryAdd(typeof(PartitionC), third));
        Assert.True(provider.TryAdd(typeof(PartitionD), fourth));
        Assert.True(provider.TryAdd(typeof(PartitionE), fifth));
        Assert.Equal(5, provider.Count);
        AssertProviderOrder(provider, first, second, third, fourth, fifth);

        Assert.True(provider.TryRemove(typeof(PartitionA), out object removed));
        Assert.Same(first, removed);
        AssertProviderOrder(provider, second, third, fourth, fifth);

        Assert.True(provider.TryRemove(typeof(PartitionD), out removed));
        Assert.Same(fourth, removed);
        AssertProviderOrder(provider, second, third, fifth);

        Assert.True(provider.TryRemove(typeof(PartitionC), out removed));
        Assert.Same(third, removed);
        AssertProviderOrder(provider, second, fifth);

        Assert.True(provider.TryRemove(typeof(PartitionB), out removed));
        Assert.Same(second, removed);
        AssertProviderOrder(provider, fifth);

        Assert.True(provider.TryRemove(typeof(PartitionE), out removed));
        Assert.Same(fifth, removed);
        Assert.True(provider.IsEmpty);
        Assert.Equal(0, provider.Count);
        AssertProviderOrder(provider);
    }

    [Fact]
    public void Clear_ShouldDiscardInlineAndPromotedValuesBeforeReuse()
    {
        PartitionProvider<object> provider = new();
        PartitionA first = new();
        PartitionB second = new();
        PartitionC third = new();
        PartitionD replacement = new();

        Assert.True(provider.TryAdd(typeof(PartitionA), first));
        Assert.True(provider.TryAdd(typeof(PartitionB), second));
        Assert.True(provider.TryAdd(typeof(PartitionC), third));

        provider.Clear();
        provider.Clear();

        Assert.True(provider.IsEmpty);
        Assert.Equal(0, provider.Count);
        Assert.False(provider.TryGet(typeof(PartitionA), out _));
        Assert.False(provider.TryGet(typeof(PartitionB), out _));
        Assert.False(provider.TryGet(typeof(PartitionC), out _));

        Assert.True(provider.TryAdd(typeof(PartitionD), replacement));
        Assert.True(provider.TryGet(out PartitionD storedReplacement));
        Assert.Same(replacement, storedReplacement);
        AssertProviderOrder(provider, replacement);
    }

    [Fact]
    public void RecycledVoxel_ShouldNotRetainPromotedPartitionTypesValuesOrGeneration()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero);

        Assert.True(world.TryAddGrid(configuration, out ushort originalGridIndex));
        VoxelGrid originalGrid = world.ActiveGrids[originalGridIndex];
        Assert.True(originalGrid.TryGetVoxel(Vector3d.Zero, out Voxel originalVoxel));

        PartitionA first = new();
        PartitionB second = new();
        PartitionC third = new();

        Assert.True(originalVoxel.TryAddPartition(first));
        Assert.True(originalVoxel.TryAddPartition(second));
        Assert.True(originalVoxel.TryAddPartition(third));

        WorldVoxelIndex originalWorldIndex = originalVoxel.WorldIndex;

        Assert.True(world.TryRemoveGrid(originalGridIndex));
        Assert.True(world.TryAddGrid(configuration, out ushort reusedGridIndex));

        VoxelGrid reusedGrid = world.ActiveGrids[reusedGridIndex];
        Assert.True(reusedGrid.TryGetVoxel(Vector3d.Zero, out Voxel reusedVoxel));

        Assert.NotEqual(originalWorldIndex.GridSpawnToken, reusedVoxel.WorldIndex.GridSpawnToken);
        Assert.False(reusedVoxel.HasPartition<PartitionA>());
        Assert.False(reusedVoxel.HasPartition<PartitionB>());
        Assert.False(reusedVoxel.HasPartition<PartitionC>());

        PartitionD replacement = new();
        Assert.True(reusedVoxel.TryAddPartition(replacement));
        Assert.Equal(reusedVoxel.WorldIndex, replacement.WorldIndex);
        Assert.Same(replacement, reusedVoxel.GetPartitionOrDefault<PartitionD>());
    }

    [Fact]
    public void RecycledVoxels_TwoConcreteTypes_ShouldNotAllocateAfterWarmup()
    {
        const int voxelCount = 16;
        Voxel[] voxels = new Voxel[voxelCount];
        for (int i = 0; i < voxelCount; i++)
        {
            VoxelIndex index = new(i, 0, 0);
            Voxel voxel = new();
            voxel.Initialize(
                new WorldVoxelIndex(1, 0, 1, index),
                new Vector3d(i, 0, 0),
                scanCellKey: 0,
                isBoundaryVoxel: false,
                gridVersion: 1);
            Assert.True(voxel.TryAddPartition(new PartitionA()));
            Assert.True(voxel.TryRemovePartition<PartitionA>());
            voxel.Reset();
            voxel.Initialize(
                new WorldVoxelIndex(1, 0, 2, index),
                new Vector3d(i, 0, 0),
                scanCellKey: 0,
                isBoundaryVoxel: false,
                gridVersion: 2);
            voxels[i] = voxel;
        }

        PartitionA[] firstPartitions = new PartitionA[voxelCount];
        PartitionB[] secondPartitions = new PartitionB[voxelCount];
        for (int i = 0; i < voxelCount; i++)
        {
            firstPartitions[i] = new PartitionA();
            secondPartitions[i] = new PartitionB();
        }

        ForceCollection();

        bool allSucceeded = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < voxelCount; i++)
        {
            allSucceeded &= voxels[i].TryAddPartition(firstPartitions[i]);
            allSucceeded &= voxels[i].TryAddPartition(secondPartitions[i]);
            allSucceeded &= voxels[i].TryRemovePartition<PartitionB>();
            allSucceeded &= voxels[i].TryRemovePartition<PartitionA>();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allSucceeded);
        Assert.Equal(0, allocated);
    }

    private static void AssertProviderOrder(PartitionProvider<object> provider, params object[] expected)
    {
        PartitionProvider<object>.Enumerator enumerator = provider.GetEnumerator();
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(enumerator.MoveNext());
            Assert.Same(expected[i], enumerator.Current);
        }

        Assert.False(enumerator.MoveNext());
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private abstract class TestPartition : IVoxelPartition
    {
        public WorldVoxelIndex WorldIndex { get; private set; }

        public void SetParentIndex(WorldVoxelIndex parentVoxelIndex)
        {
            WorldIndex = parentVoxelIndex;
        }

        public void OnAddToVoxel(Voxel voxel) { }

        public void OnRemoveFromVoxel(Voxel voxel) { }
    }

    private sealed class PartitionA : TestPartition { }

    private sealed class PartitionB : TestPartition { }

    private sealed class PartitionC : TestPartition { }

    private sealed class PartitionD : TestPartition { }

    private sealed class PartitionE : TestPartition { }
}
