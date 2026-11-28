using System;
using System.Collections.Generic;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Tests;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Blockers.Tests;

[Collection("GridForgeCollection")]
public sealed class BlockerCounterExhaustionTests : IDisposable
{
    private readonly GridWorld _world = GridWorldTestFactory.CreateWorld();

    public enum Operation { Apply, Rollback, Remove }

    [Theory]
    [InlineData(false, Operation.Apply)]
    [InlineData(true, Operation.Apply)]
    [InlineData(false, Operation.Rollback)]
    [InlineData(true, Operation.Rollback)]
    [InlineData(false, Operation.Remove)]
    [InlineData(true, Operation.Remove)]
    public void InterruptedBlockerRetainsItsRemainingAttachmentUntilDeactivation(bool cache, Operation operation)
    {
        Vector3d max = new(operation == Operation.Rollback ? 2 : 1, 0, 0);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(_world, Vector3d.Zero, max);
        BoundsBlocker blocker = new(_world, Vector3d.Zero, max, cacheCoveredVoxels: cache);
        if (operation == Operation.Rollback)
        {
            Assert.True(grid.TryGetVoxel(max, out Voxel occupied));
            Assert.True(grid.TryAddVoxelOccupant(occupied, new TestOccupant(max)));
        }
        if (operation == Operation.Remove)
            blocker.ApplyBlockage();

        var changes = new List<GridEventKind>();
        _world.OnChangeCommitted += info => changes.Add(info.ChangeKind);
        int notifications = 0;
        void CountNotification(BlockageEventInfo _) => notifications++;
        Blocker.OnBlockageApplied += CountNotification;
        Blocker.OnBlockageRemoved += CountNotification;
        try
        {
            GridWorldTestFactory.SeedPublishedSequence(_world, ulong.MaxValue - (operation == Operation.Rollback ? 3UL : 1UL));
            if (operation == Operation.Remove)
                Assert.Throws<OverflowException>(blocker.RemoveBlockage);
            else
                Assert.Throws<OverflowException>(blocker.ApplyBlockage);

            GridEventKind[] expectedChanges = operation switch
            {
                Operation.Apply => new[] { GridEventKind.ObstacleAdded },
                Operation.Remove => new[] { GridEventKind.ObstacleRemoved },
                _ => new[] { GridEventKind.ObstacleAdded, GridEventKind.ObstacleAdded, GridEventKind.ObstacleRemoved }
            };
            Assert.Equal(expectedChanges, changes);
            Assert.True(blocker.IsBlocking);
            ObstacleToken token = blocker.BlockageToken;
            Assert.True(token.IsValid);
            Voxel remaining = Assert.Single(grid.EnumerateVoxels(), voxel => voxel.IsBlocked);
            Assert.Contains(token, remaining.ObstacleTracker);
            Assert.Throws<OverflowException>(blocker.RemoveBlockage);
            Assert.True(blocker.IsBlocking);
            Assert.Equal(token, blocker.BlockageToken);
            Assert.Contains(token, remaining.ObstacleTracker);
            Assert.Equal(1, grid.ObstacleCount);
            Assert.Equal(expectedChanges, changes);
            Assert.Equal(0, notifications);

            _world.Reset(deactivate: true);
            Assert.False(_world.IsActive);
            Assert.False(blocker.IsBlocking);
            Assert.False(blocker.BlockageToken.IsValid);
            Assert.Empty(_world.ActiveGrids);
            blocker.RemoveBlockage();
            Assert.Equal(0, notifications);
        }
        finally
        {
            Blocker.OnBlockageApplied -= CountNotification;
            Blocker.OnBlockageRemoved -= CountNotification;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExhaustionDoesNotClaimAnAttachmentRemovedByItsOwnNotification(bool removeInCallback, bool removeVoxel)
    {
        Vector3d max = new(1, 0, 0);
        GridConfiguration configuration = new(Vector3d.Zero, max, storageKind: GridStorageKind.Sparse);
        VoxelIndex[] indices = { new(0, 0, 0), new(1, 0, 0) };
        Assert.True(_world.TryAddGrid(configuration, indices, out ushort slot));
        VoxelGrid grid = _world.ActiveGrids[slot];
        BoundsBlocker blocker = new(_world, Vector3d.Zero, max);
        bool removed = false;
        bool removedVoxel = false;
        _world.OnChangeCommitted += info =>
        {
            if (info.ChangeKind == GridEventKind.ObstacleAdded && removeInCallback)
            {
                removed = grid.TryRemoveObstacle(info.AffectedBoundsMin, blocker.BlockageToken);
                if (removeVoxel)
                    removedVoxel = grid.TryRemoveVoxel(info.VoxelIndex);
            }
        };
        GridWorldTestFactory.SeedPublishedSequence(_world, ulong.MaxValue - (removeVoxel ? 3UL : removeInCallback ? 2UL : 0UL));
        Assert.Throws<OverflowException>(blocker.ApplyBlockage);
        Assert.Equal(removeInCallback, removed);
        Assert.Equal(removeVoxel, removedVoxel);
        Assert.False(blocker.IsBlocking);
        Assert.False(blocker.BlockageToken.IsValid);
        Assert.Equal(0, grid.ObstacleCount);
        blocker.RemoveBlockage();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedApplyRecoversAttachmentsAndResetWatcherAfterReentrantRemoval(bool cache)
    {
        Vector3d max = new(2, 0, 0);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(_world, Vector3d.Zero, max);
        BoundsBlocker blocker = new(_world, Vector3d.Zero, max, cacheCoveredVoxels: cache);
        int additions = 0;
        bool removedSecond = false;
        ObstacleToken expected = default;
        _world.OnChangeCommitted += info =>
        {
            if (info.ChangeKind == GridEventKind.ObstacleAdded && ++additions == 2)
            {
                expected = blocker.BlockageToken;
                blocker.RemoveBlockage();
                removedSecond = grid.TryRemoveObstacle(info.AffectedBoundsMin, expected);
            }
        };
        GridWorldTestFactory.SeedPublishedSequence(_world, ulong.MaxValue - 3);
        Assert.Throws<OverflowException>(blocker.ApplyBlockage);
        Assert.True(removedSecond);
        Assert.Equal(1, grid.ObstacleCount);
        Assert.True(blocker.IsBlocking);
        Assert.Equal(expected, blocker.BlockageToken);
        Assert.Throws<OverflowException>(blocker.RemoveBlockage);
        Assert.Equal(expected, blocker.BlockageToken);

        _world.Dispose();
        Assert.False(blocker.IsBlocking);
        Assert.False(blocker.BlockageToken.IsValid);
    }

    public void Dispose()
    {
        if (_world.IsActive)
            GridWorldTestFactory.SeedPublishedSequence(_world, 1);
        _world.Dispose();
    }
}
