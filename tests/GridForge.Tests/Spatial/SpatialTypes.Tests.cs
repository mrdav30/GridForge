using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Tests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace GridForge.Spatial.Tests;

[Collection("GridForgeCollection")]
public class SpatialTypesTests : IDisposable
{
    public SpatialTypesTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();

        GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VoxelIndex_ShouldSupportTwoArgumentConstructionAndEqualityOperators()
    {
        VoxelIndex twoArgumentIndex = new(3, 4);
        VoxelIndex sameIndex = new(3, 4, 0);
        VoxelIndex differentX = new(4, 4, 0);
        VoxelIndex differentY = new(3, 5, 0);
        VoxelIndex differentZ = new(3, 4, 1);
        object boxedIndex = sameIndex;

        Assert.True(twoArgumentIndex.IsAllocated);
        Assert.Equal(0, twoArgumentIndex.z);
        Assert.True(twoArgumentIndex == sameIndex);
        Assert.False(twoArgumentIndex != sameIndex);
        Assert.True(twoArgumentIndex.Equals(boxedIndex));
        Assert.False(twoArgumentIndex.Equals(differentX));
        Assert.False(twoArgumentIndex.Equals(differentY));
        Assert.False(twoArgumentIndex.Equals(differentZ));
        Assert.False(twoArgumentIndex.Equals("not a voxel index"));
    }

    [Fact]
    public void GlobalVoxelIndex_ShouldSupportEqualityOperatorsAndObjectComparison()
    {
        GlobalVoxelIndex first = new(2, new VoxelIndex(1, 2, 3), 99);
        GlobalVoxelIndex second = new(2, new VoxelIndex(1, 2, 3), 100);
        object boxed = second;

        Assert.False(first == second);
        Assert.True(first != second);
        Assert.False(first.Equals(boxed));
        Assert.False(first.Equals("not a global voxel index"));
        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
        Assert.Contains("Index: 2", first.ToString());
    }

    [Fact]
    public void GridConfiguration_ShouldSnapBoundsAndFallbackToDefaultScanCellSize_WhenInputsAreInvalid()
    {
        GridConfiguration configuration = new(
            new Vector3d(5, 5, 5),
            new Vector3d(1, 1, 1),
            scanCellSize: 0);

        Assert.Equal(new Vector3d(1, 1, 1), configuration.BoundsMin);
        Assert.Equal(new Vector3d(5, 5, 5), configuration.BoundsMax);
        Assert.Equal(GridConfiguration.DefaultScanCellSize, configuration.ScanCellSize);
        Assert.Equal(new Vector3d(3, 3, 3), configuration.GridCenter);
        Assert.Equal(configuration.ToBoundsKey(), new BoundsKey(configuration.BoundsMin, configuration.BoundsMax));
    }

    [Fact]
    public void GridConfiguration_ShouldUseJsonConstructorNormalization_WhenDeserializing()
    {
        string boundsMinJson = JsonSerializer.Serialize(new Vector3d(5, 5, 5));
        string boundsMaxJson = JsonSerializer.Serialize(new Vector3d(1, 1, 1));
        string json = $$"""
            {
              "BoundsMin": {{boundsMinJson}},
              "BoundsMax": {{boundsMaxJson}},
              "ScanCellSize": 0
            }
            """;

        GridConfiguration configuration = JsonSerializer.Deserialize<GridConfiguration>(json);

        Assert.Equal(new Vector3d(1, 1, 1), configuration.BoundsMin);
        Assert.Equal(new Vector3d(5, 5, 5), configuration.BoundsMax);
        Assert.Equal(GridConfiguration.DefaultScanCellSize, configuration.ScanCellSize);
    }

