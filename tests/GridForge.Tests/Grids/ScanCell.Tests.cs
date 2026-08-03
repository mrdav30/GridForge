using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class ScanCellTests : IDisposable
{
    private readonly GridWorld _world;

    public ScanCellTests()
    {
        _world = GridWorldTestFactory.CreateWorld();
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetOccupantsFor_ShouldReturnCorrectList()
    {
        _world.TryAddGrid(new GridConfiguration(
            new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(45, 0, 45);

        var occupant1 = new TestOccupant(position);
        var occupant2 = new TestOccupant(position);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        grid.TryGetVoxel(position, out Voxel target);

        List<IVoxelOccupant> occupants = new(grid.GetOccupants(target.Index));
        Assert.True(occupants.Count > 0);
    }

    [Fact]
    public void GetConditionalOccupants_ShouldFilterCorrectly()
    {
        _world.TryAddGrid(new GridConfiguration(
            new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(41, 0, 41);

        var occupant1 = new TestOccupant(position, 1);
        var occupant2 = new TestOccupant(position);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        grid.TryGetVoxel(position, out Voxel target);

        List<IVoxelOccupant> filtered = new(
            grid.GetConditionalOccupants(target.Index, groupCondition: key => key == 1));

        Assert.Single(filtered);
        Assert.Equal(1, filtered[0].OccupantGroupId);
    }

    [Fact]
    public void GetOccupants_ShouldReturnEmptyList_WhenNoOccupantsPresent()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        List<IVoxelOccupant> occupants = new(grid.GetOccupants(new Vector3d(-25, 0, -25)));

        Assert.Empty(occupants);
    }

    [Fact]
    public void RemoveOccupant_ShouldReturnFalse_WhenOccupantDoesNotExist()
    {
        _world.TryAddGrid(new GridConfiguration(
            new Vector3d(-10, 0, -10),
            new Vector3d(10, 0, 10)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(10, 0, 10);

        var occupant = new TestOccupant(position);

        bool removed = grid.TryRemoveVoxelOccupant(occupant); // Non-existent occupant

        Assert.False(removed);
    }

    [Fact]
    public void GetConditionalOccupants_ShouldReturnEmptyList_WhenNoMatches()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(2, 0, 2);

        var occupant1 = new TestOccupant(position, 5);
        var occupant2 = new TestOccupant(position, 6);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);

        List<IVoxelOccupant> filtered = new(
            grid.GetConditionalOccupants(position, groupCondition: key => key == 99)); // No matches

        Assert.Empty(filtered);
    }

    [Fact]
    public void RemoveAllOccupants_ShouldRemoveOnlyMatchingClusterOccupants()
    {
        _world.TryAddGrid(new GridConfiguration(
            new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = new(49, 0, 49);

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
        _world.TryAddGrid(new GridConfiguration(new Vector3d(9, 9, 9), new Vector3d(10, 10, 10)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d position = Vector3d.FromDouble(9.5, 9.5, 9.5);

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
        _world.TryAddGrid(new GridConfiguration(
            new Vector3d(-20, 0, -20),
            new Vector3d(20, 0, 20)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d scanCenter = new(0, 0, 0);
        Fixed64 scanRadius = (Fixed64)6; // Searching within a radius of 5 units

        var occupant1 = new TestOccupant(new Vector3d(2, 0, 2), 1);  // Within radius
        var occupant2 = new TestOccupant(new Vector3d(4, 0, 4), 1);  // Within radius
        var occupant3 = new TestOccupant(new Vector3d(10, 0, 10), 1); // Outside radius

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);
        grid.TryAddVoxelOccupant(occupant3);

        // Act
        var results = new SwiftList<IVoxelOccupant>(
            GridScanManager.ScanRadius(_world, scanCenter, scanRadius));

        // Assert
        Assert.Contains(occupant1, results);
        Assert.Contains(occupant2, results);
        Assert.DoesNotContain(occupant3, results);
    }

    [Fact]
    public void ScanRadius2D_ShouldLockToSelectedLayerAndUseXzDistance()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(5, 3, 5), scanCellSize: 4),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector2d scanCenter = new(0, 0);
        Fixed64 scanRadius = (Fixed64)2;
        TestOccupant sameLayerInside = new(new Vector3d(1, 0, 1), 1);
        TestOccupant sameLayerOutside = new(new Vector3d(4, 0, 4), 1);
        TestOccupant otherLayerInsideXz = new(new Vector3d(1, 1, 1), 1);

        Assert.True(grid.TryAddVoxelOccupant(sameLayerInside));
        Assert.True(grid.TryAddVoxelOccupant(sameLayerOutside));
        Assert.True(grid.TryAddVoxelOccupant(otherLayerInsideXz));

        IVoxelOccupant[] results = GridScanManager.ScanRadius(_world, scanCenter, scanRadius).ToArray();

        Assert.Contains(sameLayerInside, results);
        Assert.DoesNotContain(sameLayerOutside, results);
        Assert.DoesNotContain(otherLayerInsideXz, results);
    }

    [Fact]
    public void ScanRadius2D_ShouldDifferFrom3DScanWhenVerticalOffsetIsInsideSphere()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(5, 3, 5), scanCellSize: 4),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector2d scanCenter2D = new(0, 0);
        Vector3d scanCenter3D = GridPlane2d.ToWorld(scanCenter2D);
        Fixed64 scanRadius = (Fixed64)2;
        TestOccupant otherLayerInside3dRadius = new(new Vector3d(1, 1, 1), 1);

        Assert.True(grid.TryAddVoxelOccupant(otherLayerInside3dRadius));

        IVoxelOccupant[] twoDimensionalResults = GridScanManager.ScanRadius(_world, scanCenter2D, scanRadius).ToArray();
        IVoxelOccupant[] threeDimensionalResults = GridScanManager.ScanRadius(_world, scanCenter3D, scanRadius).ToArray();

        Assert.DoesNotContain(otherLayerInside3dRadius, twoDimensionalResults);
        Assert.Contains(otherLayerInside3dRadius, threeDimensionalResults);
    }

    [Fact]
    public void ScanRadius_ShouldFilterByGroupCondition()
    {
        // Arrange
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-20, 0, -20), new Vector3d(20, 0, 20)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d scanCenter = new(0, 0, 0);
        Fixed64 scanRadius = (Fixed64)5;

        var occupant1 = new TestOccupant(new Vector3d(1, 0, 1), 1); // Group 1
        var occupant2 = new TestOccupant(new Vector3d(2, 0, 2), 2); // Group 2
        var occupant3 = new TestOccupant(new Vector3d(3, 0, 3), 3); // Group 3 (out of filter)

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);
        grid.TryAddVoxelOccupant(occupant3);

        // Act
        var filteredResults = new SwiftList<IVoxelOccupant>(GridScanManager.ScanRadius(_world,
            scanCenter,
            scanRadius, groupCondition: groupId => groupId == 1 || groupId == 2));

        // Assert
        Assert.Contains(occupant1, filteredResults);
        Assert.Contains(occupant2, filteredResults);
        // Should be excluded based on group condition
        Assert.DoesNotContain(occupant3, filteredResults);
    }

    [Fact]
    public void ScanRadiusInto_ShouldClearAndFillCallerOwnedResults()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-20, 0, -20), new Vector3d(20, 0, 20)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d scanCenter = new(0, 0, 0);
        Fixed64 scanRadius = (Fixed64)5;
        var staleOccupant = new TestOccupant(new Vector3d(-10, 0, -10), 9);
        var occupant1 = new TestOccupant(new Vector3d(1, 0, 1), 1);
        var occupant2 = new TestOccupant(new Vector3d(3, 0, 3), 2);
        var occupant3 = new TestOccupant(new Vector3d(9, 0, 9), 1);
        SwiftList<IVoxelOccupant> results = new();
        results.Add(staleOccupant);

        grid.TryAddVoxelOccupant(occupant1);
        grid.TryAddVoxelOccupant(occupant2);
        grid.TryAddVoxelOccupant(occupant3);

        GridScanManager.ScanRadiusInto(
            _world,
            scanCenter,
            scanRadius,
            results,
            groupCondition: groupId => groupId == 1);

        Assert.DoesNotContain(staleOccupant, results);
        Assert.Contains(occupant1, results);
        Assert.DoesNotContain(occupant2, results);
        Assert.DoesNotContain(occupant3, results);
    }

#if !DEBUG
    [Fact]
    public void TryGetOccupantAt_ShouldAvoidSteadyStateAllocation()
    {
        _world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        TestOccupant occupant = new(Vector3d.Zero, 1);

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(Vector3d.Zero, out ScanCell scanCell));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            _world,
            occupant,
            voxel.WorldIndex,
            out OccupantTicket ticket));
        Assert.True(scanCell.TryGetOccupantAt(voxel.WorldIndex, ticket, out _));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        bool resolved = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8192; i++)
        {
            resolved &= scanCell.TryGetOccupantAt(
                voxel.WorldIndex,
                ticket,
                out IVoxelOccupant currentOccupant)
                && ReferenceEquals(occupant, currentOccupant);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(resolved);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ScanRadiusInto_ShouldAvoidSteadyStateAllocation()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(31, 0, 31), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        for (int z = 0; z < 8; z++)
        {
            for (int x = 0; x < 8; x++)
            {
                var occupant = new TestOccupant(new Vector3d(x, 0, z), (byte)((x + z) & 1));
                grid.TryAddVoxelOccupant(occupant);
            }
        }

        Vector3d scanCenter = new(4, 0, 4);
        Fixed64 scanRadius = (Fixed64)7;
        SwiftList<IVoxelOccupant> results = new();
        GridScanScratch scratch = new();

        GridScanManager.ScanRadiusInto(_world, scanCenter, scanRadius, results, scratch);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
            GridScanManager.ScanRadiusInto(_world, scanCenter, scanRadius, results, scratch);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < 512,
            $"Expected steady-state scan allocation below 512 bytes, but allocated {allocated} bytes.");
    }

    [Fact]
    public void ScanRadiusInto2D_ShouldAvoidSteadyStateAllocation()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(31, 1, 31), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        for (int z = 0; z < 8; z++)
        {
            for (int x = 0; x < 8; x++)
            {
                var occupant = new TestOccupant(new Vector3d(x, 0, z), (byte)((x + z) & 1));
                grid.TryAddVoxelOccupant(occupant);
            }
        }

        Vector2d scanCenter = new(4, 4);
        Fixed64 scanRadius = (Fixed64)7;
        SwiftList<IVoxelOccupant> results = new();
        GridScanScratch scratch = new();

        GridScanManager.ScanRadiusInto(_world, scanCenter, scanRadius, results, scratch);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
            GridScanManager.ScanRadiusInto(_world, scanCenter, scanRadius, results, scratch);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < 512,
            $"Expected steady-state 2D scan allocation below 512 bytes, but allocated {allocated} bytes.");
    }
