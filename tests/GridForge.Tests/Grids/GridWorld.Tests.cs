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

        using GridWorld world = new((Fixed64)0.5, spatialGridCellSize: 32);

        Assert.True(world.TryAddGrid(rawConfiguration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.Equal(new Vector3d(-1, 0, -1), grid.BoundsMin);
        Assert.Equal(new Vector3d(1.5, 0, 1.5), grid.BoundsMax);
        Assert.Equal(rawConfiguration.ScanCellSize, grid.Configuration.ScanCellSize);
    }

    [Fact]
    public void TraceLine_ShouldOnlyReturnGridsFromSpecifiedWorld()
    {
        using GridWorld firstWorld = new();
        using GridWorld secondWorld = new();
        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 0));

        Assert.True(firstWorld.TryAddGrid(configuration, out ushort firstIndex));
        Assert.True(secondWorld.TryAddGrid(configuration, out ushort secondIndex));

        GridVoxelSet[] tracedSets = GridTracer.TraceLine(
            firstWorld,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 0),
            includeEnd: true).ToArray();

        Assert.Single(tracedSets);
        Assert.Equal(firstIndex, tracedSets[0].Grid.GridIndex);
        Assert.Equal(firstWorld.SpawnToken, tracedSets[0].Grid.World!.SpawnToken);
        Assert.NotEqual(secondWorld.SpawnToken, tracedSets[0].Grid.World!.SpawnToken);
    }

    [Fact]
    public void OccupantTrackingAndScan_ShouldStayInsideExplicitWorld()
    {
        using GridWorld firstWorld = new();
        using GridWorld secondWorld = new();
        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2));
        Guid sharedId = Guid.NewGuid();

        Assert.True(firstWorld.TryAddGrid(configuration, out ushort firstIndex));
        Assert.True(secondWorld.TryAddGrid(configuration, out ushort secondIndex));

        VoxelGrid firstGrid = firstWorld.ActiveGrids[firstIndex];
        VoxelGrid secondGrid = secondWorld.ActiveGrids[secondIndex];
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
        using GridWorld blockerWorld = new();
        using GridWorld otherWorld = new();
        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        BoundingArea area = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        BoundsBlocker blocker = new(blockerWorld, area, cacheCoveredVoxels: true);

        blocker.ApplyBlockage();
        Assert.False(blocker.IsBlocking);

        Assert.True(otherWorld.TryAddGrid(configuration, out ushort otherIndex));
        VoxelGrid otherGrid = otherWorld.ActiveGrids[otherIndex];
        Assert.True(otherGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel otherVoxel));

        Assert.False(blocker.IsBlocking);
        Assert.False(otherVoxel.IsBlocked);

        Assert.True(blockerWorld.TryAddGrid(configuration, out ushort blockerIndex));
        VoxelGrid blockerGrid = blockerWorld.ActiveGrids[blockerIndex];
        Assert.True(blockerGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel blockerVoxel));

        Assert.True(blocker.IsBlocking);
        Assert.True(blockerVoxel.IsBlocked);
        Assert.False(otherVoxel.IsBlocked);
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
