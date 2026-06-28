using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class VoxelTests : IDisposable
{
    private readonly GridWorld _world;

    public VoxelTests()
    {
        _world = GridWorldTestFactory.CreateWorld();
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    private static T[] CopyToArray<T>(ReadOnlySpan<T> values)
    {
        T[] copy = new T[values.Length];
        values.CopyTo(copy);
        return copy;
    }

    [Fact]
    public void Voxel_ShouldInitializeCorrectly()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d testPosition = new(10, 0, 10);

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
        Voxel first = new();
        Voxel second = new();

        Assert.True(first.Equals(first));
        Assert.False(first.Equals(second));
        Assert.NotEqual(0, first.GetHashCode());
    }

    [Fact]
    public void Voxel_StateProperties_ShouldReflectAllocationOccupancyAndObstacleState()
    {
        Voxel detachedVoxel = new();

        Assert.False(detachedVoxel.IsBlocked);
        Assert.False(detachedVoxel.IsBlockable);
        Assert.False(detachedVoxel.IsOccupied);

        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));

        Assert.False(voxel.IsBlocked);
        Assert.True(voxel.IsBlockable);
        Assert.False(voxel.IsOccupied);

        TestOccupant occupant = new(voxel.WorldPosition);
        BoundsKey obstacleToken = new(voxel.WorldPosition, voxel.WorldPosition);

        Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
        Assert.True(voxel.IsOccupied);
        Assert.False(voxel.IsBlockable);

        Assert.True(grid.TryRemoveVoxelOccupant(voxel, occupant));
        Assert.True(grid.TryAddObstacle(voxel, obstacleToken));
        Assert.True(voxel.IsBlocked);
        Assert.True(voxel.IsBlockable);
    }

    [Fact]
    public void Voxel_ShouldHandleOccupantsCorrectly()
    {
        var config = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(6, 0, 6);
        TestOccupant occupant = new(position);
        grid.TryAddVoxelOccupant(occupant);

        int previousTicket = -1;
        foreach (WorldVoxelIndex occupiedIndex in GridOccupantManager.GetOccupiedIndices(_world, occupant))
        {
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(_world, occupant, occupiedIndex, out int ticket));
            grid.TryGetVoxel(occupiedIndex.VoxelIndex, out Voxel occupantVoxel);

            Assert.True(occupantVoxel.IsOccupied);
            Assert.True(grid.TryGetVoxelOccupant(occupantVoxel, ticket, out _));
            previousTicket = ticket;
        }

        grid.TryRemoveVoxelOccupant(occupant);
        Assert.False(grid.TryGetVoxelOccupant(position, previousTicket, out _));
    }

    [Fact]
    public void Voxel_ShouldCorrectlyBlockAndUnblock()
    {
        var config = new GridConfiguration(new(35, 1, 35), new(40, 1, 40));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(36, 1, 36), out Voxel voxel);

        BoundsKey spawnKey = new(new(36, 1, 36), new(37, 1, 37));

        grid.TryAddObstacle(voxel, spawnKey);
        Assert.True(voxel.IsBlocked);

        grid.TryRemoveObstacle(voxel, spawnKey);
        Assert.False(voxel.IsBlocked);
    }

    [Fact]
    public void Voxel_ShouldCorrectlyHandlePartitions()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        var partition = new TestPartition();
        Assert.True(voxel.TryAddPartition(partition));
        Assert.False(voxel.TryAddPartition(new TestPartition()));

        Assert.True(voxel.TryGetPartition<TestPartition>(out _));

        voxel.TryGetPartition(out TestPartition voxelPartition);

        Assert.Equal(partition, voxelPartition);
        Assert.Same(partition, voxel.GetPartitionOrDefault<TestPartition>());

        voxel.TryRemovePartition<TestPartition>();
        Assert.False(voxel.TryGetPartition<TestPartition>(out _));
    }

    [Fact]
    public void PartitionAddRemoveSuccessPath_ShouldNotAllocateForSinglePartition()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);
        TestPartition partition = new();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool added = voxel.TryAddPartition(partition);
        bool removed = voxel.TryRemovePartition<TestPartition>();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(added);
        Assert.True(removed);
        Assert.True(allocated < 64, $"Expected single partition add/remove success path to avoid allocation but allocated {allocated} bytes.");
    }

    [Fact]
    public void SparseGrid_ShouldAllowPartitionsOnlyOnConfiguredVoxels()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2));
        VoxelIndex[] configuredVoxels = { new(1, 0, 1) };

        Assert.True(_world.TryAddGrid(config, configuredVoxels, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.False(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out _));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 1), out Voxel voxel));

        TestPartition partition = new();

        Assert.True(voxel.TryAddPartition(partition));
        Assert.True(voxel.TryGetPartition<TestPartition>(out TestPartition storedPartition));
        Assert.Same(partition, storedPartition);
        Assert.Equal(voxel.WorldIndex, partition.WorldIndex);
    }

    [Fact]
    public void TryAddPartition_ShouldRejectDuplicateTypeAndRetainOriginalPartition()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        TestPartition originalPartition = new();
        TestPartition duplicatePartition = new();

        Assert.True(voxel.TryAddPartition(originalPartition));
        Assert.False(voxel.TryAddPartition(duplicatePartition));
        Assert.True(voxel.TryGetPartition<TestPartition>(out TestPartition storedPartition));
        Assert.Same(originalPartition, storedPartition);
    }

    [Fact]
    public void PartitionQueries_ShouldReturnGracefulDefaultsWhenPartitionIsAbsent()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        Assert.False(voxel.HasPartition<TestPartition>());
        Assert.False(voxel.TryGetPartition<TestPartition>(out _));
        Assert.Null(voxel.GetPartitionOrDefault<TestPartition>());
        Assert.False(voxel.TryRemovePartition<TestPartition>());

        using DiagnosticCaptureScope diagnostics = new();
        Assert.False(voxel.TryRemovePartition<TestPartition>());
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("not found"));
    }

    [Fact]
    public void TryAddPartition_ShouldRollbackProviderStateWhenOnAddThrows()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        ThrowOnAddPartition partition = new();

        Assert.False(voxel.TryAddPartition(partition));
        Assert.False(voxel.HasPartition<ThrowOnAddPartition>());
        Assert.Null(voxel.GetPartitionOrDefault<ThrowOnAddPartition>());
    }

    [Fact]
    public void TryAddPartition_ShouldRejectNullPartition()
    {
        Voxel voxel = new();

        Assert.False(voxel.TryAddPartition(null));
        Assert.False(voxel.IsPartioned);
    }

    [Fact]
    public void TryRemovePartition_ShouldRemovePartitionEvenWhenOnRemoveThrows()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        ThrowOnRemovePartition partition = new();

        Assert.True(voxel.TryAddPartition(partition));
        Assert.True(voxel.TryRemovePartition<ThrowOnRemovePartition>());
        Assert.False(voxel.HasPartition<ThrowOnRemovePartition>());
        Assert.Null(voxel.GetPartitionOrDefault<ThrowOnRemovePartition>());
    }

    [Fact]
    public void Reset_ShouldUseProvidedOwnerGridWhenClearingTrackedObstacles()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        BoundsKey obstacleToken = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));
        Assert.True(grid.TryAddObstacle(voxel, obstacleToken));

        MethodInfo resetMethod = typeof(Voxel).GetMethod(
            "Reset",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(resetMethod);
        resetMethod.Invoke(voxel, new object[] { grid });

        Assert.False(voxel.IsAllocated);
        Assert.False(voxel.IsBlocked);
        Assert.Equal(0, grid.ObstacleCount);
    }

    [Fact]
    public void Reset_ShouldNotResolveOwnerGridWhenNotProvided()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        BoundsKey obstacleToken = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));
        Assert.True(grid.TryAddObstacle(voxel, obstacleToken));

        MethodInfo resetMethod = typeof(Voxel).GetMethod(
            "Reset",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(resetMethod);
        resetMethod.Invoke(voxel, new object[] { null });

        Assert.False(voxel.IsAllocated);
        Assert.False(voxel.IsBlocked);
        Assert.Equal(1, grid.ObstacleCount);
    }

    [Fact]
    public void Voxel_ShouldSetPartitionParentIndex()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        var partition = new TestPartition();

        Assert.True(voxel.TryAddPartition(partition));
        Assert.Equal(voxel.WorldIndex, partition.WorldIndex);
    }

    [Fact]
    public void Voxel_ShouldAllowDistinctConcretePartitionsWithSameTypeName()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        var partitionA = new PartitionFamilyA.SharedPartition();
        var partitionB = new PartitionFamilyB.SharedPartition();

        Assert.True(voxel.TryAddPartition(partitionA));
        Assert.True(voxel.TryAddPartition(partitionB));
        Assert.True(voxel.TryGetPartition<PartitionFamilyA.SharedPartition>(out PartitionFamilyA.SharedPartition storedA));
        Assert.True(voxel.TryGetPartition<PartitionFamilyB.SharedPartition>(out PartitionFamilyB.SharedPartition storedB));
        Assert.Equal(voxel.WorldIndex, partitionA.WorldIndex);
        Assert.Equal(voxel.WorldIndex, partitionB.WorldIndex);
        Assert.Same(partitionA, storedA);
        Assert.Same(partitionB, storedB);
    }

    [Fact]
    public void Voxel_ShouldRespectBoundaryConditions()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        grid.TryGetVoxel(new Vector3d(-10, 0, 0), out Voxel westVoxel);
        grid.TryGetVoxel(new Vector3d(10, 0, 0), out Voxel eastVoxel);

        Assert.True(grid.IsFacingBoundary(westVoxel.Index, RectangularDirection.West));
        Assert.True(grid.IsFacingBoundary(eastVoxel.Index, RectangularDirection.East));
    }

    [Fact]
    public void Voxel_ShouldNotIncrementObstacleCountBeyondLimit()
    {
        var config = new GridConfiguration(new Vector3d(36, 1, 36), new Vector3d(40, 1, 40));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(new Vector3d(37, 1, 37), out Voxel voxel);

        BoundsKey spawnKey = new(new(37, 1, 37), new(38, 1, 38));

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
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(10, 0, 10);
        TestOccupant occupant1 = new(position);
        TestOccupant occupant2 = new(position);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        grid.TryGetVoxel(position, out Voxel targetVoxel);

        Assert.True(targetVoxel.IsOccupied);
        Assert.True(targetVoxel.OccupantCount > 0);

        Assert.True(GridOccupantManager.TryGetOccupancyTicket(_world, occupant1, targetVoxel.WorldIndex, out int ticket1));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(_world, occupant2, targetVoxel.WorldIndex, out int ticket2));

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
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(0, 0, 0);
        TestOccupant occupant = new(position);

        grid.TryGetVoxel(position, out Voxel voxel);

        var previousState = voxel.IsOccupied;

        grid.TryRemoveVoxelOccupant(voxel, occupant); // Removing non-existent occupant

        Assert.True(voxel.IsOccupied == previousState); // Should remain unchanged
    }

    [Fact]
    public void Voxel_ShouldRetrieveOccupantsByType()
    {
        var config = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(-27, 0, -27);
        TestOccupant occupant1 = new(position);
        TestOccupant occupant2 = new(position);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        var occupants = grid.GetVoxelOccupantsByType<TestOccupant>(position);

        Assert.Equal(2, occupants.Count());
        Assert.Contains(occupant1, occupants);
        Assert.Contains(occupant2, occupants);
    }

    [Fact]
    public void GetRectangularNeighborsInto_ShouldReturnDeterministicDirectionOrderForInteriorVoxel()
    {
        var config = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetVoxel(new Vector3d(1, 1, 1), out Voxel voxel));

        SwiftList<(RectangularDirection Direction, Voxel Voxel)> neighbors = new();
        voxel.GetRectangularNeighborsInto(grid, neighbors);

        Assert.Equal(RectangularDirectionUtility.Offsets.Length, neighbors.Count);

        for (int i = 0; i < RectangularDirectionUtility.Offsets.Length; i++)
        {
            (int x, int y, int z) offset = RectangularDirectionUtility.Offsets[i];
            Vector3d expectedPosition = voxel.WorldPosition + new Vector3d(offset.x, offset.y, offset.z);

            Assert.Equal((RectangularDirection)i, neighbors[i].Direction);
            Assert.Equal(expectedPosition, neighbors[i].Voxel.WorldPosition);
        }
    }

    [Fact]
    public void RectangularDirectionUtility_ShouldExposeDeterministicSubsets()
    {
        Assert.Equal(26, RectangularDirectionUtility.All.Length);
        Assert.Equal(6, RectangularDirectionUtility.Primary.Length);
        Assert.Equal(6, RectangularDirectionUtility.Perpendicular.Length);
        Assert.Equal(8, RectangularDirectionUtility.Planar.Length);
        Assert.Equal(2, RectangularDirectionUtility.Vertical.Length);
        Assert.Equal(9, RectangularDirectionUtility.BelowLayer.Length);
        Assert.Equal(9, RectangularDirectionUtility.AboveLayer.Length);
        Assert.Equal(16, RectangularDirectionUtility.VerticalDiagonal.Length);
        Assert.Equal(20, RectangularDirectionUtility.Diagonal.Length);

        Assert.Equal(
            CopyToArray(RectangularDirectionUtility.Perpendicular),
            CopyToArray(RectangularDirectionUtility.Primary));
        Assert.Equal(
            new[]
            {
                RectangularDirection.West,
                RectangularDirection.South,
                RectangularDirection.East,
                RectangularDirection.North,
                RectangularDirection.SouthWest,
                RectangularDirection.NorthWest,
                RectangularDirection.SouthEast,
                RectangularDirection.NorthEast
            },
            CopyToArray(RectangularDirectionUtility.Planar));
        RectangularDirection[] belowLayer = CopyToArray(RectangularDirectionUtility.BelowLayer);
        RectangularDirection[] aboveLayer = CopyToArray(RectangularDirectionUtility.AboveLayer);
        RectangularDirection[] verticalDiagonal = CopyToArray(RectangularDirectionUtility.VerticalDiagonal);
        Assert.Equal(
            belowLayer.Skip(1)
                .Concat(aboveLayer.Skip(1))
                .ToArray(),
            verticalDiagonal);
        Assert.DoesNotContain(RectangularDirection.Below, verticalDiagonal);
        Assert.DoesNotContain(RectangularDirection.Above, verticalDiagonal);
    }

    [Fact]
    public void GetRectangularNeighborsInto_ShouldReturnOnlyValidDirectionsForCornerVoxel()
    {
        var config = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));

        SwiftList<(RectangularDirection Direction, Voxel Voxel)> neighbors = new();
        voxel.GetRectangularNeighborsInto(grid, neighbors);

        var actualDirections = neighbors
            .Select(result => result.Direction)
            .ToList();
        var expectedDirections = CopyToArray(RectangularDirectionUtility.All)
            .Where(direction =>
            {
                (int x, int y, int z) offset = RectangularDirectionUtility.Offsets[(int)direction];
                return offset.x >= 0 && offset.y >= 0 && offset.z >= 0;
            })
            .ToList();

        Assert.Equal(expectedDirections, actualDirections);
        Assert.Equal(7, actualDirections.Count);
    }

    [Fact]
    public void TryGetNeighbor_ShouldResolveConsistentlyAcrossRepeatedCalls()
    {
        var config = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetVoxel(new Vector3d(1, 1, 1), out Voxel voxel));

        foreach (RectangularDirection direction in RectangularDirectionUtility.All)
        {
            bool foundFirst = voxel.TryGetNeighbor(
                grid,
                direction,
                out Voxel firstNeighbor);
            bool foundSecond = voxel.TryGetNeighbor(
                grid,
                direction,
                out Voxel secondNeighbor);

            Assert.Equal(foundFirst, foundSecond);

            if (foundFirst)
                Assert.Same(firstNeighbor, secondNeighbor);
        }
    }

    [Fact]
    public void GetRectangularNeighborsInto_ShouldRemainStableAcrossRepeatedCalls()
    {
        var config = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetVoxel(new Vector3d(1, 1, 1), out Voxel voxel));

        SwiftList<(RectangularDirection Direction, Voxel Voxel)> firstPass = new();
        SwiftList<(RectangularDirection Direction, Voxel Voxel)> secondPass = new();
        voxel.GetRectangularNeighborsInto(grid, firstPass);
        voxel.GetRectangularNeighborsInto(grid, secondPass);

        Assert.Equal(firstPass.Count, secondPass.Count);
        Assert.Equal(firstPass.Select(result => result.Direction), secondPass.Select(result => result.Direction));
    }

    [Fact]
    public void GetRectangularNeighborsInto_ShouldSkipMissingCornerDirections()
    {
        var config = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2));
        _world.TryAddGrid(config, out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));

        SwiftList<(RectangularDirection Direction, Voxel Voxel)> firstNeighbors = new();
        SwiftList<(RectangularDirection Direction, Voxel Voxel)> secondNeighbors = new();
        voxel.GetRectangularNeighborsInto(grid, firstNeighbors);
        voxel.GetRectangularNeighborsInto(grid, secondNeighbors);

        RectangularDirection[] firstPass = firstNeighbors
            .Select(result => result.Direction)
            .ToArray();
        RectangularDirection[] secondPass = secondNeighbors
            .Select(result => result.Direction)
            .ToArray();

        Assert.Equal(7, firstPass.Length);
        Assert.Equal(firstPass, secondPass);
        Assert.DoesNotContain(RectangularDirection.West, secondPass);
        Assert.DoesNotContain(RectangularDirection.South, secondPass);
        Assert.DoesNotContain(RectangularDirection.Below, secondPass);
    }

    [Fact]
    public void TryGetNeighbor_ShouldResolveAcrossConjoinedGridBoundary()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0)),
            out ushort firstGridIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0)),
            out ushort secondGridIndex));

        VoxelGrid firstGrid = _world.ActiveGrids[firstGridIndex];
        VoxelGrid secondGrid = _world.ActiveGrids[secondGridIndex];

        Assert.True(firstGrid.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel boundaryVoxel));
        Assert.True(secondGrid.TryGetVoxel(new Vector3d(2, 0, 0), out Voxel adjacentVoxel));

        Assert.True(boundaryVoxel.TryGetNeighbor(firstGrid, RectangularDirection.East, out Voxel resolvedNeighbor));
        Assert.Same(adjacentVoxel, resolvedNeighbor);
    }

    [Fact]
    public void SparseNeighborLookup_ShouldTreatMissingDenseToSparseNeighborAsAbsent()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0)),
            out ushort denseGridIndex));
        GridConfiguration sparseConfig = CreateSparseConfig(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0));
        VoxelIndex[] configuredVoxels = { new(0, 0, 0) };

        Assert.True(_world.TryAddGrid(sparseConfig, configuredVoxels, out _));
        VoxelGrid denseGrid = _world.ActiveGrids[denseGridIndex];
        Assert.True(denseGrid.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel boundaryVoxel));

        Assert.False(boundaryVoxel.TryGetNeighbor(denseGrid, RectangularDirection.East, out _));
    }

    [Fact]
    public void SparseNeighborLookup_ShouldResolveConfiguredSparseToSparseNeighbor()
    {
        GridConfiguration firstConfig = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0));
        GridConfiguration secondConfig = CreateSparseConfig(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0));
        VoxelIndex[] firstConfiguredVoxels = { new(1, 0, 0) };
        VoxelIndex[] secondConfiguredVoxels = { new(1, 0, 0) };

        Assert.True(_world.TryAddGrid(firstConfig, firstConfiguredVoxels, out ushort firstGridIndex));
        Assert.True(_world.TryAddGrid(secondConfig, secondConfiguredVoxels, out ushort secondGridIndex));
        VoxelGrid firstGrid = _world.ActiveGrids[firstGridIndex];
        VoxelGrid secondGrid = _world.ActiveGrids[secondGridIndex];
        Assert.True(firstGrid.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel boundaryVoxel));
        Assert.True(secondGrid.TryGetVoxel(new Vector3d(2, 0, 0), out Voxel expectedNeighbor));

        Assert.True(boundaryVoxel.TryGetNeighbor(firstGrid, RectangularDirection.East, out Voxel neighbor));
        Assert.Same(expectedNeighbor, neighbor);
    }

    [Fact]
    public void TryGetNeighbor_ShouldHandleInvalidRectangularDirectionsGracefully()
    {
        Voxel detachedVoxel = new();
        VoxelGrid detachedGrid = new();

        Assert.False(detachedVoxel.TryGetNeighbor(detachedGrid, RectangularDirection.None, out _));
        Assert.False(detachedVoxel.TryGetNeighbor(detachedGrid, RectangularDirection.West, out _));
        Assert.False(detachedVoxel.TryGetNeighbor(detachedGrid, (RectangularDirection)(-2), out _));
        Assert.False(detachedVoxel.TryGetNeighbor(detachedGrid, (RectangularDirection)999, out _));
    }

    [Fact]
    public void BoundaryNeighborLookup_ShouldReflectAdjacentGridsLoadAndUnload()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort centerIndex));
        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];

        Assert.True(centerGrid.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel boundaryVoxel));
        Assert.False(boundaryVoxel.TryGetNeighbor(centerGrid, RectangularDirection.NorthEast, out _));

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2)),
            out ushort northEastIndex));
        VoxelGrid northEastGrid = _world.ActiveGrids[northEastIndex];

        Assert.True(northEastGrid.TryGetVoxel(new Vector3d(2, 0, 2), out Voxel expectedNeighbor));
        Assert.True(boundaryVoxel.TryGetNeighbor(centerGrid, RectangularDirection.NorthEast, out Voxel resolvedNeighbor));
        Assert.Same(expectedNeighbor, resolvedNeighbor);

        Assert.True(_world.TryRemoveGrid(northEastIndex));
        Assert.False(boundaryVoxel.TryGetNeighbor(centerGrid, RectangularDirection.NorthEast, out _));
    }

    [Fact]
    public void ReleasedVoxel_ShouldNotLeakObstacleOrPartitionStateWhenReused()
    {
        GridConfiguration centerConfig = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        GridConfiguration eastConfig = new(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0));
        BoundsKey obstacleToken = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));

        Assert.True(_world.TryAddGrid(centerConfig, out ushort centerIndex));
        Assert.True(_world.TryAddGrid(eastConfig, out ushort eastIndex));

        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];

        Assert.True(centerGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel oldVoxel));
        Assert.True(oldVoxel.TryGetNeighbor(centerGrid, RectangularDirection.East, out Voxel eastNeighbor));
        Assert.Equal(new Vector3d(1, 0, 0), eastNeighbor.WorldPosition);
        Assert.True(oldVoxel.TryAddPartition(new TestPartition()));
        Assert.True(centerGrid.TryAddObstacle(oldVoxel, obstacleToken));

        Assert.True(_world.TryRemoveGrid(eastIndex));
        Assert.True(_world.TryRemoveGrid(centerIndex));
        Assert.True(_world.TryAddGrid(centerConfig, out ushort reusedIndex));

        VoxelGrid reusedGrid = _world.ActiveGrids[reusedIndex];

        Assert.True(reusedGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel reusedVoxel));
        Assert.False(reusedVoxel.HasPartition<TestPartition>());
        Assert.Null(reusedVoxel.GetPartitionOrDefault<TestPartition>());
        Assert.False(reusedVoxel.IsBlocked);
        Assert.Equal(0, reusedVoxel.ObstacleCount);
        Assert.False(reusedVoxel.TryGetNeighbor(reusedGrid, RectangularDirection.East, out _));
        Assert.True(reusedGrid.TryAddObstacle(reusedVoxel, obstacleToken));
    }

    [Fact]
    public void Reset_ShouldReleaseObstaclesWithoutOwnerGridLookupAndSwallowPartitionRemoveFailures()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        BoundsKey obstacleToken = new(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));
        Assert.True(grid.TryAddObstacle(voxel, obstacleToken));
        Assert.True(voxel.TryAddPartition(new ThrowOnRemovePartition()));
        Assert.True(voxel.IsPartioned);
        Assert.Equal(voxel.WorldIndex.GridIndex, voxel.GridIndex);

        voxel.WorldIndex = new WorldVoxelIndex(
            voxel.WorldIndex.WorldSpawnToken,
            ushort.MaxValue,
            voxel.WorldIndex.GridSpawnToken,
            voxel.Index);

        InvokeVoxelReset(voxel);

        Assert.False(voxel.IsAllocated);
        Assert.False(voxel.IsPartioned);
        Assert.False(voxel.HasPartition<ThrowOnRemovePartition>());
        Assert.Null(voxel.ObstacleTracker);
        Assert.Equal(0, voxel.ObstacleCount);
        Assert.Equal(0, voxel.OccupantCount);
        Assert.Equal(0, voxel.ScanCellKey);
    }

    [Fact]
    public void Voxel_ToStringAndObjectEquality_ShouldReflectGlobalIdentity()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));

        Assert.Equal(voxel.WorldIndex.ToString(), voxel.ToString());
        Assert.True(voxel.Equals((object)voxel));
        Assert.False(voxel.Equals((object)new object()));
    }

    private static void InvokeVoxelReset(Voxel voxel)
    {
        MethodInfo resetMethod = typeof(Voxel).GetMethod(
            "Reset",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(resetMethod);
        resetMethod.Invoke(voxel, new object[] { null });
    }

    private static GridConfiguration CreateSparseConfig(Vector3d min, Vector3d max) =>
        new(min, max, storageKind: GridStorageKind.Sparse);
}
