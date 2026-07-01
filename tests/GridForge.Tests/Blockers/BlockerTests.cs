using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Tests;
using GridForge.Grids.Topology;
using GridForge.Spatial;
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

        FixedBoundBox boundingBox = FixedBoundBox.FromMinMax(new Vector3d(32, 0, 32), new Vector3d(34, 0, 34));
        var blocker = new BoundsBlocker(_world, boundingBox);
        blocker.ApplyBlockage();

        Assert.True(voxel.IsBlocked); // Voxel should now be blocked
    }

    [Fact]
    public void BoundsBlocker_ShouldAffectConfiguredSparseVoxelsOnly()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4));
        VoxelIndex[] configuredVoxels =
        {
            new(1, 0, 1),
            new(3, 0, 3),
        };

        Assert.True(_world.TryAddGrid(config, configuredVoxels, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 1), out Voxel firstVoxel));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(3, 0, 3), out Voxel secondVoxel));
        Assert.False(grid.TryGetVoxel(new VoxelIndex(2, 0, 2), out _));

        BoundsBlocker blocker = new(
            _world,
            FixedBoundBox.FromMinMax(new Vector3d(0, 0, 0), new Vector3d(4, 0, 4)),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();

        Assert.True(firstVoxel.IsBlocked);
        Assert.True(secondVoxel.IsBlocked);
        Assert.Equal(2, grid.ObstacleCount);

        blocker.RemoveBlockage();

        Assert.False(firstVoxel.IsBlocked);
        Assert.False(secondVoxel.IsBlocked);
        Assert.Equal(0, grid.ObstacleCount);
    }

    [Fact]
    public void BoundsBlocker_ShouldReapplyCachedSparseCoverageAfterGridReload()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        FixedBoundBox bounds = FixedBoundBox.FromMinMax(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        VoxelIndex[] configuredVoxels = { new(1, 0, 1) };

        Assert.True(_world.TryAddGrid(config, configuredVoxels, out ushort firstGridIndex));
        VoxelGrid firstGrid = _world.ActiveGrids[firstGridIndex];
        Assert.True(firstGrid.TryGetVoxel(new VoxelIndex(1, 0, 1), out Voxel firstVoxel));

        BoundsBlocker blocker = new(_world, bounds, cacheCoveredVoxels: true);
        blocker.ApplyBlockage();

        Assert.True(firstVoxel.IsBlocked);
        Assert.True(_world.TryRemoveGrid(firstGridIndex));
        Assert.False(firstVoxel.IsAllocated);

        Assert.True(_world.TryAddGrid(config, configuredVoxels, out ushort secondGridIndex));
        VoxelGrid secondGrid = _world.ActiveGrids[secondGridIndex];
        Assert.True(secondGrid.TryGetVoxel(new VoxelIndex(1, 0, 1), out Voxel secondVoxel));

        Assert.True(secondVoxel.IsBlocked);

        blocker.RemoveBlockage();

        Assert.False(secondVoxel.IsBlocked);
    }

    [Fact]
    public void BoundsBlocker_ShouldApplyToRuntimeAddedSparseVoxelInsideActiveBounds()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2));
        VoxelIndex coveredIndex = new(1, 0, 1);

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        BoundsBlocker blocker = new(
            _world,
            FixedBoundBox.FromMinMax(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();

        Assert.False(blocker.IsBlocking);

        Assert.True(grid.TryAddVoxel(coveredIndex, out Voxel addedVoxel));

        Assert.True(blocker.IsBlocking);
        Assert.True(addedVoxel.IsBlocked);
        Assert.Equal(1, grid.ObstacleCount);
        Assert.False(grid.TryRemoveVoxel(coveredIndex));

        blocker.RemoveBlockage();

        Assert.False(addedVoxel.IsBlocked);
        Assert.True(grid.TryRemoveVoxel(coveredIndex));
    }

    [Fact]
    public void BoundsBlocker_ShouldApplyToConfiguredAndRuntimeAddedSparseHexVoxels()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            Fixed64.One,
            HexOrientation.PointyTop);
        GridConfiguration config = CreateSparseHexConfig(metrics, new VoxelIndex(1, 0, 1));
        VoxelIndex originIndex = new(0, 0, 0);
        VoxelIndex eastIndex = new(1, 0, 0);

        Assert.True(_world.TryAddGrid(config, new[] { originIndex }, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(originIndex, out Voxel origin));
        Assert.False(grid.TryGetVoxel(eastIndex, out _));

        Vector3d outsideHexFootprint = new(grid.BoundsMax.X, grid.BoundsMin.Y, grid.BoundsMin.Z);
        BoundsBlocker blocker = new(
            _world,
            FixedBoundBox.FromMinMax(grid.BoundsMin, outsideHexFootprint),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(origin.IsBlocked);
        Assert.Equal(1, grid.ObstacleCount);

        Assert.True(grid.TryAddVoxel(eastIndex, out Voxel east));

        Assert.True(east.IsBlocked);
        Assert.Equal(2, grid.ObstacleCount);
        Assert.False(grid.TryRemoveVoxel(eastIndex));

        blocker.RemoveBlockage();

        Assert.False(origin.IsBlocked);
        Assert.False(east.IsBlocked);
        Assert.Equal(0, grid.ObstacleCount);
        Assert.True(grid.TryRemoveVoxel(eastIndex));
    }

    [Fact]
    public void Blocker_ShouldRemoveBlockageFromVoxels()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = Vector3d.FromDouble(-35.5, 0, -35.5);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
        Assert.NotNull(voxel);

        FixedBoundBox boundingBox = FixedBoundBox.FromMinMax(new Vector3d(-36, 0, -36), new Vector3d(-35, 0, -35));
        var blocker = new BoundsBlocker(_world, boundingBox);
        blocker.ApplyBlockage();
        Assert.True(voxel.IsBlocked);

        blocker.RemoveBlockage();
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Blocker_RemoveBeforeApply_ShouldNoOpWithoutRegisteredWatcher()
    {
        BoundsBlocker blocker = new(
            _world,
            FixedBoundBox.FromMinMax(Vector3d.Zero, Vector3d.Zero));

        blocker.RemoveBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.True(blocker.IsActive);
        Assert.Equal(default, blocker.BlockageToken);
    }

    [Fact]
    public void MultipleBlockers_ShouldStackCorrectly()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        var voxelSize = (float)grid.Configuration.TopologyMetrics.CellWidth;

        Vector3d position = Vector3d.FromDouble(-39 + voxelSize, 0, -39 + voxelSize);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
        Assert.NotNull(voxel);

        FixedBoundBox boundingBox1 = FixedBoundBox.FromMinMax(
                new Vector3d(-40, 0, -40),
                Vector3d.FromDouble(-39 + voxelSize, 0, -39 + voxelSize)
            );
        var blocker1 = new BoundsBlocker(_world, boundingBox1);
        FixedBoundBox boundingBox2 = FixedBoundBox.FromMinMax(
                Vector3d.FromDouble(-39 + voxelSize, 0, -39 + voxelSize),
                new Vector3d(-39, 0, -39)
            );
        var blocker2 = new BoundsBlocker(_world, boundingBox2);

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

        FixedBoundBox boundingBox1 = FixedBoundBox.FromMinMax(
            new Vector3d(-39, 0, -39),
            new Vector3d(-40, 0, -40));
        FixedBoundBox boundingBox2 = FixedBoundBox.FromMinMax(
            Vector3d.FromDouble(-38.5, 0, -38.5),
            Vector3d.FromDouble(-39.5, 0, -39.5));

        var blocker1 = new BoundsBlocker(_world, boundingBox1);
        var blocker2 = new BoundsBlocker(_world, boundingBox2);

        blocker1.ApplyBlockage();
        blocker2.ApplyBlockage();

        blocker1.RemoveBlockage();

        Vector3d position = Vector3d.FromDouble(-39.4, 0, -39.4);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;

        Assert.True(voxel.IsBlocked); // Should still be blocked because of blocker2
        Assert.True(voxel.ObstacleCount > 0);

        Vector3d position2 = Vector3d.FromDouble(-38.4, 0, -38.4);
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

        Vector3d position = Vector3d.FromDouble(-60.5, 0, -60.5);
        Voxel voxel = grid.TryGetVoxel(position, out Voxel n) ? n : null;
        Assert.NotNull(voxel);

        FixedBoundBox boundingBox = FixedBoundBox.FromMinMax(new Vector3d(-61, 0, -61), new Vector3d(-60, 0, -60));
        var blocker = new BoundsBlocker(_world, boundingBox, false);
        blocker.ApplyBlockage();

        Assert.False(voxel.IsBlocked); // Should not be blocked due to deactivation
    }

    [Fact]
    public void BoundsBlocker_ShouldAffectCorrectVoxels()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-40, 0, -40), new Vector3d(-30, 0, -30)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        FixedBoundBox blockBox = FixedBoundBox.FromMinMax(new Vector3d(-40, 0, -40), new Vector3d(-39, 0, -39));
        var boundsBlocker = new BoundsBlocker(_world, blockBox);
        boundsBlocker.ApplyBlockage();

        // Get all blocked voxels using GridTracer
        var blockedVoxels = GridTracer.GetCoveredVoxels(_world, blockBox.Min, blockBox.Max)
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
        FixedBoundBox boundingArea = FixedBoundBox.FromMinMax(new Vector3d(-30, 0, -30), Vector3d.FromDouble(-29.5, 0, -29.5));
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

        FixedBoundBox boundingBox = FixedBoundBox.FromMinMax(new Vector3d(-31, 0, -31), new Vector3d(-29, 0, -29));
        var blocker = new BoundsBlocker(_world, boundingBox);
        blocker.ApplyBlockage();

        var blockedVoxels = GridTracer.GetCoveredVoxels(_world, boundingBox.Min, boundingBox.Max)
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
            var blocker = new BoundsBlocker(_world, FixedBoundBox.FromMinMax(min, max));
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
            var blocker = new BoundsBlocker(_world, FixedBoundBox.FromMinMax(min, max));
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
            FixedBoundBox.FromMinMax(new Vector3d(1, 0, 1), new Vector3d(3, 0, 3)),
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
            FixedBoundBox.FromMinMax(new Vector3d(1, 0, 1), new Vector3d(3, 0, 3)),
            isActive: false);

        blocker.ToggleStatus(true);
        Assert.True(blocker.IsBlocking);
        Assert.True(voxel.IsBlocked);

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
            FixedBoundBox.FromMinMax(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)),
            cacheCoveredVoxels: false);

        blocker.SetCacheCoveredVoxels(true);
        Assert.True(blocker.CacheCoveredVoxels);

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
            FixedBoundBox.FromMinMax(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)));

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
            FixedBoundBox.FromMinMax(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)));
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
        FixedBoundBox box = FixedBoundBox.FromMinMax(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        BoundsBlocker blocker = new(_world, box, cacheCoveredVoxels: true);

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
        FixedBoundBox box = FixedBoundBox.FromMinMax(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0));
        BoundsBlocker blocker = new(_world, box);

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
            FixedBoundBox.FromMinMax(new Vector3d(40, 0, 40), new Vector3d(41, 0, 41)));

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

        BoundsBlocker blocker = new(_world, FixedBoundBox.FromMinMax(position, position));
        blocker.ApplyBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.False(voxel.IsBlocked);
        Assert.True(voxel.IsOccupied);
    }

    [Fact]
    public void Blocker_ShouldRollbackAppliedVoxelsWhenCoveragePartiallyRejectsObstacle()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0)),
            out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel freeVoxel));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel occupiedVoxel));
        Assert.True(grid.TryAddVoxelOccupant(occupiedVoxel, new TestOccupant(occupiedVoxel.WorldPosition)));

        BoundsBlocker blocker = new(
            _world,
            FixedBoundBox.FromMinMax(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0)),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.False(freeVoxel.IsBlocked);
        Assert.False(occupiedVoxel.IsBlocked);
        Assert.Equal(0, grid.ObstacleCount);
    }

    [Fact]
    public void Blocker_ShouldIgnoreUnrelatedGridAddAndRemovalEvents()
    {
        GridConfiguration watchedGrid = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        GridConfiguration unrelatedGrid = new(new Vector3d(10, 0, 10), new Vector3d(10, 0, 10));
        BoundsBlocker blocker = new(_world, FixedBoundBox.FromMinMax(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)));

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
            FixedBoundBox.FromMinMax(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1)),
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
        InvokeBlockerMethod(blocker, "HandleActiveGridAdded", overlappingEvent);
        InvokeBlockerMethod(blocker, "HandleActiveGridRemoved", overlappingEvent);
        InvokeBlockerMethod(blocker, "HandleActiveGridChanged", overlappingEvent);
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
            FixedBoundBox.FromMinMax(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
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
            FixedBoundBox.FromMinMax(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
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

    [Fact]
    public void BoundsBlocker_ShouldApplyAndRemoveCachedHexCoverageWhenAabbCornerIsOutsideHexFootprint()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            Fixed64.One,
            HexOrientation.PointyTop);
        GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1));

        Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel origin));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel east));

        Vector3d outsideHexFootprint = new(grid.BoundsMax.X, grid.BoundsMin.Y, grid.BoundsMin.Z);
        BoundsBlocker blocker = new(
            _world,
            FixedBoundBox.FromMinMax(grid.BoundsMin, outsideHexFootprint),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(origin.IsBlocked);
        Assert.True(east.IsBlocked);

        blocker.RemoveBlockage();

        Assert.False(blocker.IsBlocking);
        Assert.False(origin.IsBlocked);
        Assert.False(east.IsBlocked);
    }

    [Fact]
    public void BoundsBlocker_ShouldApplyAcrossRectangularAndHexGridsInSameWorld()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(new Fixed64(2), Fixed64.One);
        GridConfiguration rectangularConfiguration = new(
            new Vector3d(-3, 0, 0),
            new Vector3d(-1, 0, 0));
        GridConfiguration hexConfiguration = CreateHexConfiguration(
            new Vector3d(0, 0, 0),
            metrics,
            new VoxelIndex(1, 0, 0));

        Assert.True(_world.TryAddGrid(rectangularConfiguration, out ushort rectangularGridIndex));
        Assert.True(_world.TryAddGrid(hexConfiguration, out ushort hexGridIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularGridIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexGridIndex];
        Assert.True(rectangularGrid.TryGetVoxel(new Vector3d(-2, 0, 0), out Voxel rectangularVoxel));
        Assert.True(hexGrid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel hexVoxel));

        Vector3d boundsMax = hexGrid.GetWorldPosition(new VoxelIndex(1, 0, 0));
        BoundsBlocker blocker = new(
            _world,
            FixedBoundBox.FromMinMax(new Vector3d(-2, 0, 0), boundsMax),
            cacheCoveredVoxels: true);

        blocker.ApplyBlockage();

        Assert.True(blocker.IsBlocking);
        Assert.True(rectangularVoxel.IsBlocked);
        Assert.True(hexVoxel.IsBlocked);

        blocker.RemoveBlockage();

        Assert.False(rectangularVoxel.IsBlocked);
        Assert.False(hexVoxel.IsBlocked);
    }

    private static object InvokeBlockerMethod(Blocker blocker, string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(Blocker).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find Blocker.{methodName}.");

        return method.Invoke(blocker, arguments);
    }

    private static GridConfiguration CreateSparseConfig(Vector3d min, Vector3d max) =>
        new(min, max, storageKind: GridStorageKind.Sparse);

    private static GridConfiguration CreateSparseHexConfig(
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        int scanCellSize = GridConfiguration.DefaultScanCellSize)
    {
        Vector3d boundsMax = HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            Vector3d.Zero,
            boundsMax,
            scanCellSize,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
    }

    private static GridConfiguration CreateHexConfiguration(
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        int scanCellSize = GridConfiguration.DefaultScanCellSize) =>
        CreateHexConfiguration(Vector3d.Zero, metrics, maxIndex, scanCellSize);

    private static GridConfiguration CreateHexConfiguration(
        Vector3d boundsMin,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        int scanCellSize = GridConfiguration.DefaultScanCellSize)
    {
        Vector3d boundsMax = boundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            boundsMin,
            boundsMax,
            scanCellSize,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);
    }
}