#endif

    [Fact]
    public void ScanRadiusIntoGeneric_ShouldFilterByTypeWithoutLinqAllocation()
    {
        _world.TryAddGrid(new GridConfiguration(new Vector3d(-5, 0, -5), new Vector3d(5, 0, 5)), out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        var occupant = new TestOccupant(new Vector3d(1, 0, 1), 1);
        SwiftList<TestOccupant> results = new();
        GridScanScratch scratch = new();

        grid.TryAddVoxelOccupant(occupant);
        GridScanManager.ScanRadiusInto<TestOccupant>(_world, Vector3d.Zero, (Fixed64)3, results, scratch);

        Assert.Single(results);
        Assert.Same(occupant, results[0]);
    }

    [Fact]
    public void ScanCell_ShouldRemainEmptyUntilOccupied()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(15, 0, 15), scanCellSize: 4),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetScanCell(new Vector3d(1, 0, 1), out ScanCell scanCell));

        Assert.False(scanCell.IsOccupied);
        Assert.Equal(0, scanCell.CellOccupantCount);
        Assert.Empty(grid.GetOccupants(new Vector3d(1, 0, 1)));
    }

    [Fact]
    public void AddOccupantsWithinRadiusTo_ShouldNoOpForEmptyScanCell()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(3, 0, 3), scanCellSize: 2),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetScanCell(new Vector3d(1, 0, 1), out ScanCell scanCell));
        SwiftList<IVoxelOccupant> untypedResults = new();
        SwiftList<TestOccupant> typedResults = new();

        scanCell.AddOccupantsWithinRadiusTo(untypedResults, Vector3d.Zero, Fixed64.One);
        scanCell.AddOccupantsWithinRadiusTo(typedResults, Vector3d.Zero, Fixed64.One);

        Assert.Empty(untypedResults);
        Assert.Empty(typedResults);
    }

    [Fact]
    public void ScanCell_ShouldTrackHighOccupancyWithinSingleCell()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(15, 0, 15), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d position = new(2, 0, 2);

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
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Vector3d firstPosition = new(1, 0, 1);
        Vector3d secondPosition = new(2, 0, 2);

        TestOccupant firstBucketOccupant = new(firstPosition, 1);
        TestOccupant secondBucketOccupant = new(firstPosition, 2);
        TestOccupant thirdBucketOccupant = new(secondPosition, 3);

        Assert.True(grid.TryAddVoxelOccupant(firstBucketOccupant));
        Assert.True(grid.TryAddVoxelOccupant(secondBucketOccupant));
        Assert.True(grid.TryAddVoxelOccupant(thirdBucketOccupant));
        Assert.True(grid.TryGetVoxel(firstPosition, out Voxel firstVoxel));
        Assert.True(grid.TryGetVoxel(secondPosition, out Voxel secondVoxel));
        Assert.True(grid.TryGetScanCell(firstPosition, out ScanCell scanCell));

        List<IVoxelOccupant> firstBucket = scanCell.GetOccupantsFor(firstVoxel.WorldIndex).ToList();
        List<IVoxelOccupant> secondBucket = scanCell.GetOccupantsFor(secondVoxel.WorldIndex).ToList();

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
        ScanCell inactiveCell = new();

        inactiveCell.Reset();

        Assert.False(inactiveCell.IsAllocated);
        Assert.False(inactiveCell.IsOccupied);
        Assert.Equal(0, inactiveCell.CellOccupantCount);
        Assert.False(inactiveCell.TryRemoveOccupant(default, default));
