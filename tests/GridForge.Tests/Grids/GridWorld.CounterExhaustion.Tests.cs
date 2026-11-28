using System;
using System.Collections.Generic;
using System.Reflection;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridWorldCounterExhaustionTests : IDisposable
{
    private readonly GridWorld _world = GridWorldTestFactory.CreateWorld();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SparseMutationAtExhaustionPreservesTheBoundedSnapshot(bool add)
    {
        VoxelIndex index = new(0, 0, 0);
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero, storageKind: GridStorageKind.Sparse);
        VoxelIndex[] configured = add ? new[] { index } : Array.Empty<VoxelIndex>();
        Assert.True(_world.TryAddGrid(configuration, configured, out ushort slot));
        VoxelGrid grid = _world.ActiveGrids[slot];
        List<GridEventInfo> events = new();
        _world.OnChangeCommitted += events.Add;
        GridWorldTestFactory.SeedPublishedSequence(_world, ulong.MaxValue - 1);

        // Use the last commit to establish a real, fully published voxel state.
        if (add)
            Assert.True(grid.TryRemoveVoxel(index));
        else
            Assert.True(grid.TryAddVoxel(index, out _));
        GridEventInfo last = Assert.Single(events);
        Assert.Equal(add ? GridEventKind.SparseVoxelRemoved : GridEventKind.SparseVoxelAdded, last.ChangeKind);
        Assert.Equal(ulong.MaxValue, last.ChangeSequence);

        Voxel[] before = new Voxel[1];
        Assert.Equal(GridBoundedVoxelStatus.Completed, _world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 10, before, out _, out int count, out ulong snapshot));
        Assert.Equal(add ? 0 : 1, count);
        Assert.Equal(ulong.MaxValue, snapshot);
        uint version = grid.Version;
        if (add)
        {
            Assert.False(grid.TryRemoveVoxel(index));
            Assert.Throws<OverflowException>(() => grid.TryAddVoxel(index, out _));
        }
        else
        {
            Assert.False(grid.TryAddVoxel(index, out _));
            Assert.Throws<OverflowException>(() => grid.TryRemoveVoxel(index));
        }

        Assert.Equal(GridBoundedVoxelStatus.Completed, _world.CheckBoundedVoxelSnapshot(snapshot));
        Voxel[] after = new Voxel[1];
        Assert.Equal(GridBoundedVoxelStatus.Completed, _world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 10, after, out _, out int currentCount, out ulong current));
        Assert.Equal(count, currentCount);
        Assert.Same(before[0], after[0]);
        Assert.Equal(snapshot, current);
        Assert.Equal(snapshot, grid.LastChangeSequence);
        Assert.Equal(count, grid.ConfiguredVoxelCount);
        Assert.Equal(version, grid.Version);
        Assert.Single(events);
    }

    [Theory]
    [InlineData(GridEventKind.ObstacleAdded)]
    [InlineData(GridEventKind.ObstacleRemoved)]
    [InlineData(GridEventKind.ObstaclesCleared)]
    public void ObstacleMutationAtExhaustionPreservesMembershipAndEvents(GridEventKind mutation)
    {
        VoxelIndex index = new(0, 0, 0);
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero, storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(configuration, new[] { index }, out ushort slot));
        VoxelGrid grid = _world.ActiveGrids[slot];
        Assert.True(grid.TryGetVoxel(index, out Voxel voxel));
        ObstacleToken first = _world.AllocateObstacleToken();
        ObstacleToken second = _world.AllocateObstacleToken();
        List<GridEventInfo> events = new();
        _world.OnChangeCommitted += events.Add;
        GridWorldTestFactory.SeedPublishedSequence(_world, ulong.MaxValue - 1);
        Assert.True(grid.TryAddObstacle(voxel, first));
        GridEventInfo last = Assert.Single(events);
        Assert.Equal(GridEventKind.ObstacleAdded, last.ChangeKind);
        Assert.Equal(ulong.MaxValue, last.ChangeSequence);
        uint version = grid.Version;
        Assert.False(grid.TryRemoveVoxel(index));
        Assert.False(grid.TryAddObstacle(voxel, first));
        Assert.False(grid.TryRemoveObstacle(voxel, second));

        if (mutation == GridEventKind.ObstacleAdded)
            Assert.Throws<OverflowException>(() => grid.TryAddObstacle(voxel, second));
        else if (mutation == GridEventKind.ObstacleRemoved)
            Assert.Throws<OverflowException>(() => grid.TryRemoveObstacle(voxel, first));
        else
            Assert.Throws<OverflowException>(() => grid.ClearObstacles(voxel));

        Assert.Equal(1, grid.ObstacleCount);
        Assert.Equal(1, voxel.ObstacleCount);
        Assert.Contains(first, voxel.ObstacleTracker);
        Assert.DoesNotContain(second, voxel.ObstacleTracker);
        Assert.Equal(version, voxel.CachedGridVersion);
        Assert.Equal(version, grid.Version);
        Assert.Equal(ulong.MaxValue, grid.LastChangeSequence);
        Assert.Equal(ulong.MaxValue, _world.ChangeSequence);
        Assert.Single(events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExhaustedRegistrationLeavesTheOnlyPublishedGridResolvable(bool identityExhausted)
    {
        GridConfiguration original = new(Vector3d.Zero, Vector3d.Zero);
        GridConfiguration other = new(Vector3d.One, Vector3d.One);
        if (identityExhausted)
            SetWorldField("_gridGenerationCounter", long.MaxValue - 1);
        else
            GridWorldTestFactory.SeedPublishedSequence(_world, ulong.MaxValue - 1);
        List<GridEventInfo> events = new();
        _world.OnChangeCommitted += events.Add;
        Assert.True(_world.TryAddGrid(original, out ushort slot));
        VoxelGrid grid = _world.ActiveGrids[slot];
        GridEventInfo last = Assert.Single(events);
        Assert.Equal(GridEventKind.GridAdded, last.ChangeKind);
        Assert.Equal(grid.SpawnToken, last.GridSpawnToken);
        Assert.Equal(identityExhausted ? 1UL : ulong.MaxValue, last.ChangeSequence);
        uint version = _world.Version;
        Assert.False(_world.TryAddGrid(original, out _));
        if (identityExhausted)
        {
            Assert.Equal(long.MaxValue, grid.SpawnToken);
            Assert.Throws<InvalidOperationException>(() => _world.TryAddGrid(other, out _));
        }
        else
        {
            Assert.Throws<OverflowException>(() => _world.TryAddGrid(other, out _));
        }

        Assert.Single(_world.ActiveGrids);
        Assert.Single(_world.BoundsTracker);
        Assert.True(_world.TryGetGrid(Vector3d.Zero, out VoxelGrid current));
        Assert.Same(grid, current);
        Assert.False(_world.TryGetGrid(Vector3d.One, out _));
        Assert.Equal(last.ChangeSequence, _world.ChangeSequence);
        Assert.Equal(last.ChangeSequence, grid.LastChangeSequence);
        Assert.Equal(version, _world.Version);
        Assert.Single(events);
    }

    [Fact]
    public void HighSlotRetainsItsVoxelIdentityAfterLowerSlotsAreRemoved()
    {
        VoxelGrid first = GridWorldTestFactory.AddGrid(_world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid second = GridWorldTestFactory.AddGrid(_world, Vector3d.One, Vector3d.One);
        Vector3d position = new Vector3d(2, 0, 0);
        VoxelGrid survivor = GridWorldTestFactory.AddGrid(_world, position, position);
        Assert.True(survivor.TryGetVoxel(position, out Voxel original));
        WorldVoxelIndex identity = original.WorldIndex;

        Assert.True(_world.TryRemoveGrid(first.GridIndex));
        Assert.True(_world.TryRemoveGrid(second.GridIndex));

        Assert.True(_world.TryGetVoxel(identity, out Voxel current));
        Assert.Same(original, current);
    }

    [Fact]
    public void FirstObstacleAtExhaustionLeavesTheEmptyVoxelUntouched()
    {
        VoxelGrid grid = GridWorldTestFactory.AddGrid(_world, Vector3d.Zero, Vector3d.Zero);
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        ObstacleToken token = _world.AllocateObstacleToken();
        GridWorldTestFactory.SeedPublishedSequence(_world, ulong.MaxValue);

        grid.ClearObstacles(voxel);
        Assert.Throws<OverflowException>(() => grid.TryAddObstacle(voxel, token));
        Assert.Null(voxel.ObstacleTracker);
        Assert.Equal(0, grid.ObstacleCount);
        Assert.Equal(0, voxel.ObstacleCount);
    }

    private void SetWorldField(string name, object value) =>
        typeof(GridWorld).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_world, value);

    public void Dispose()
    {
        // Undo the synthetic sequence boundary so teardown cannot mask a failing assertion.
        GridWorldTestFactory.SeedPublishedSequence(_world, 1);
        _world.Dispose();
    }
}
