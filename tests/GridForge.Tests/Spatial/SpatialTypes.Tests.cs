using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Tests;
using GridForge.Grids.Topology;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace GridForge.Spatial.Tests;

[Collection("GridForgeCollection")]
public class SpatialTypesTests
{
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
    public void VoxelIndex_ShouldCompareByXThenYThenZ()
    {
        VoxelIndex origin = new(0, 0, 0);
        VoxelIndex same = new(0, 0, 0);

        Assert.Equal(0, origin.CompareTo(same));
        Assert.True(origin.CompareTo(new VoxelIndex(1, 0, 0)) < 0);
        Assert.True(new VoxelIndex(1, 0, 0).CompareTo(origin) > 0);
        Assert.True(new VoxelIndex(1, 0, 0).CompareTo(new VoxelIndex(1, 1, 0)) < 0);
        Assert.True(new VoxelIndex(1, 1, 0).CompareTo(new VoxelIndex(1, 1, 1)) < 0);
        Assert.True(new VoxelIndex(1, 1, 2).CompareTo(new VoxelIndex(1, 1, 1)) > 0);
    }

    [Fact]
    public void WorldVoxelIndex_ShouldSupportEqualityOperatorsAndObjectComparison()
    {
        WorldVoxelIndex first = new(17, 2, 99, new VoxelIndex(1, 2, 3));
        WorldVoxelIndex second = new(17, 2, 100, new VoxelIndex(1, 2, 3));
        object boxed = second;

        Assert.False(first == second);
        Assert.True(first != second);
        Assert.False(first.Equals(boxed));
        Assert.False(first.Equals("not a world voxel index"));
        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
        Assert.Contains("World: 17", first.ToString());
        Assert.Contains("Grid: 2", first.ToString());
    }

    [Fact]
    public void WorldVoxelIndex_GetHashCode_ShouldAvoidAllocation()
    {
        WorldVoxelIndex index = new(17, 2, 99, new VoxelIndex(1, 2, 3));

        _ = index.GetHashCode();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int iterations = 256;
        int hash = 17;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            hash = unchecked((hash * 31) + index.GetHashCode());
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotEqual(0, hash);
        Assert.True(allocated < 64, $"Expected allocation-free hashing but allocated {allocated} bytes.");
    }

