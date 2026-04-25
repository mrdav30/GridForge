using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Tests;
using GridForge.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace GridForge.Blockers.Tests;

[Collection("GridForgeCollection")] // Ensures shared GridForge state is reset per run
public class BlockerTests : IDisposable
{
    private readonly GridWorld _world;

    public BlockerTests()
    {
        _world = GridWorldTestFactory.CreateWorld();
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Blocker_ShouldApplyBlockageToVoxels()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(30, 0, 30), new Vector3d(35, 0, 35)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(33, 0, 33);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
        Assert.NotNull(voxel);
        Assert.False(voxel.IsBlocked); // Ensure voxel is initially unblocked

        BoundingArea boundingArea = new(new Vector3d(32, 0, 32), new Vector3d(34, 0, 34));
        var blocker = new BoundsBlocker(_world, boundingArea);
        blocker.ApplyBlockage();

        Assert.True(voxel.IsBlocked); // Voxel should now be blocked
    }

    [Fact]
    public void Blocker_ShouldRemoveBlockageFromVoxels()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(-35.5, 0, -35.5);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
        Assert.NotNull(voxel);

        BoundingArea boundingArea = new(new Vector3d(-36, 0, -36), new Vector3d(-35, 0, -35));
        var blocker = new BoundsBlocker(_world, boundingArea);
        blocker.ApplyBlockage();
        Assert.True(voxel.IsBlocked);

        blocker.RemoveBlockage();
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void MultipleBlockers_ShouldStackCorrectly()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        var voxelSize = (float)_world.VoxelSize;

        Vector3d position = new(-39 + voxelSize, 0, -39 + voxelSize);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
        Assert.NotNull(voxel);

        BoundingArea boundingArea1 = new(
                new Vector3d(-40, 0, -40),
                new Vector3d(-39 + voxelSize, 0, -39 + voxelSize)
            );
        var blocker1 = new BoundsBlocker(_world, boundingArea1);
        BoundingArea boundingArea2 = new(
                new Vector3d(-39 + voxelSize, 0, -39 + voxelSize),
                new Vector3d(-39, 0, -39)
            );
        var blocker2 = new BoundsBlocker(_world, boundingArea2);

        blocker1.ApplyBlockage();
        blocker2.ApplyBlockage();

