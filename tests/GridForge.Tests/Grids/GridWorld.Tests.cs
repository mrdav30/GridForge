using FixedMathSharp;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Spatial;
using GridForge.Utility;
using System;
using System.Linq;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class GridWorldTests
{
    [Fact]
    public void TryAddGrid_ShouldNormalizeBoundsUsingOwningWorldVoxelSize()
    {
        GridConfiguration rawConfiguration = new(
            new Vector3d(-1.25, 0, -1.25),
            new Vector3d(1.25, 0, 1.25),
            scanCellSize: 4);

        Assert.Equal(new Vector3d(-1.25, 0, -1.25), rawConfiguration.BoundsMin);
        Assert.Equal(new Vector3d(1.25, 0, 1.25), rawConfiguration.BoundsMax);

        using GridWorld world = GridWorldTestFactory.CreateWorld((Fixed64)0.5, spatialGridCellSize: 32);

        Assert.True(world.TryAddGrid(rawConfiguration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.Equal(new Vector3d(-1, 0, -1), grid.BoundsMin);
        Assert.Equal(new Vector3d(1.5, 0, 1.5), grid.BoundsMax);
        Assert.Equal(rawConfiguration.ScanCellSize, grid.Configuration.ScanCellSize);
    }

    [Fact]
    public void TraceLine_ShouldOnlyReturnGridsFromSpecifiedWorld()
    {
        using GridWorld firstWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld secondWorld = GridWorldTestFactory.CreateWorld();
        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(
            firstWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 0));
        VoxelGrid secondGrid = GridWorldTestFactory.AddGrid(
            secondWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 0));

        GridVoxelSet[] tracedSets = GridTracer.TraceLine(
            firstWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 0),
            includeEnd: true).ToArray();

        Assert.Single(tracedSets);
        Assert.Equal(firstGrid.GridIndex, tracedSets[0].Grid.GridIndex);
        Assert.Equal(firstWorld.SpawnToken, tracedSets[0].Grid.World!.SpawnToken);
        Assert.NotEqual(secondGrid.World!.SpawnToken, tracedSets[0].Grid.World!.SpawnToken);
    }

    [Fact]
    public void OccupantTrackingAndScan_ShouldStayInsideExplicitWorld()
    {
        using GridWorld firstWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld secondWorld = GridWorldTestFactory.CreateWorld();
        Guid sharedId = Guid.NewGuid();

        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(
            firstWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2));
        VoxelGrid secondGrid = GridWorldTestFactory.AddGrid(
            secondWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2));
        SharedIdOccupant firstOccupant = new(sharedId, new Vector3d(1, 0, 1), 3);
        SharedIdOccupant secondOccupant = new(sharedId, new Vector3d(1, 0, 1), 3);

        Assert.True(GridOccupantManager.TryRegister(firstWorld, firstOccupant));
        Assert.True(GridOccupantManager.TryRegister(secondWorld, secondOccupant));
        Assert.True(firstGrid.TryGetVoxel(firstOccupant.Position, out Voxel firstVoxel));
        Assert.True(secondGrid.TryGetVoxel(secondOccupant.Position, out Voxel secondVoxel));

        Assert.True(GridOccupantManager.TryGetOccupancyTicket(firstWorld, firstOccupant, firstVoxel.WorldIndex, out int firstTicket));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(secondWorld, secondOccupant, secondVoxel.WorldIndex, out int secondTicket));
        Assert.False(GridOccupantManager.TryGetOccupancyTicket(firstWorld, secondOccupant, secondVoxel.WorldIndex, out _));

        Assert.Same(firstOccupant, GridScanManager.ScanRadius(firstWorld, new Vector3d(1, 0, 1), Fixed64.One).Single());
        Assert.Same(secondOccupant, GridScanManager.ScanRadius(secondWorld, new Vector3d(1, 0, 1), Fixed64.One).Single());
        Assert.True(GridScanManager.TryGetVoxelOccupant(firstWorld, firstVoxel.WorldIndex, firstTicket, out IVoxelOccupant resolvedFirst));
        Assert.True(GridScanManager.TryGetVoxelOccupant(secondWorld, secondVoxel.WorldIndex, secondTicket, out IVoxelOccupant resolvedSecond));
        Assert.Same(firstOccupant, resolvedFirst);
        Assert.Same(secondOccupant, resolvedSecond);
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(firstWorld, secondOccupant));
    }

    [Fact]
    public void Blocker_ShouldIgnoreGridChangesFromOtherWorlds()
    {
        using GridWorld blockerWorld = GridWorldTestFactory.CreateWorld();
        using GridWorld otherWorld = GridWorldTestFactory.CreateWorld();
        BoundingArea area = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        BoundsBlocker blocker = new(blockerWorld, area, cacheCoveredVoxels: true);

        blocker.ApplyBlockage();
        Assert.False(blocker.IsBlocking);

        VoxelGrid otherGrid = GridWorldTestFactory.AddGrid(
            otherWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        Assert.True(otherGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel otherVoxel));

        Assert.False(blocker.IsBlocking);
        Assert.False(otherVoxel.IsBlocked);

        VoxelGrid blockerGrid = GridWorldTestFactory.AddGrid(
            blockerWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        Assert.True(blockerGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel blockerVoxel));

        Assert.True(blocker.IsBlocking);
        Assert.True(blockerVoxel.IsBlocked);
        Assert.False(otherVoxel.IsBlocked);
    }

    [Fact]
    public void DisposedWorld_ShouldInvalidateStaleWorldVoxelIndices()
    {
        WorldVoxelIndex staleIndex;

        using (GridWorld originalWorld = GridWorldTestFactory.CreateWorld())
        {
            VoxelGrid originalGrid = GridWorldTestFactory.AddGrid(
                originalWorld,
                new Vector3d(0, 0, 0),
                new Vector3d(0, 0, 0));
            Assert.True(originalGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel originalVoxel));
            staleIndex = originalVoxel.WorldIndex;
        }

        using GridWorld replacementWorld = GridWorldTestFactory.CreateWorld();
        VoxelGrid replacementGrid = GridWorldTestFactory.AddGrid(
            replacementWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        Assert.True(replacementGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel replacementVoxel));

        Assert.False(replacementWorld.TryGetGrid(staleIndex, out _));
        Assert.False(replacementWorld.TryGetVoxel(staleIndex, out _));
        Assert.False(replacementWorld.TryGetGridAndVoxel(staleIndex, out _, out _));
        Assert.NotEqual(staleIndex.WorldSpawnToken, replacementVoxel.WorldIndex.WorldSpawnToken);
    }

    private sealed class SharedIdOccupant : IVoxelOccupant
    {
        public Guid GlobalId { get; }

        public byte OccupantGroupId { get; }

        public Vector3d Position { get; set; }

        public SharedIdOccupant(Guid globalId, Vector3d position, byte occupantGroupId)
        {
            GlobalId = globalId;
            Position = position;
            OccupantGroupId = occupantGroupId;
        }
    }
}