    [Fact]
    public void PartitionProvider_ShouldTrackCountAndTypedLookups()
    {
        PartitionProvider<object> provider = new();
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

    [Fact]
    public void PartitionProvider_ShouldExposePartitionsAndRejectMissingLookups()
    {
        PartitionProvider<object> provider = new();
        ProviderEntryA first = new();
        ProviderEntryB second = new();
        PartitionProvider<object> mismatchedProvider = new();
        IEnumerable<object> emptyPartitions = GetPartitions(provider);

        Assert.True(provider.TryAdd(typeof(ProviderEntryA), first));
        Assert.True(provider.TryAdd(typeof(ProviderEntryB), second));
        IEnumerable<object> partitions = GetPartitions(provider);

        Assert.Empty(emptyPartitions);
        Assert.Equal(2, partitions.Count());
        Assert.Contains(first, partitions);
        Assert.Contains(second, partitions);

        Assert.False(provider.TryGet((Type)null, out object missingByNullType));
        Assert.Null(missingByNullType);
        Assert.False(provider.TryGet(typeof(ProviderEntryC), out object missingByType));
        Assert.Null(missingByType);
        Assert.False(provider.TryRemove(typeof(ProviderEntryC), out object removedMissing));
        Assert.Null(removedMissing);
        Assert.False(provider.TryGet(out ProviderEntryC missingTyped));
        Assert.Null(missingTyped);

        Assert.True(mismatchedProvider.TryAdd(typeof(ProviderEntryC), new ProviderEntryA()));
        Assert.False(mismatchedProvider.TryGet(out ProviderEntryC mismatchedTyped));
        Assert.Null(mismatchedTyped);
        mismatchedProvider.Clear();
        mismatchedProvider.Clear();
        Assert.True(mismatchedProvider.IsEmpty);

        Assert.True(provider.TryRemove(typeof(ProviderEntryA), out _));
        Assert.True(provider.TryRemove(typeof(ProviderEntryB), out _));
        Assert.True(provider.IsEmpty);
        Assert.False(provider.TryRemove(typeof(ProviderEntryB), out object removedAgain));
        Assert.Null(removedAgain);
    }

    [Fact]
    public void EventInfoStructs_ShouldExposeStoredIndicesAndBounds()
    {
        GridConfiguration configuration = new(
            new Vector3d(-2, 0, -1),
            new Vector3d(2, 0, 1),
            scanCellSize: 4);
        GlobalVoxelIndex voxelIndex = new(7, new VoxelIndex(1, 2, 3), 42);
        BoundsKey obstacleToken = new(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1));
        TestOccupant occupant = new(new Vector3d(0, 0, 0), 5);

        GridEventInfo gridEventInfo = new(7, 99, configuration, 3);
        ObstacleEventInfo obstacleEventInfo = new(voxelIndex, obstacleToken, 2, 4);
        ObstacleClearEventInfo obstacleClearEventInfo = new(voxelIndex, 2, 5);
        OccupantEventInfo occupantEventInfo = new(voxelIndex, occupant, 12, 1);

        Assert.Equal((ushort)7, gridEventInfo.GridIndex);
        Assert.Equal(99, gridEventInfo.GridSpawnToken);
        Assert.Equal(configuration.BoundsMin, gridEventInfo.BoundsMin);
        Assert.Equal(configuration.BoundsMax, gridEventInfo.BoundsMax);
        Assert.Equal(configuration.ToBoundsKey(), gridEventInfo.ToBoundsKey());
        Assert.Equal(3u, gridEventInfo.GridVersion);

        Assert.Equal((ushort)7, obstacleEventInfo.GridIndex);
        Assert.Equal(voxelIndex, obstacleEventInfo.VoxelIndex);
        Assert.Equal(obstacleToken, obstacleEventInfo.ObstacleToken);
        Assert.Equal((byte)2, obstacleEventInfo.ObstacleCount);
        Assert.Equal(4u, obstacleEventInfo.GridVersion);

        Assert.Equal((ushort)7, obstacleClearEventInfo.GridIndex);
        Assert.Equal(voxelIndex, obstacleClearEventInfo.VoxelIndex);
        Assert.Equal((byte)2, obstacleClearEventInfo.ClearedObstacleCount);
        Assert.Equal(5u, obstacleClearEventInfo.GridVersion);

        Assert.Equal((ushort)7, occupantEventInfo.GridIndex);
        Assert.Equal(voxelIndex, occupantEventInfo.VoxelIndex);
        Assert.Same(occupant, occupantEventInfo.Occupant);
        Assert.Equal(12, occupantEventInfo.Ticket);
        Assert.Equal((byte)1, occupantEventInfo.OccupantCount);
    }

    private sealed class ProviderEntryA { }

    private sealed class ProviderEntryB { }

    private sealed class ProviderEntryC { }

    private static IEnumerable<object> GetPartitions(PartitionProvider<object> provider)
    {
        PropertyInfo partitionsProperty = typeof(PartitionProvider<object>).GetProperty(
            "Partitions",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate PartitionProvider.Partitions.");

        return (IEnumerable<object>)partitionsProperty.GetValue(provider);
    }
}
