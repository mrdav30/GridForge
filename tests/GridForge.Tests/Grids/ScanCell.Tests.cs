using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class ScanCellTests : IDisposable
{
    public ScanCellTests()
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
    public void GetOccupantsFor_ShouldReturnCorrectList()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(
            new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(45, 0, 45);

        var occupant1 = new TestOccupant(position);
        var occupant2 = new TestOccupant(position);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        grid.TryGetVoxel(position, out Voxel target);

        List<IVoxelOccupant> occupants = new List<IVoxelOccupant>(grid.GetOccupants(target.Index));
        Assert.True(occupants.Count > 0);
    }

    [Fact]
    public void GetConditionalOccupants_ShouldFilterCorrectly()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(
            new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(41, 0, 41);

        var occupant1 = new TestOccupant(position, 1);
        var occupant2 = new TestOccupant(position);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        grid.TryGetVoxel(position, out Voxel target);

        List<IVoxelOccupant> filtered = new List<IVoxelOccupant>(
            grid.GetConditionalOccupants(target.Index, groupCondition: key => key == 1));

        Assert.Single(filtered);
        Assert.Equal(1, filtered[0].OccupantGroupId);
    }

    [Fact]
    public void GetOccupants_ShouldReturnEmptyList_WhenNoOccupantsPresent()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        List<IVoxelOccupant> occupants = new List<IVoxelOccupant>(grid.GetOccupants(new Vector3d(-25, 0, -25)));

        Assert.Empty(occupants);
    }

    [Fact]
    public void RemoveOccupant_ShouldReturnFalse_WhenOccupantDoesNotExist()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(
            new Vector3d(-10, 0, -10),
            new Vector3d(10, 0, 10)),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(10, 0, 10);

        var occupant = new TestOccupant(position);

        bool removed = grid.TryRemoveVoxelOccupant(occupant); // Non-existent occupant

        Assert.False(removed);
    }

    [Fact]
    public void GetConditionalOccupants_ShouldReturnEmptyList_WhenNoMatches()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(2, 0, 2);

        var occupant1 = new TestOccupant(position, 5);
        var occupant2 = new TestOccupant(position, 6);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        List<IVoxelOccupant> filtered = new List<IVoxelOccupant>(
            grid.GetConditionalOccupants(position, groupCondition: key => key == 99)); // No matches

        Assert.Empty(filtered);
    }

    [Fact]
    public void RemoveAllOccupants_ShouldRemoveOnlyMatchingClusterOccupants()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(
            new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(49, 0, 49);

        var occupant1 = new TestOccupant(position, 1); // Cluster Key 1
        var occupant2 = new TestOccupant(position, 1); // Cluster Key 1
        var occupant3 = new TestOccupant(position, 2); // Cluster Key 2 (should not be removed)

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);
        grid.TryAddVoxelOccupant(occupant3);

        grid.TryGetVoxel(position, out Voxel target);

        bool removed1 = grid.TryRemoveVoxelOccupant(target.Index, occupant1);
        bool removed2 = grid.TryRemoveVoxelOccupant(occupant2);

        Assert.True(removed1);
        Assert.True(removed2);

        // Verify only ClusterKey 1 occupants are removed, but ClusterKey 2 still exists
        bool hasCluster2Occupants = grid.GetConditionalOccupants(
            position,
            groupCondition: key => key == 2).IsPopulatedSafe();

        Assert.True(hasCluster2Occupants); // ClusterKey 2 should still be occupied
    }

    [Fact]
    public void RemoveAllOccupants_ShouldMarkIndependentGridAsUnoccupied()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(9, 9, 9), new Vector3d(10, 10, 10)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d position = new Vector3d(9.5, 9.5, 9.5);

        var occupant1 = new TestOccupant(position, 1);
        var occupant2 = new TestOccupant(position, 2);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        grid.TryGetVoxel(position, out Voxel target);

        bool removed1 = grid.TryRemoveVoxelOccupant(occupant1);
        bool removed2 = grid.TryRemoveVoxelOccupant(target.Index, occupant2);

        Assert.True(removed1);
        Assert.True(removed2);

        grid.TryGetScanCell(position, out ScanCell cell);

        Assert.False(cell.IsOccupied); // Should be false after last occupant is removed
    }

    [Fact]
    public void ScanRadius_ShouldFindOccupantsWithinRadius()
    {
        // Arrange
        GlobalGridManager.TryAddGrid(new GridConfiguration(
            new Vector3d(-20, 0, -20),
            new Vector3d(20, 0, 20)),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d scanCenter = new Vector3d(0, 0, 0);
        Fixed64 scanRadius = (Fixed64)6; // Searching within a radius of 5 units

        var occupant1 = new TestOccupant(new Vector3d(2, 0, 2), 1);  // Within radius
        var occupant2 = new TestOccupant(new Vector3d(4, 0, 4), 1);  // Within radius
        var occupant3 = new TestOccupant(new Vector3d(10, 0, 10), 1); // Outside radius

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);
        grid.TryAddVoxelOccupant(occupant3);

        // Act
        var results = new SwiftList<IVoxelOccupant>(
            GridScanManager.ScanRadius(scanCenter, scanRadius));

        // Assert
        Assert.Contains(occupant1, results);
        Assert.Contains(occupant2, results);
        Assert.DoesNotContain(occupant3, results);
    }

    [Fact]
    public void ScanRadius_ShouldFilterByGroupCondition()
    {
        // Arrange
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-20, 0, -20), new Vector3d(20, 0, 20)), out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d scanCenter = new Vector3d(0, 0, 0);
        Fixed64 scanRadius = (Fixed64)5;

        var occupant1 = new TestOccupant(new Vector3d(1, 0, 1), 1); // Group 1
        var occupant2 = new TestOccupant(new Vector3d(2, 0, 2), 2); // Group 2
        var occupant3 = new TestOccupant(new Vector3d(3, 0, 3), 3); // Group 3 (out of filter)

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);
        grid.TryAddVoxelOccupant(occupant3);

        // Act
        var filteredResults = new SwiftList<IVoxelOccupant>(GridScanManager.ScanRadius(
            scanCenter,
            scanRadius, groupCondition: groupId => groupId == 1 || groupId == 2));

        // Assert
        Assert.Contains(occupant1, filteredResults);
        Assert.Contains(occupant2, filteredResults);
        // Should be excluded based on group condition
        Assert.DoesNotContain(occupant3, filteredResults);
    }

    [Fact]
    public void ScanCell_ShouldRemainEmptyUntilOccupied()
    {
        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(15, 0, 15), scanCellSize: 4),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetScanCell(new Vector3d(1, 0, 1), out ScanCell scanCell));

        Assert.False(scanCell.IsOccupied);
        Assert.Equal(0, scanCell.CellOccupantCount);
        Assert.Empty(grid.GetOccupants(new Vector3d(1, 0, 1)));
    }

    [Fact]
    public void ScanCell_ShouldTrackHighOccupancyWithinSingleCell()
    {
        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(15, 0, 15), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(2, 0, 2);

        List<TestOccupant> occupants = Enumerable.Range(0, 64)
            .Select(_ => new TestOccupant(position))
            .ToList();

        foreach (TestOccupant occupant in occupants)
            Assert.True(grid.TryAddVoxelOccupant(occupant));

        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(position, out ScanCell scanCell));
        Assert.NotNull(grid.ActiveScanCells);
        Assert.Single(grid.ActiveScanCells);
        Assert.Equal(64, voxel.OccupantCount);
        Assert.Equal(64, scanCell.CellOccupantCount);
        Assert.Equal(64, grid.GetOccupants(position).Count());
    }

    [Fact]
    public void GetOccupantsFor_ShouldIsolateOccupantsByVoxelBucket()
    {
        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Vector3d firstPosition = new Vector3d(1, 0, 1);
        Vector3d secondPosition = new Vector3d(2, 0, 2);

        TestOccupant firstBucketOccupant = new TestOccupant(firstPosition, 1);
        TestOccupant secondBucketOccupant = new TestOccupant(firstPosition, 2);
        TestOccupant thirdBucketOccupant = new TestOccupant(secondPosition, 3);

        Assert.True(grid.TryAddVoxelOccupant(firstBucketOccupant));
        Assert.True(grid.TryAddVoxelOccupant(secondBucketOccupant));
        Assert.True(grid.TryAddVoxelOccupant(thirdBucketOccupant));
        Assert.True(grid.TryGetVoxel(firstPosition, out Voxel firstVoxel));
        Assert.True(grid.TryGetVoxel(secondPosition, out Voxel secondVoxel));
        Assert.True(grid.TryGetScanCell(firstPosition, out ScanCell scanCell));

        List<IVoxelOccupant> firstBucket = InvokeGetOccupantsFor(scanCell, firstVoxel.GlobalIndex).ToList();
        List<IVoxelOccupant> secondBucket = InvokeGetOccupantsFor(scanCell, secondVoxel.GlobalIndex).ToList();

        Assert.Equal(2, firstBucket.Count);
        Assert.Contains(firstBucketOccupant, firstBucket);
        Assert.Contains(secondBucketOccupant, firstBucket);
        Assert.DoesNotContain(thirdBucketOccupant, firstBucket);

        Assert.Single(secondBucket);
        Assert.Same(thirdBucketOccupant, secondBucket[0]);
    }

    [Fact]
    public void ScanCell_InternalOperations_ShouldHandleInactiveAndMissingStateGracefully()
    {
        ScanCell inactiveCell = new ScanCell();
        TestOccupant occupant = new TestOccupant(Vector3d.Zero);

        InvokeReset(inactiveCell);

        Assert.False(inactiveCell.IsAllocated);
        Assert.False(inactiveCell.IsOccupied);
        Assert.Equal(0, inactiveCell.CellOccupantCount);
        Assert.False(InvokeTryRemoveOccupant(
            inactiveCell,
            new GlobalVoxelIndex(0, new VoxelIndex(0, 0, 0), 0),
            occupant,
            0));
    }

    [Fact]
    public void ScanCell_InternalOperations_ShouldReturnEmptyOrFalseForMissingBucketsAndTickets()
    {
        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant occupant = new TestOccupant(new Vector3d(1, 0, 1), 3);

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.True(grid.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel occupiedVoxel));
        Assert.True(grid.TryGetVoxel(new Vector3d(2, 0, 2), out Voxel emptyVoxel));
        Assert.True(grid.TryGetScanCell(occupant.Position, out ScanCell scanCell));

        List<IVoxelOccupant> missingBucket = InvokeGetOccupantsFor(scanCell, emptyVoxel.GlobalIndex).ToList();
        TestOccupant missingBucketOccupant = new TestOccupant(emptyVoxel.WorldPosition);
        TestOccupant invalidTicketOccupant = new TestOccupant(occupiedVoxel.WorldPosition);

        Assert.Empty(missingBucket);
        Assert.False(InvokeTryRemoveOccupant(scanCell, emptyVoxel.GlobalIndex, missingBucketOccupant, 0));
        Assert.False(InvokeTryRemoveOccupant(scanCell, occupiedVoxel.GlobalIndex, invalidTicketOccupant, 99));
        Assert.Single(InvokeGetOccupantsFor(scanCell, occupiedVoxel.GlobalIndex));
    }

    [Fact]
    public void TryGetOccupantAt_ShouldReturnFalseForRemovedOrInvalidTickets()
    {
        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        TestOccupant occupant = new TestOccupant(new Vector3d(1, 0, 1), 7);

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.True(grid.TryGetVoxel(occupant.Position, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(occupant.Position, out ScanCell scanCell));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(occupant, voxel.GlobalIndex, out int ticket));

        Assert.True(InvokeTryGetOccupantAt(scanCell, voxel.GlobalIndex, ticket, out IVoxelOccupant resolvedOccupant));
        Assert.Same(occupant, resolvedOccupant);

        Assert.True(grid.TryRemoveVoxelOccupant(occupant));

        Assert.False(InvokeTryGetOccupantAt(scanCell, voxel.GlobalIndex, ticket, out IVoxelOccupant removedOccupant));
        Assert.Null(removedOccupant);
        Assert.False(InvokeTryGetOccupantAt(scanCell, voxel.GlobalIndex, ticket + 1, out IVoxelOccupant invalidTicketOccupant));
        Assert.Null(invalidTicketOccupant);
    }

    [Fact]
    public void ScanRadius_ShouldRespectOccupantConditionAcrossScanCellBoundaries()
    {
        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(20, 0, 20), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        TestOccupant leftCellOccupant = new TestOccupant(new Vector3d(7, 0, 7), 1);
        TestOccupant rightCellOccupant = new TestOccupant(new Vector3d(8, 0, 8), 1);
        TestOccupant distantOccupant = new TestOccupant(new Vector3d(14, 0, 14), 1);

        grid.TryAddVoxelOccupant(leftCellOccupant);
        grid.TryAddVoxelOccupant(rightCellOccupant);
        grid.TryAddVoxelOccupant(distantOccupant);

        List<IVoxelOccupant> filteredResults = GridScanManager.ScanRadius(
            new Vector3d(7.5, 0, 7.5),
            (Fixed64)2,
            occupantCondition: occupant => occupant.Position.x >= (Fixed64)8)
            .ToList();

        Assert.DoesNotContain(leftCellOccupant, filteredResults);
        Assert.Contains(rightCellOccupant, filteredResults);
        Assert.DoesNotContain(distantOccupant, filteredResults);
    }

    [Fact]
    public void OccupantOperations_ShouldRemainConsistentUnderConcurrentLoad()
    {
        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(15, 0, 15), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        Vector3d position = new Vector3d(2, 0, 2);

        TestOccupant[] occupants = Enumerable.Range(0, 128)
            .Select(_ => new TestOccupant(position))
            .ToArray();
        bool[] addResults = new bool[occupants.Length];
        bool[] removeResults = new bool[occupants.Length];

        Parallel.For(0, occupants.Length, i => addResults[i] = grid.TryAddVoxelOccupant(occupants[i]));

        Assert.All(addResults, Assert.True);
        Assert.True(grid.TryGetVoxel(position, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(position, out ScanCell scanCell));
        Assert.Equal(128, voxel.OccupantCount);
        Assert.Equal(128, scanCell.CellOccupantCount);

        Parallel.For(0, occupants.Length, i => removeResults[i] = grid.TryRemoveVoxelOccupant(occupants[i]));

        Assert.All(removeResults, Assert.True);
        Assert.False(voxel.IsOccupied);
        Assert.False(scanCell.IsOccupied);
        Assert.Equal(0, scanCell.CellOccupantCount);
        Assert.Null(grid.ActiveScanCells);
    }

    private static void InvokeReset(ScanCell scanCell)
    {
        MethodInfo method = typeof(ScanCell).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find ScanCell.Reset.");

        method.Invoke(scanCell, Array.Empty<object>());
    }

    private static IEnumerable<IVoxelOccupant> InvokeGetOccupantsFor(ScanCell scanCell, GlobalVoxelIndex index)
    {
        MethodInfo method = typeof(ScanCell).GetMethod("GetOccupantsFor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find ScanCell.GetOccupantsFor.");

        return (IEnumerable<IVoxelOccupant>)method.Invoke(scanCell, new object[] { index });
    }

    private static bool InvokeTryGetOccupantAt(ScanCell scanCell, GlobalVoxelIndex index, int ticket, out IVoxelOccupant occupant)
    {
        MethodInfo method = typeof(ScanCell).GetMethod("TryGetOccupantAt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find ScanCell.TryGetOccupantAt.");
        object[] args = new object[] { index, ticket, null };
        bool result = (bool)method.Invoke(scanCell, args);
        occupant = (IVoxelOccupant)args[2];
        return result;
    }

    private static bool InvokeTryRemoveOccupant(ScanCell scanCell, GlobalVoxelIndex index, IVoxelOccupant occupant, int ticket)
    {
        MethodInfo method = typeof(ScanCell).GetMethod("TryRemoveOccupant", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find ScanCell.TryRemoveOccupant.");
        object[] args = new object[] { index, occupant, ticket };
        return (bool)method.Invoke(scanCell, args);
    }

}