    [Fact]
    public void GridConfiguration_ShouldNormalizeOrderingAndFallbackToDefaultScanCellSize_WhenInputsAreInvalid()
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
        Assert.Equal(GridTopologyKind.RectangularPrism, configuration.TopologyKind);
        Assert.Equal(GridTopologyMetrics.Rectangular(GridWorld.DefaultRectangularCellSize), configuration.TopologyMetrics);
    }

    [Fact]
    public void GridConfiguration_ShouldIncludeTopologyInGridIdentity()
    {
        GridConfiguration defaultRectangular = new(
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 4),
            scanCellSize: 2);
        GridConfiguration differentScanSize = new(
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 4),
            scanCellSize: 16);
        GridConfiguration halfCellRectangular = new(
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 4),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)0.5));

        Assert.Equal(defaultRectangular.ToGridKey(), differentScanSize.ToGridKey());
        Assert.Equal(defaultRectangular.GetHashCode(), differentScanSize.GetHashCode());
        Assert.NotEqual(defaultRectangular.ToGridKey(), halfCellRectangular.ToGridKey());
        Assert.NotEqual(defaultRectangular.GetHashCode(), halfCellRectangular.GetHashCode());
    }

    [Fact]
    public void GridConfigurationKey_ShouldSupportObjectEqualityAndOperators()
    {
        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 4),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        GridConfigurationKey key = configuration.ToGridKey();
        GridConfigurationKey same = configuration.ToGridKey();
        GridConfigurationKey different = new(
            configuration.BoundsMin,
            configuration.BoundsMax,
            GridTopologyKind.HexPrism,
            GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One));
        object boxed = same;

        Assert.True(key.Equals(boxed));
        Assert.False(key.Equals("not a key"));
        Assert.True(key == same);
        Assert.False(key != same);
        Assert.False(key == different);
        Assert.True(key != different);
    }

    [Fact]
    public void GridTopologyMetrics_ShouldSupportObjectEqualityAndOperators()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, new Fixed64(2), HexOrientation.FlatTop);
        GridTopologyMetrics same = GridTopologyMetrics.Hex(Fixed64.One, new Fixed64(2), HexOrientation.FlatTop);
        GridTopologyMetrics different = GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(2), HexOrientation.FlatTop);
        object boxed = same;

        Assert.True(metrics.Equals(boxed));
        Assert.False(metrics.Equals("not metrics"));
        Assert.True(metrics == same);
        Assert.False(metrics != same);
        Assert.False(metrics == different);
        Assert.True(metrics != different);
    }

    [Fact]
    public void GridTopologyMetrics_ShouldValidateRequiredDimensionsByTopologyKind()
    {
        MethodInfo isValid = typeof(GridTopologyMetrics).GetMethod(
            "IsValid",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(GridTopologyKind), typeof(GridTopologyMetrics) },
            null);

        Assert.NotNull(isValid);
        Assert.True(InvokeIsValid(isValid, GridTopologyKind.RectangularPrism, GridTopologyMetrics.Rectangular(Fixed64.One)));
        Assert.False(InvokeIsValid(
            isValid,
            GridTopologyKind.RectangularPrism,
            new GridTopologyMetrics(
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One,
                Fixed64.One)));
        Assert.False(InvokeIsValid(
            isValid,
            GridTopologyKind.RectangularPrism,
            new GridTopologyMetrics(
                Fixed64.Zero,
                Fixed64.One,
                Fixed64.Zero,
                Fixed64.One)));
        Assert.False(InvokeIsValid(
            isValid,
            GridTopologyKind.RectangularPrism,
            new GridTopologyMetrics(
                Fixed64.Zero,
                Fixed64.One,
                Fixed64.One,
                Fixed64.Zero)));
        Assert.True(InvokeIsValid(isValid, GridTopologyKind.HexPrism, GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One)));
        Assert.False(InvokeIsValid(isValid, GridTopologyKind.HexPrism, GridTopologyMetrics.Hex(Fixed64.Zero, Fixed64.One)));
        Assert.False(InvokeIsValid(isValid, GridTopologyKind.HexPrism, GridTopologyMetrics.Hex(Fixed64.One, Fixed64.Zero)));
        Assert.False(InvokeIsValid(isValid, (GridTopologyKind)int.MaxValue, GridTopologyMetrics.Rectangular(Fixed64.One)));
    }

    [Fact]
    public void DirectionUtilities_ShouldClassifyDirectionsAndBoundaryRanges()
    {
        Assert.True(RectangularDirectionUtility.IsPerpendicularNeighbor(RectangularDirection.West));
        Assert.False(RectangularDirectionUtility.IsPerpendicularNeighbor(RectangularDirection.SouthWest));
        Assert.True(RectangularDirectionUtility.IsDiagonalNeighbor(RectangularDirection.SouthWest));
        Assert.False(RectangularDirectionUtility.IsDiagonalNeighbor(RectangularDirection.Above));
        Assert.Equal(RectangularDirection.NorthEast, RectangularDirectionUtility.GetDirectionFromOffset((1, 0, 1)));
        Assert.Equal(RectangularDirection.None, RectangularDirectionUtility.GetDirectionFromOffset((2, 0, 0)));
        Assert.Equal((0, 0), RectangularDirectionUtility.GetBoundaryRange(-1, 5));
        Assert.Equal((4, 4), RectangularDirectionUtility.GetBoundaryRange(1, 5));
        Assert.Equal((0, 4), RectangularDirectionUtility.GetBoundaryRange(0, 5));

        Assert.True(HexDirectionUtility.IsPlanar(HexDirection.QPositive));
        Assert.False(HexDirectionUtility.IsPlanar(HexDirection.Above));
        Assert.True(HexDirectionUtility.IsVertical(HexDirection.Below));
        Assert.True(HexDirectionUtility.IsVertical(HexDirection.Above));
        Assert.False(HexDirectionUtility.IsVertical(HexDirection.QPositive));
        Assert.Equal(RectangularDirection.None, RectangularDirectionUtility.GetDirectionFromOffset((2, 0, 0)));
    }

    [Fact]
    public void RectangularTopology_ShouldResolveBoundaryRangesForDirectionSlots()
    {
        RectangularPrismTopology topology = new RectangularPrismTopology(
            GridTopologyMetrics.Rectangular(Fixed64.One));
        (Vector3d unpaddedMin, Vector3d unpaddedMax) = topology.NormalizeBounds(
            Vector3d.FromDouble(0.25, 0.25, 0.25),
            Vector3d.FromDouble(0.75, 0.75, 0.75),
            Fixed64.Zero);
        (Vector3d paddedMin, Vector3d paddedMax) = topology.NormalizeBounds(
            Vector3d.FromDouble(0.25, 0.25, 0.25),
            Vector3d.FromDouble(0.75, 0.75, 0.75),
            Fixed64.Half);

        topology.GetBoundaryRange(
            (int)RectangularDirection.NorthEast,
            width: 4,
            height: 3,
            length: 5,
            out int xStart,
            out int xEnd,
            out int yStart,
            out int yEnd,
            out int zStart,
            out int zEnd);

        Assert.Equal(Vector3d.Zero, unpaddedMin);
        Assert.Equal(Vector3d.One, unpaddedMax);
        Assert.Equal(new Vector3d(-1, -1, -1), paddedMin);
        Assert.Equal(new Vector3d(2, 2, 2), paddedMax);
        Assert.Equal(3, xStart);
        Assert.Equal(3, xEnd);
        Assert.Equal(0, yStart);
        Assert.Equal(2, yEnd);
        Assert.Equal(4, zStart);
        Assert.Equal(4, zEnd);
    }

    [Fact]
    public void HexCoordinateUtility_ShouldRoundDominantCubeAxisAndCeilOutsideTolerance()
    {
        HexCoordinateUtility.RoundCube((Fixed64)0.51, (Fixed64)0.2, out int qAdjusted, out int rFromQ);
        Assert.Equal(1, qAdjusted);
        Assert.Equal(0, rFromQ);

        HexCoordinateUtility.RoundCube((Fixed64)0.2, (Fixed64)0.51, out int qFromR, out int rAdjusted);
        Assert.Equal(0, qFromR);
        Assert.Equal(1, rAdjusted);

        Assert.Equal(2, HexCoordinateUtility.CeilToIntWithTolerance((Fixed64)1.25));
    }

    [Fact]
    public void TopologyVoxelRangeUtility_ShouldRejectInactiveGrid()
    {
        Assert.False(TopologyVoxelRangeUtility.TryGetCandidateRange(
            new VoxelGrid(),
            Vector3d.Zero,
            Vector3d.Zero,
            out VoxelIndex minIndex,
            out VoxelIndex maxIndex));
        Assert.Equal(default, minIndex);
        Assert.Equal(default, maxIndex);
    }

    [Fact]
    public void GridConfiguration_ShouldUseJsonConstructorOrderingNormalization_WhenDeserializing()
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
    public void PartitionProvider_ReAddAfterLastRemove_ShouldReuseBackingStorage()
    {
        PartitionProvider<object> provider = new();
        Type entryType = typeof(ProviderEntryA);
        ProviderEntryA entry = new();

        Assert.True(provider.TryAdd(entryType, entry));
        Assert.True(provider.TryRemove(entryType, out _));
        Assert.True(provider.TryAdd(entryType, entry));
        Assert.True(provider.TryRemove(entryType, out _));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int iterations = 256;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            Assert.True(provider.TryAdd(entryType, entry));
            Assert.True(provider.TryRemove(entryType, out _));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64, $"Expected repeated partition re-adds to reuse storage but allocated {allocated} bytes.");
    }

    [Fact]
    public void PartitionProvider_FirstSinglePartitionAdd_ShouldNotAllocate()
    {
        PartitionProvider<object> provider = new();
        Type entryType = typeof(ProviderEntryA);
        ProviderEntryA entry = new();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(provider.TryAdd(entryType, entry));
        Assert.True(provider.TryRemove(entryType, out object removed));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(entry, removed);
        Assert.True(allocated < 64, $"Expected first single partition add/remove to avoid backing storage allocation but allocated {allocated} bytes.");
    }

    [Fact]
    public void EventInfoStructs_ShouldExposeStoredIndicesAndBounds()
    {
        GridConfiguration configuration = new(
            new Vector3d(-2, 0, -1),
            new Vector3d(2, 0, 1),
            scanCellSize: 4);
        WorldVoxelIndex voxelIndex = new(13, 7, 42, new VoxelIndex(1, 2, 3));
        BoundsKey obstacleToken = new(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1));
        TestOccupant occupant = new(new Vector3d(0, 0, 0), 5);

        GridEventInfo gridEventInfo = new(13, 7, 99, configuration, 3);
        ObstacleEventInfo obstacleEventInfo = new(voxelIndex, obstacleToken, 2, 4);
        ObstacleClearEventInfo obstacleClearEventInfo = new(voxelIndex, 2, 5);
        OccupantEventInfo occupantEventInfo = new(voxelIndex, occupant, 12, 1);

        Assert.Equal(13, gridEventInfo.WorldSpawnToken);
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

    private static bool InvokeIsValid(MethodInfo isValid, GridTopologyKind kind, GridTopologyMetrics metrics) =>
        (bool)isValid.Invoke(null, new object[] { kind, metrics });
}