        Assert.True(voxel.IsBlocked); // Ensure voxel is blocked
        Assert.True(voxel.ObstacleCount >= 2);
    }

    [Fact]
    public void RemovingOneBlocker_ShouldNotAffectOthers()
    {
        _world.TryAddGrid(new GridConfiguration(
            new Vector3d(-40, 0, -40),
            new Vector3d(-30, 0, -30)),
            out ushort gridIndex);

        BoundingArea boundingArea1 = new(
            new Vector3d(-39, 0, -39),
            new Vector3d(-40, 0, -40));
        BoundingArea boundingArea2 = new(
            new Vector3d(-38.5, 0, -38.5),
            new Vector3d(-39.5, 0, -39.5));

        var blocker1 = new BoundsBlocker(_world, boundingArea1);
        var blocker2 = new BoundsBlocker(_world, boundingArea2);

        blocker1.ApplyBlockage();
        blocker2.ApplyBlockage();

        blocker1.RemoveBlockage();

        Vector3d position = new(-39.4, 0, -39.4);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;

        Assert.False(voxel.IsBlocked); // Should still be blocked because of blocker2
        Assert.False(voxel.ObstacleCount > 0);

        Vector3d position2 = new(-38.4, 0, -38.4);
        Voxel voxel2 = grid.TryGetVoxel(position2, out Voxel n2) ? n2 : null;

        Assert.True(voxel2.IsBlocked); // Should still be blocked because of blocker2
        Assert.True(voxel2.ObstacleCount > 0);
    }

    [Fact]
    public void DeactivatingBlocker_ShouldPreventApplication()
    {
        _world.TryAddGrid(new GridConfiguration(
            new Vector3d(-65, 0, -65),
            new Vector3d(-60, 0, -60)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(-60.5, 0, -60.5);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
        Assert.NotNull(voxel);

        BoundingArea boundingArea = new(new Vector3d(-61, 0, -61), new Vector3d(-60, 0, -60));
        var blocker = new BoundsBlocker(_world, boundingArea, false);
        blocker.ApplyBlockage();

        Assert.False(voxel.IsBlocked); // Should not be blocked due to deactivation
    }

    [Fact]
    public void BoundsBlocker_ShouldAffectCorrectVoxels()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        var blockArea = new BoundingArea(new Vector3d(-40, 0, -40), new Vector3d(-39, 0, -39));
        var boundsBlocker = new BoundsBlocker(_world, blockArea);
        boundsBlocker.ApplyBlockage();

        // Get all blocked voxels using GridTracer
        var blockedVoxels = GridTracer.GetCoveredVoxels(_world, blockArea.Min, blockArea.Max)
                                     .SelectMany(covered => covered.Voxels) // Flatten the grouped voxels
                                     .ToList();

        Assert.NotEmpty(blockedVoxels);
        Assert.All(blockedVoxels, voxel => Assert.True(voxel.IsBlocked));
    }

    [Fact]
    public void Blocker_ShouldCorrectlyAffectEdgeVoxels()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        // Blocker placed at the grid's edge
        BoundingArea boundingArea = new(new Vector3d(-30, 0, -30), new Vector3d(-29.5, 0, -29.5));
        var blocker = new BoundsBlocker(_world, boundingArea);
        blocker.ApplyBlockage();

        var blockedVoxels = GridTracer.GetCoveredVoxels(_world, boundingArea.Min, boundingArea.Max)
                                     .SelectMany(covered => covered.Voxels)
                                     .ToList();

        Assert.NotEmpty(blockedVoxels);
        Assert.All(blockedVoxels, voxel => Assert.True(voxel.IsBlocked));
    }

    [Fact]
    public void Blocker_ShouldApplyAcrossMultipleGrids()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(-40, 0, -40),
            new Vector3d(-30, 0, -30)),
            out ushort grid1);
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(-30, 0, -30),
            new Vector3d(-20, 0, -20)),
            out ushort grid2);

        BoundingArea boundingArea = new(new Vector3d(-31, 0, -31), new Vector3d(-29, 0, -29));
        var blocker = new BoundsBlocker(_world, boundingArea);
        blocker.ApplyBlockage();

        var blockedVoxels = GridTracer.GetCoveredVoxels(_world, boundingArea.Min, boundingArea.Max)
                                     .SelectMany(covered => covered.Voxels)
                                     .ToList();

        Assert.NotEmpty(blockedVoxels);
        Assert.All(blockedVoxels, voxel => Assert.True(voxel.IsBlocked));
    }

    [Fact]
    public void Blockers_ShouldApplyToLocalGridInstance()
    {
        _world.TryAddGrid(new GridConfiguration(
            new Vector3d(100, 0, 100),
            new Vector3d(150, 0, 150)),
            out _);

        List<BoundsBlocker> blockers = new();

        for (int i = 0; i < 10; i++) // Keep it small for unit testing
        {
            Vector3d min = new(100 + i, 0, 100 + i);
            Vector3d max = new(101 + i, 0, 101 + i);
            var blocker = new BoundsBlocker(_world, new BoundingArea(min, max));
            blockers.Add(blocker);
            blocker.ApplyBlockage(); // Modify the explicit local test world
        }

        Assert.True(blockers.All(b => b.IsBlocking), "All blockers should have applied.");
    }

    [Fact]
    public void Blockers_ShouldBeThreadSafe()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-200, 0, -200), new Vector3d(-100, 0, -100)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Parallel.For(0, 100, i =>
        {
            Vector3d min = new(-200 + i, 0, -200 + i);
            Vector3d max = new(-199 + i, 0, -199 + i);
            var blocker = new BoundsBlocker(_world, new BoundingArea(min, max));
            blocker.ApplyBlockage();
        });

        Assert.True(grid.ObstacleCount > 90); // Ensure most blockers applied correctly
    }

    [Fact]
    public void Blocker_ShouldSupportCachedCoveredVoxels()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(5, 0, 5)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d position = new(2, 0, 2);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel foundVoxel) ? foundVoxel : null;
        Assert.NotNull(voxel);

        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(1, 0, 1), new Vector3d(3, 0, 3)),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(voxel.IsBlocked);

        blocker.RemoveBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Blocker_ToggleStatus_ShouldApplyAndRemoveBlockage()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(5, 0, 5)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d position = new(2, 0, 2);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel foundVoxel) ? foundVoxel : null;
        Assert.NotNull(voxel);

        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(1, 0, 1), new Vector3d(3, 0, 3)),
            isActive: false);

        blocker.ToggleStatus(true);
        Assert.True(blocker.IsBlocking);
        Assert.True(voxel.IsBlocked);

        blocker.ToggleStatus(false);
        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Blocker_SetCacheCoveredVoxels_ShouldSupportTransitionsBeforeAndAfterActivation()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel voxel));

        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)),
            cacheCoveredVoxels: false);

        blocker.SetCacheCoveredVoxels(true);
        Assert.True(blocker.CacheCoveredVoxels);

        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(voxel.IsBlocked);

        blocker.SetCacheCoveredVoxels(false);
        Assert.False(blocker.CacheCoveredVoxels);

        blocker.RemoveBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);

        blocker.SetCacheCoveredVoxels(true);
        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(voxel.IsBlocked);
    }

    [Fact]
    public void Blocker_Reset_ShouldRemoveBlockageAndDeactivateFurtherApplication()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel voxel));

        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)));

        blocker.ApplyBlockage();
        Assert.True(blocker.IsBlocking);
        Assert.True(voxel.IsBlocked);

        blocker.Reset();

        Assert.False(blocker.IsActive);
        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);

        blocker.ApplyBlockage();
        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Blocker_Notifications_ShouldContinueNotifyingSubscribersWhenOneThrows()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            out _));

        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)));
        List<BoundsKey> notifications = new();

        void ThrowingApplyHandler(BlockageEventInfo eventInfo)
        {
            throw new InvalidOperationException("subscriber failure");
        }

        void ThrowingRemoveHandler(BlockageEventInfo eventInfo)
        {
            throw new InvalidOperationException("subscriber failure");
        }

        void RecordingApplyHandler(BlockageEventInfo eventInfo)
        {
            notifications.Add(eventInfo.BlockageToken);
        }

        void RecordingRemoveHandler(BlockageEventInfo eventInfo)
        {
            notifications.Add(eventInfo.BlockageToken);
        }

        Blocker.OnBlockageApplied += ThrowingApplyHandler;
        Blocker.OnBlockageApplied += RecordingApplyHandler;
        Blocker.OnBlockageRemoved += ThrowingRemoveHandler;
        Blocker.OnBlockageRemoved += RecordingRemoveHandler;

        try
        {
            blocker.ApplyBlockage();
            blocker.RemoveBlockage();
        }
        finally
        {
            Blocker.OnBlockageApplied -= ThrowingApplyHandler;
            Blocker.OnBlockageApplied -= RecordingApplyHandler;
            Blocker.OnBlockageRemoved -= ThrowingRemoveHandler;
            Blocker.OnBlockageRemoved -= RecordingRemoveHandler;
        }

        Assert.Equal(2, notifications.Count);
        Assert.Equal(notifications[0], notifications[1]);
    }

    [Fact]
    public void Blocker_ShouldReapplyWhenCoveredGridIsRemovedAndReadded()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        BoundingArea area = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        BoundsBlocker blocker = new(_world, area, cacheCoveredVoxels: true);

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));

        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(voxel.IsBlocked);

        Assert.True(_world.TryRemoveGrid(gridIndex));

        Assert.False(blocker.IsBlocking);

        Assert.True(_world.TryAddGrid(config, out ushort readdedIndex));
        VoxelGrid readdedGrid = _world.ActiveGrids[readdedIndex];
        Assert.True(readdedGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel readdedVoxel));

        Assert.True(blocker.IsBlocking);
        Assert.True(readdedVoxel.IsBlocked);
    }

    [Fact]
    public void Blocker_ShouldApplyToNewOverlappingGridAddedAfterInitialApplication()
    {
        GridConfiguration firstConfig = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        GridConfiguration secondConfig = new(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0));
        BoundingArea area = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0));
        BoundsBlocker blocker = new(_world, area);

        Assert.True(_world.TryAddGrid(firstConfig, out ushort firstIndex));
        VoxelGrid firstGrid = _world.ActiveGrids[firstIndex];
        Assert.True(firstGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel firstVoxel));

        blocker.ApplyBlockage();

        Assert.True(firstVoxel.IsBlocked);

        Assert.True(_world.TryAddGrid(secondConfig, out ushort secondIndex));
        VoxelGrid secondGrid = _world.ActiveGrids[secondIndex];
        Assert.True(secondGrid.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel secondVoxel));

        Assert.True(blocker.IsBlocking);
        Assert.True(firstVoxel.IsBlocked);
        Assert.True(secondVoxel.IsBlocked);
    }

    [Fact]
    public void Blocker_ShouldIgnoreEmptyCoverageAndStopWatchingAfterExplicitRemoval()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            out _));

        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(40, 0, 40), new Vector3d(41, 0, 41)));

        blocker.ApplyBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.Equal(default, blocker.BlockageToken);

        blocker.RemoveBlockage();

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(40, 0, 40), new Vector3d(41, 0, 41)),
            out ushort gridIndex));
        VoxelGrid newGrid = _world.ActiveGrids[gridIndex];
        Assert.True(newGrid.TryGetVoxel(new Vector3d(40, 0, 40), out Voxel voxel));

        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Blocker_ShouldReportNotBlockingWhenCoveredVoxelRejectsObstacle()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d position = new(0, 0, 0);
        TestOccupant occupant = new(position);

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));
        Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));

        BoundsBlocker blocker = new(_world, new BoundingArea(position, position));
        blocker.ApplyBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);
        Assert.True(voxel.IsOccupied);
    }

    [Fact]
    public void Blocker_ShouldIgnoreUnrelatedGridAddAndRemovalEvents()
    {
        GridConfiguration watchedGrid = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        GridConfiguration unrelatedGrid = new(new Vector3d(10, 0, 10), new Vector3d(10, 0, 10));
        BoundsBlocker blocker = new(_world, new BoundingArea(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)));

        Assert.True(_world.TryAddGrid(watchedGrid, out ushort watchedIndex));
        VoxelGrid watched = _world.ActiveGrids[watchedIndex];
        Assert.True(watched.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel watchedVoxel));

        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(watchedVoxel.IsBlocked);

        Assert.True(_world.TryAddGrid(unrelatedGrid, out ushort unrelatedIndex));
        Assert.True(_world.TryRemoveGrid(unrelatedIndex));

        Assert.True(blocker.IsBlocking);
        Assert.True(watchedVoxel.IsBlocked);
    }

    [Fact]
    public void Blocker_PrivateGuardPaths_ShouldHandleInactiveBlockersAndNoOpCacheChanges()
    {
        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)),
            isActive: false,
            cacheCoveredVoxels: false);
        GridEventInfo overlappingEvent = new(
            1,
            1,
            10,
            new GridConfiguration(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)),
            1);

        blocker.SetCacheCoveredVoxels(false);

        bool shouldReact = (bool)InvokeBlockerMethod(blocker, "ShouldReactToGridAdded", overlappingEvent);
        bool shouldReactToRemovedGrid = (bool)InvokeBlockerMethod(blocker, "ShouldReactToGridRemoved", overlappingEvent);
        InvokeBlockerMethod(blocker, "ReapplyBlockage");
        InvokeBlockerMethod(blocker, "RegisterGridWatcher");
        _world.Reset();

        Assert.False(shouldReact);
        Assert.False(shouldReactToRemovedGrid);
        Assert.False(blocker.IsActive);
        Assert.False(blocker.IsBlocking);
        Assert.Equal(default, blocker.BlockageToken);
    }

    [Fact]
    public void Blocker_ShouldFallbackToRetracingWhenCachedCoveredVoxelIdsAreCleared()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));

        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(voxel.IsBlocked);

        object cachedCoveredVoxels = typeof(Blocker)
            .GetField("_cachedCoveredVoxels", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(blocker)
            ?? throw new InvalidOperationException("Could not find Blocker._cachedCoveredVoxels.");
        cachedCoveredVoxels.GetType().GetMethod("Clear")
            ?.Invoke(cachedCoveredVoxels, Array.Empty<object>());

        blocker.RemoveBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Blocker_ShouldFallbackToRetracingWhenCachedCoveredVoxelSetIsMissing()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));

        BoundsBlocker blocker = new(
            _world,
            new BoundingArea(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();
        Assert.True(voxel.IsBlocked);

        FieldInfo cachedCoveredVoxelsField = typeof(Blocker).GetField(
            "_cachedCoveredVoxels",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find Blocker._cachedCoveredVoxels.");
        cachedCoveredVoxelsField.SetValue(blocker, null);

        blocker.RemoveBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);
    }

    private static object InvokeBlockerMethod(Blocker blocker, string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(Blocker).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find Blocker.{methodName}.");

        return method.Invoke(blocker, arguments);
    }
}
