using System;
using Xunit;

namespace GridForge.Spatial.Tests;

public class SpatialTypesTests
{
    [Fact]
    public void VoxelIndex_ShouldSupportTwoArgumentConstructionAndEqualityOperators()
    {
        VoxelIndex twoArgumentIndex = new VoxelIndex(3, 4);
        VoxelIndex sameIndex = new VoxelIndex(3, 4, 0);
        object boxedIndex = sameIndex;

        Assert.True(twoArgumentIndex.IsAllocated);
        Assert.Equal(0, twoArgumentIndex.z);
        Assert.True(twoArgumentIndex == sameIndex);
        Assert.False(twoArgumentIndex != sameIndex);
        Assert.True(twoArgumentIndex.Equals(boxedIndex));
    }

    [Fact]
    public void GlobalVoxelIndex_ShouldSupportEqualityOperatorsAndObjectComparison()
    {
        GlobalVoxelIndex first = new GlobalVoxelIndex(2, new VoxelIndex(1, 2, 3), 99);
        GlobalVoxelIndex second = new GlobalVoxelIndex(2, new VoxelIndex(1, 2, 3), 100);
        object boxed = second;

        Assert.False(first == second);
        Assert.True(first != second);
        Assert.False(first.Equals(boxed));
        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
        Assert.Contains("Index: 2", first.ToString());
    }

    [Fact]
    public void PartitionProvider_ShouldTrackCountAndTypedLookups()
    {
        PartitionProvider<object> provider = new PartitionProvider<object>();
        Type firstType = typeof(ProviderEntryA);
        Type secondType = typeof(ProviderEntryB);
        object first = new ProviderEntryA();
        object second = new ProviderEntryB();

        Assert.True(provider.IsEmpty);
        Assert.Equal(0, provider.Count);
        Assert.False(provider.TryAdd(null, first));
        Assert.False(provider.TryAdd(firstType, null));

        Assert.True(provider.TryAdd(firstType, first));
        Assert.True(provider.TryAdd(secondType, second));
        Assert.Equal(2, provider.Count);
        Assert.True(provider.Has(firstType));
        Assert.True(provider.Has<ProviderEntryA>());
        Assert.True(provider.TryGet(firstType, out object byType));
        Assert.Same(first, byType);
        Assert.True(provider.TryGet(out ProviderEntryB typed));
        Assert.Same(second, typed);

        Assert.True(provider.TryRemove(firstType, out object removed));
        Assert.Same(first, removed);
        Assert.False(provider.TryRemove(null, out _));

        provider.Clear();

        Assert.True(provider.IsEmpty);
        Assert.False(provider.Has(secondType));
        Assert.Equal(0, provider.Count);
    }

    private sealed class ProviderEntryA { }

    private sealed class ProviderEntryB { }
}
