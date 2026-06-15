//=======================================================================
// GridDiagnosticSessionTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Tests;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using Xunit;

namespace GridForge.Diagnostics.Tests;

[Collection("GridForgeCollection")]
public class GridDiagnosticSessionTests
{
    [Fact]
    public void Session_ShouldCaptureGridAddRemoveAndResetChanges()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        using GridDiagnosticSession session = new(world);
        SwiftList<GridDiagnosticChange> changes = new();

        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));

        Assert.Equal(1, session.GetDirtyChangesInto(changes));
        AssertGridChange(changes[0], grid, GridDiagnosticChangeKind.GridAdded);

        int removedWorldToken = world.SpawnToken;
        ushort removedGridIndex = grid.GridIndex;
        int removedGridSpawnToken = grid.SpawnToken;
        Vector3d removedBoundsMin = grid.BoundsMin;
        Vector3d removedBoundsMax = grid.BoundsMax;
        session.ClearDirtyChanges();
        Assert.True(world.TryRemoveGrid(grid.GridIndex));

        Assert.Equal(1, session.GetDirtyChangesInto(changes));
        AssertGridChange(
            changes[0],
            removedWorldToken,
            removedGridIndex,
            removedGridSpawnToken,
            removedBoundsMin,
            removedBoundsMax,
            GridDiagnosticChangeKind.GridRemoved);

        VoxelGrid resetGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(4, 0, 0),
            new Vector3d(4, 0, 0));
        session.ClearDirtyChanges();

        world.Reset();

        Assert.Equal(1, session.GetDirtyChangesInto(changes));
        GridDiagnosticChange resetChange = changes[0];
        Assert.True((resetChange.Kind & GridDiagnosticChangeKind.WorldReset) != 0);
        Assert.Equal(world.SpawnToken, resetChange.WorldSpawnToken);
        Assert.Equal(ushort.MaxValue, resetChange.GridIndex);
        Assert.Equal(default, resetChange.WorldIndex);
        Assert.False(world.ActiveGrids.IsAllocated(resetGrid.GridIndex));
    }

    [Fact]
    public void Session_WorldReset_ShouldSupersedePendingChanges()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        using GridDiagnosticSession session = new(world);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
        Assert.True(grid.TryAddObstacle(voxel, new BoundsKey(voxel.WorldPosition, voxel.WorldPosition)));
        SwiftList<GridDiagnosticChange> changes = new();

        world.Reset();

        Assert.Equal(1, session.GetDirtyChangesInto(changes));
        Assert.True((changes[0].Kind & GridDiagnosticChangeKind.WorldReset) != 0);
    }

    [Fact]
    public void Session_ShouldCaptureObstacleCellChangesAndBroadGridChange()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
        using GridDiagnosticSession session = new(world);
        BoundsKey obstacleToken = new(voxel.WorldPosition, voxel.WorldPosition);
        SwiftList<GridDiagnosticChange> changes = new();

        Assert.True(grid.TryAddObstacle(voxel, obstacleToken));
        Assert.True(grid.TryRemoveObstacle(voxel, obstacleToken));

        session.GetDirtyChangesInto(changes);

        GridDiagnosticChange cellChange = Assert.Single(changes, change => change.WorldIndex == voxel.WorldIndex);
        Assert.True((cellChange.Kind & GridDiagnosticChangeKind.ObstacleChanged) != 0);
        Assert.Equal(voxel.Index, cellChange.VoxelIndex);

        GridDiagnosticChange gridChange = Assert.Single(changes, change => change.WorldIndex == default);
        AssertGridChange(gridChange, grid, GridDiagnosticChangeKind.GridChanged);
    }

    [Fact]
    public void Session_ShouldCaptureOccupantChangesWithoutGridVersionEventsAndSortCells()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel firstVoxel));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel secondVoxel));
        using GridDiagnosticSession session = new(world);
        TestOccupant firstOccupant = new(firstVoxel.WorldPosition);
        TestOccupant secondOccupant = new(secondVoxel.WorldPosition);
        SwiftList<GridDiagnosticChange> changes = new();

        Assert.True(grid.TryAddVoxelOccupant(secondVoxel, secondOccupant));
        Assert.True(grid.TryRemoveVoxelOccupant(secondVoxel, secondOccupant));
        Assert.True(grid.TryAddVoxelOccupant(firstVoxel, firstOccupant));

        Assert.Equal(2, session.GetDirtyChangesInto(changes));
        Assert.Equal(firstVoxel.WorldIndex, changes[0].WorldIndex);
        Assert.Equal(secondVoxel.WorldIndex, changes[1].WorldIndex);
        Assert.All(changes, change =>
        {
            Assert.True((change.Kind & GridDiagnosticChangeKind.OccupantChanged) != 0);
            Assert.True((change.Kind & GridDiagnosticChangeKind.GridChanged) == 0);
        });
    }

    [Fact]
    public void Session_ShouldCaptureSparseVoxelMutationAndAddressRangeChanges()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = AddSparseGrid(world);
        using GridDiagnosticSession session = new(world);
        VoxelIndex index = new(1, 0, 1);
        SwiftList<GridDiagnosticChange> changes = new();

        Assert.True(grid.TryAddVoxel(index, out Voxel addedVoxel));

        session.GetDirtyChangesInto(changes);

        GridDiagnosticChange addCellChange = Assert.Single(changes, change => change.WorldIndex == addedVoxel.WorldIndex);
        Assert.True((addCellChange.Kind & GridDiagnosticChangeKind.SparseVoxelAdded) != 0);

        GridDiagnosticChange addRangeChange = Assert.Single(changes, change => (change.Kind & GridDiagnosticChangeKind.SparseAddressChanged) != 0);
        AssertAddressRangeChange(addRangeChange, grid, index, addedVoxel.WorldPosition);

        session.ClearDirtyChanges();
        Assert.True(grid.TryRemoveVoxel(index));

        session.GetDirtyChangesInto(changes);

        GridDiagnosticChange removeCellChange = Assert.Single(changes, change => change.VoxelIndex == index && (change.Kind & GridDiagnosticChangeKind.SparseVoxelRemoved) != 0);
        Assert.Equal(new WorldVoxelIndex(world.SpawnToken, grid.GridIndex, grid.SpawnToken, index), removeCellChange.WorldIndex);

        GridDiagnosticChange removeRangeChange = Assert.Single(changes, change => (change.Kind & GridDiagnosticChangeKind.SparseAddressChanged) != 0);
        AssertAddressRangeChange(removeRangeChange, grid, index, new Vector3d(1, 0, 1));
    }

    [Fact]
    public void Session_ShouldIgnoreEventsFromOtherWorlds()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid localGrid = GridWorldTestFactory.AddGrid(world, new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        using GridDiagnosticSession session = new(world);
        using GridWorld otherWorld = GridWorldTestFactory.CreateWorld();
        VoxelGrid otherGrid = GridWorldTestFactory.AddGrid(otherWorld, new Vector3d(10, 0, 0), new Vector3d(10, 0, 0));
        Assert.True(otherGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel otherVoxel));
        SwiftList<GridDiagnosticChange> changes = new();

        BoundsKey otherObstacle = new(otherVoxel.WorldPosition, otherVoxel.WorldPosition);
        Assert.True(otherGrid.TryAddObstacle(otherVoxel, otherObstacle));
        Assert.True(otherGrid.TryRemoveObstacle(otherVoxel, otherObstacle));
        Assert.True(otherGrid.TryAddVoxelOccupant(otherVoxel, new TestOccupant(otherVoxel.WorldPosition)));

        Assert.Equal(0, session.GetDirtyChangesInto(changes));
        Assert.Empty(changes);
        Assert.True(localGrid.IsActive);
    }

    [Fact]
    public void Session_Dispose_ShouldPreventFurtherDirtyCaptures()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));
        GridDiagnosticSession session = new(world);
        SwiftList<GridDiagnosticChange> changes = new();

        session.Dispose();
        Assert.True(grid.TryAddObstacle(voxel, new BoundsKey(voxel.WorldPosition, voxel.WorldPosition)));

        Assert.Equal(0, session.GetDirtyChangesInto(changes));
        Assert.Empty(changes);
    }

    private static VoxelGrid AddSparseGrid(GridWorld world)
    {
        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);

        Assert.True(world.TryAddGrid(configuration, new[] { new VoxelIndex(0, 0, 0) }, out ushort gridIndex));
        return world.ActiveGrids[gridIndex];
    }

    private static void AssertGridChange(
        GridDiagnosticChange change,
        VoxelGrid grid,
        GridDiagnosticChangeKind expectedKind) =>
        AssertGridChange(
            change,
            grid.World!.SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            grid.BoundsMin,
            grid.BoundsMax,
            expectedKind);

    private static void AssertGridChange(
        GridDiagnosticChange change,
        int expectedWorldSpawnToken,
        ushort expectedGridIndex,
        int expectedGridSpawnToken,
        Vector3d expectedBoundsMin,
        Vector3d expectedBoundsMax,
        GridDiagnosticChangeKind expectedKind)
    {
        Assert.True((change.Kind & expectedKind) != 0);
        Assert.Equal(expectedWorldSpawnToken, change.WorldSpawnToken);
        Assert.Equal(expectedGridIndex, change.GridIndex);
        Assert.Equal(expectedGridSpawnToken, change.GridSpawnToken);
        Assert.Equal(default, change.WorldIndex);
        Assert.Equal(expectedBoundsMin, change.BoundsMin);
        Assert.Equal(expectedBoundsMax, change.BoundsMax);
    }

    private static void AssertAddressRangeChange(
        GridDiagnosticChange change,
        VoxelGrid grid,
        VoxelIndex expectedIndex,
        Vector3d expectedCenter)
    {
        Assert.True((change.Kind & GridDiagnosticChangeKind.SparseAddressChanged) != 0);
        Assert.Equal(grid.World!.SpawnToken, change.WorldSpawnToken);
        Assert.Equal(grid.GridIndex, change.GridIndex);
        Assert.Equal(grid.SpawnToken, change.GridSpawnToken);
        Assert.Equal(expectedIndex, change.VoxelIndex);
        Assert.Equal(default, change.WorldIndex);
        Assert.True(change.BoundsMin.X < expectedCenter.X);
        Assert.True(change.BoundsMin.Y < expectedCenter.Y);
        Assert.True(change.BoundsMin.Z < expectedCenter.Z);
        Assert.True(change.BoundsMax.X > expectedCenter.X);
        Assert.True(change.BoundsMax.Y > expectedCenter.Y);
        Assert.True(change.BoundsMax.Z > expectedCenter.Z);
    }
}