#nullable enable
        Assert.False(inactiveCell.TryGetOccupantAt(default, default, out IVoxelOccupant? found));
#nullable disable
        Assert.Null(found);
    }

    [Fact]
    public void PublicRetrievalHelpers_ShouldReturnEmptyOrFalseForNeverOccupiedScanCells()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetScanCell(new Vector3d(1, 0, 1), out ScanCell scanCell));
        Assert.True(grid.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel voxel));

        Assert.Empty(scanCell.GetOccupants());
        Assert.Empty(scanCell.GetConditionalOccupants());
        Assert.Empty(scanCell.GetOccupantsFor(voxel.WorldIndex));
        Assert.False(scanCell.TryGetOccupantAt(voxel.WorldIndex, default, out IVoxelOccupant missingOccupant));
        Assert.Null(missingOccupant);
    }

    [Fact]
    public void ScanCell_InternalOperations_ShouldReturnEmptyOrFalseForMissingBucketsAndTickets()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        TestOccupant occupant = new(new Vector3d(1, 0, 1), 3);

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.True(grid.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel occupiedVoxel));
        Assert.True(grid.TryGetVoxel(new Vector3d(2, 0, 2), out Voxel emptyVoxel));
        Assert.True(grid.TryGetScanCell(occupant.Position, out ScanCell scanCell));

        List<IVoxelOccupant> missingBucket = scanCell.GetOccupantsFor(emptyVoxel.WorldIndex).ToList();

        Assert.Empty(missingBucket);
        Assert.False(scanCell.TryRemoveOccupant(emptyVoxel.WorldIndex, default));
        Assert.False(scanCell.TryRemoveOccupant(occupiedVoxel.WorldIndex, default));
        Assert.Single(scanCell.GetOccupantsFor(occupiedVoxel.WorldIndex));
    }

    [Fact]
    public void TryGetOccupantAt_ShouldReturnFalseForRemovedOrInvalidTickets()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        TestOccupant occupant = new(new Vector3d(1, 0, 1), 7);

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.True(grid.TryGetVoxel(occupant.Position, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(occupant.Position, out ScanCell scanCell));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(_world, occupant, voxel.WorldIndex, out OccupantTicket ticket));

        Assert.True(scanCell.TryGetOccupantAt(voxel.WorldIndex, ticket, out IVoxelOccupant resolvedOccupant));
        Assert.Same(occupant, resolvedOccupant);

        Assert.True(grid.TryRemoveVoxelOccupant(occupant));

        Assert.False(scanCell.TryGetOccupantAt(voxel.WorldIndex, ticket, out IVoxelOccupant removedOccupant));
        Assert.Null(removedOccupant);
        Assert.False(scanCell.TryGetOccupantAt(
            voxel.WorldIndex,
            new OccupantTicket(ticket.Slot, ticket.Generation + 1),
            out IVoxelOccupant invalidTicketOccupant));
        Assert.Null(invalidTicketOccupant);
    }

    [Fact]
    public void StaleTicket_ShouldNotResolveOrRemoveReplacementOccupantInReusedSlot()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        TestOccupant firstOccupant = new(Vector3d.Zero, 1);
        TestOccupant replacementOccupant = new(Vector3d.Zero, 2);

        Assert.True(grid.TryAddVoxelOccupant(firstOccupant));
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(Vector3d.Zero, out ScanCell scanCell));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            _world,
            firstOccupant,
            voxel.WorldIndex,
            out OccupantTicket staleTicket));
        Assert.True(grid.TryRemoveVoxelOccupant(firstOccupant));

        Assert.True(grid.TryAddVoxelOccupant(replacementOccupant));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            _world,
            replacementOccupant,
            voxel.WorldIndex,
            out OccupantTicket currentTicket));
        Assert.Equal(staleTicket.Slot, currentTicket.Slot);
        Assert.NotEqual(staleTicket, currentTicket);

        Assert.False(scanCell.TryGetOccupantAt(voxel.WorldIndex, staleTicket, out _));
        Assert.True(scanCell.TryGetOccupantAt(voxel.WorldIndex, currentTicket, out IVoxelOccupant currentOccupant));
        Assert.Same(replacementOccupant, currentOccupant);
        Assert.False(scanCell.TryRemoveOccupant(voxel.WorldIndex, staleTicket));
        Assert.True(scanCell.TryRemoveOccupant(voxel.WorldIndex, currentTicket));
    }

    [Fact]
    public void FirstTicket_ShouldStayStaleAfterSameOccupantIsRemovedAndReadded()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        TestOccupant occupant = new(Vector3d.Zero, 1);

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(Vector3d.Zero, out ScanCell scanCell));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            _world,
            occupant,
            voxel.WorldIndex,
            out OccupantTicket firstTicket));
        Assert.True(grid.TryRemoveVoxelOccupant(occupant));

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            _world,
            occupant,
            voxel.WorldIndex,
            out OccupantTicket secondTicket));
        Assert.Equal(firstTicket.Slot, secondTicket.Slot);
        Assert.NotEqual(firstTicket, secondTicket);

        Assert.False(scanCell.TryGetOccupantAt(voxel.WorldIndex, firstTicket, out _));
        Assert.True(scanCell.TryGetOccupantAt(voxel.WorldIndex, secondTicket, out IVoxelOccupant currentOccupant));
        Assert.Same(occupant, currentOccupant);
    }

    [Theory]
    [InlineData(GridStorageKind.Dense)]
    [InlineData(GridStorageKind.Sparse)]
    public async Task TryGetOccupantAt_ShouldSynchronizeWithGridOccupantMutations(GridStorageKind storageKind)
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            storageKind: storageKind);
        Assert.True(_world.TryAddGrid(configuration, new[] { new VoxelIndex(0, 0, 0) }, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        TestOccupant occupant = new(Vector3d.Zero, 1);

        Assert.True(grid.TryAddVoxelOccupant(occupant));
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(Vector3d.Zero, out ScanCell scanCell));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(
            _world,
            occupant,
            voxel.WorldIndex,
            out OccupantTicket ticket));

        using ManualResetEventSlim lookupStarted = new();
        using ManualResetEventSlim lookupFinished = new();
        Task<(bool Success, IVoxelOccupant Occupant)> lookupTask;
        lock (grid.OccupantSyncRoot)
        {
            lookupTask = Task.Run(() =>
            {
                lookupStarted.Set();
                try
                {
                    bool success = scanCell.TryGetOccupantAt(voxel.WorldIndex, ticket, out IVoxelOccupant resolved);
                    return (success, resolved);
                }
                finally
                {
                    lookupFinished.Set();
                }
            });

            Assert.True(lookupStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(
                lookupFinished.Wait(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken),
                "Exact lookup completed while the grid occupant mutation lock was held.");
        }

        (bool success, IVoxelOccupant resolvedOccupant) = await lookupTask;
        Assert.True(success);
        Assert.Same(occupant, resolvedOccupant);
    }

    [Fact]
    public async Task ScanCellReset_ShouldSynchronizeWithGridOccupantLookups()
    {
        GridConfiguration configuration = new(Vector3d.Zero, Vector3d.Zero);
        Assert.True(_world.TryAddGrid(configuration, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        TestOccupant occupant = new(Vector3d.Zero, 1);
        Assert.True(grid.TryAddVoxelOccupant(occupant));

        using ManualResetEventSlim resetStarted = new();
        using ManualResetEventSlim resetFinished = new();
        Task<bool> resetTask;
        lock (grid.OccupantSyncRoot)
        {
            resetTask = Task.Run(() =>
            {
                resetStarted.Set();
                try
                {
                    return _world.TryRemoveGrid(gridIndex);
                }
                finally
                {
                    resetFinished.Set();
                }
            });

            Assert.True(resetStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(
                resetFinished.Wait(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken),
                "Scan-cell reset completed while the grid occupant lookup lock was held.");
        }

        Assert.True(await resetTask);
    }

    [Fact]
    public void AddOccupant_ShouldFailBeforeStorageMutationWhenGenerationIsExhausted()
    {
        _world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        TestOccupant occupant = new(Vector3d.Zero, 1);

        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(Vector3d.Zero, out ScanCell scanCell));

        FieldInfo counterField = typeof(ScanCell).GetField(
            "s_occupantGenerationCounter",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find the occupant generation counter.");
        long previousCounter = (long)counterField.GetValue(null);

        try
        {
            counterField.SetValue(null, long.MaxValue);
            Assert.Throws<InvalidOperationException>(() => grid.TryAddVoxelOccupant(occupant));
        }
        finally
        {
            counterField.SetValue(null, previousCounter);
        }

        Assert.False(voxel.IsOccupied);
        Assert.Equal(0, voxel.OccupantCount);
        Assert.False(scanCell.IsOccupied);
        Assert.Equal(0, scanCell.CellOccupantCount);
        Assert.Empty(scanCell.GetOccupants());
        Assert.Null(grid.ActiveScanCells);
        Assert.False(GridOccupantManager.TryGetOccupancyTicket(
            _world,
            occupant,
            voxel.WorldIndex,
            out OccupantTicket ticket));
        Assert.False(ticket.IsValid);
    }

    [Fact]
    public void ScanRadius_ShouldRespectOccupantConditionAcrossScanCellBoundaries()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(20, 0, 20), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        TestOccupant leftCellOccupant = new(new Vector3d(7, 0, 7), 1);
        TestOccupant rightCellOccupant = new(new Vector3d(8, 0, 8), 1);
        TestOccupant distantOccupant = new(new Vector3d(14, 0, 14), 1);

        grid.TryAddVoxelOccupant(leftCellOccupant);
        grid.TryAddVoxelOccupant(rightCellOccupant);
        grid.TryAddVoxelOccupant(distantOccupant);

        List<IVoxelOccupant> filteredResults = GridScanManager.ScanRadius(_world,
            Vector3d.FromDouble(7.5, 0, 7.5),
            (Fixed64)2,
            occupantCondition: occupant => occupant.Position.X >= (Fixed64)8)
            .ToList();

        Assert.DoesNotContain(leftCellOccupant, filteredResults);
        Assert.Contains(rightCellOccupant, filteredResults);
        Assert.DoesNotContain(distantOccupant, filteredResults);
    }

    [Fact]
    public void OccupantOperations_ShouldRemainConsistentUnderConcurrentLoad()
    {
        _world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(15, 0, 15), scanCellSize: 8),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d position = new(2, 0, 2);

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

    [Fact]
    public void ConcurrentOccupantAdds_ShouldStopAtVoxelCapacityWithoutCountOverflow()
    {
        _world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort gridIndex);
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(Vector3d.Zero, out Voxel voxel));
        Assert.True(grid.TryGetScanCell(Vector3d.Zero, out ScanCell scanCell));

        TestOccupant[] occupants = Enumerable.Range(0, GridOccupantManager.MaxOccupantCount + 64)
            .Select(_ => new TestOccupant(Vector3d.Zero))
            .ToArray();
        bool[] addResults = new bool[occupants.Length];

        Parallel.For(0, occupants.Length, i => addResults[i] = grid.TryAddVoxelOccupant(voxel, occupants[i]));

        Assert.Equal(GridOccupantManager.MaxOccupantCount, addResults.Count(result => result));
        Assert.Equal(GridOccupantManager.MaxOccupantCount, voxel.OccupantCount);
        Assert.Equal(GridOccupantManager.MaxOccupantCount, scanCell.CellOccupantCount);
        Assert.Equal(GridOccupantManager.MaxOccupantCount, scanCell.GetOccupantsFor(voxel.WorldIndex).Count());
    }

    [Fact]
    public void Radius2dQueries_ShouldNoOpWhenScanCellIsUninitialized()
    {
        ScanCell scanCell = new ScanCell();
        SwiftList<IVoxelOccupant> untypedResults = new SwiftList<IVoxelOccupant>();
        SwiftList<TestOccupant> typedResults = new SwiftList<TestOccupant>();

        scanCell.AddOccupantsWithinRadius2dTo(
            untypedResults,
            Vector3d.Zero,
            localLayerY: 0,
            squaredRadius: Fixed64.One);
        scanCell.AddOccupantsWithinRadius2dTo<TestOccupant>(
            typedResults,
            Vector3d.Zero,
            localLayerY: 0,
            squaredRadius: Fixed64.One);

        Assert.Empty(untypedResults);
        Assert.Empty(typedResults);
    }

}
