using FixedMathSharp;
using GridForge.Configuration;
using System;
using System.Linq;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class VoxelTests : IDisposable
{
    public VoxelTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        GlobalGridManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Voxel_ShouldInitializeCorrectly()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d testPosition = new Vector3d(10, 0, 10);

        bool found = grid.TryGetVoxel(testPosition, out Voxel voxel);

        Assert.True(found);
        Assert.NotNull(voxel);
        Assert.Equal(testPosition, voxel.WorldPosition);
        Assert.False(voxel.IsOccupied);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Voxel_Equals_ShouldUseReferenceIdentity()
    {
        Voxel first = new Voxel();
        Voxel second = new Voxel();

        Assert.True(first.Equals(first));
        Assert.False(first.Equals(second));
        Assert.NotEqual(0, first.GetHashCode());
    }

    [Fact]
    public void Voxel_ShouldHandleOccupantsCorrectly()
    {
        var config = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(6, 0, 6);
        TestOccupant occupant = new TestOccupant(position);
        grid.TryAddVoxelOccupant(occupant);

        int previousTicket = -1;
        foreach (var kvp in occupant.OccupyingIndexMap)
        {
            grid.TryGetVoxel(kvp.Key.VoxelIndex, out Voxel occupantVoxel);

            Assert.True(occupantVoxel.IsOccupied);
            Assert.True(grid.TryGetVoxelOccupant(occupantVoxel, kvp.Value, out _));
            previousTicket = kvp.Value;
        }

        grid.TryRemoveVoxelOccupant(occupant);
        Assert.False(grid.TryGetVoxelOccupant(position, previousTicket, out _));
    }

    [Fact]
    public void Voxel_ShouldCorrectlyBlockAndUnblock()
    {
        var config = new GridConfiguration(new(35, 1, 35), new(40, 1, 40));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(36, 1, 36), out Voxel voxel);

        BoundsKey spawnKey = new BoundsKey(new(36, 1, 36), new(37, 1, 37));

        grid.TryAddObstacle(voxel, spawnKey);
        Assert.True(voxel.IsBlocked);

        grid.TryRemoveObstacle(voxel, spawnKey);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Voxel_ShouldCorrectlyHandlePartitions()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        var partition = new TestPartition();
        Assert.True(voxel.TryAddPartition(partition));
        Assert.False(voxel.TryAddPartition(new TestPartition()));

        Assert.True(voxel.TryGetPartition<TestPartition>(out _));

        voxel.TryGetPartition(out TestPartition voxelPartition);

        Assert.Equal(partition, voxelPartition);

        voxel.TryRemovePartition<TestPartition>();
        Assert.False(voxel.TryGetPartition<TestPartition>(out _));
    }

    [Fact]
    public void Voxel_ShouldSetPartitionParentIndex()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        var partition = new TestPartition();

        Assert.True(voxel.TryAddPartition(partition));
        Assert.Equal(voxel.GlobalIndex, partition.GlobalIndex);
    }

    [Fact]
    public void Voxel_ShouldAllowDistinctConcretePartitionsWithSameTypeName()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        var partitionA = new PartitionFamilyA.SharedPartition();
        var partitionB = new PartitionFamilyB.SharedPartition();

        Assert.True(voxel.TryAddPartition(partitionA));
        Assert.True(voxel.TryAddPartition(partitionB));
        Assert.True(voxel.TryGetPartition<PartitionFamilyA.SharedPartition>(out PartitionFamilyA.SharedPartition storedA));
        Assert.True(voxel.TryGetPartition<PartitionFamilyB.SharedPartition>(out PartitionFamilyB.SharedPartition storedB));
        Assert.Equal(voxel.GlobalIndex, partitionA.GlobalIndex);
        Assert.Equal(voxel.GlobalIndex, partitionB.GlobalIndex);
        Assert.Same(partitionA, storedA);
        Assert.Same(partitionB, storedB);
    }

    [Fact]
    public void Voxel_ShouldRespectBoundaryConditions()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        grid.TryGetVoxel(new Vector3d(-10, 0, 0), out Voxel westVoxel);
        grid.TryGetVoxel(new Vector3d(10, 0, 0), out Voxel eastVoxel);

        Assert.True(grid.IsFacingBoundaryDirection(westVoxel.Index, SpatialDirection.West));
        Assert.True(grid.IsFacingBoundaryDirection(eastVoxel.Index, SpatialDirection.East));
    }

    [Fact]
    public void Voxel_ShouldNotIncrementObstacleCountBeyondLimit()
    {
        var config = new GridConfiguration(new Vector3d(36, 1, 36), new Vector3d(40, 1, 40));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(37, 1, 37), out Voxel voxel);

        BoundsKey spawnKey = new BoundsKey(new(37, 1, 37), new(38, 1, 38));

        grid.TryAddObstacle(voxel, spawnKey);
        grid.TryAddObstacle(voxel, spawnKey); // Attempt to add twice

        Assert.True(voxel.IsBlocked);
        Assert.Equal(1, voxel.ObstacleCount); // Should not increase beyond 1

        grid.TryRemoveObstacle(voxel, spawnKey);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Voxel_ShouldAllowMultipleOccupants()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(10, 0, 10);
        TestOccupant occupant1 = new TestOccupant(position);
        TestOccupant occupant2 = new TestOccupant(position);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        grid.TryGetVoxel(position, out Voxel targetVoxel);

        Assert.True(targetVoxel.IsOccupied);
        Assert.True(targetVoxel.OccupantCount > 0);

        occupant1.OccupyingIndexMap.TryGetValue(targetVoxel.GlobalIndex, out int ticket1);
        occupant2.OccupyingIndexMap.TryGetValue(targetVoxel.GlobalIndex, out int ticket2);

        grid.TryRemoveVoxelOccupant(targetVoxel, occupant1);
        Assert.False(grid.TryGetVoxelOccupant(targetVoxel, ticket1, out _));
        // Still occupied by occupant2
        Assert.True(grid.TryGetVoxelOccupant(targetVoxel, ticket2, out _));

        grid.TryRemoveVoxelOccupant(targetVoxel, occupant2);
        // Now fully unoccupied
        Assert.False(grid.TryGetVoxelOccupant(targetVoxel, ticket2, out _));
    }

    [Fact]
    public void Voxel_ShouldNotChangeStateIfRemovingNonExistentOccupant()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(0, 0, 0);
        TestOccupant occupant = new TestOccupant(position);

        grid.TryGetVoxel(position, out Voxel voxel);

        var previousState = voxel.IsOccupied;

        grid.TryRemoveVoxelOccupant(voxel, occupant); // Removing non-existent occupant

        Assert.True(voxel.IsOccupied == previousState); // Should remain unchanged
    }

    [Fact]
    public void Voxel_ShouldRetrieveOccupantsByType()
    {
        var config = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(-27, 0, -27);
        TestOccupant occupant1 = new TestOccupant(position);
        TestOccupant occupant2 = new TestOccupant(position);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        var occupants = grid.GetVoxelOccupantsByType<TestOccupant>(position);

        Assert.Equal(2, occupants.Count());
        Assert.Contains(occupant1, occupants);
        Assert.Contains(occupant2, occupants);
    }

}
