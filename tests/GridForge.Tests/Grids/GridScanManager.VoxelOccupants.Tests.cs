using System;
using System.Collections.Generic;
using System.Linq;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class GridScanManagerVoxelOccupantTests
{
    [Theory]
    [InlineData(GridStorageKind.Dense)]
    [InlineData(GridStorageKind.Sparse)]
    public void GetOccupants_ShouldSelectExactVoxelAcrossOverloads_WhenNeighborsShareScanCell(GridStorageKind storageKind)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = CreateSharedScanCell(world, storageKind, out Voxel target, out Voxel neighbor, out Voxel empty);
        TestOccupant first = new TestOccupant(target.WorldPosition, 1);
        TestOccupant second = new TestOccupant(target.WorldPosition, 2);
        TestOccupant unrelated = new TestOccupant(neighbor.WorldPosition, 1);
        Assert.True(grid.TryAddVoxelOccupant(target, first));
        Assert.True(grid.TryAddVoxelOccupant(target, second));
        Assert.True(grid.TryAddVoxelOccupant(neighbor, unrelated));
        first.Position = neighbor.WorldPosition;
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, first, target.WorldIndex, out OccupantTicket targetTicket));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, second, target.WorldIndex, out OccupantTicket secondTicket));
        IVoxelOccupant[] expected = targetTicket.Slot < secondTicket.Slot
            ? new IVoxelOccupant[] { first, second }
            : new IVoxelOccupant[] { second, first };

        Assert.Equal(expected, GridScanManager.GetOccupants(world, target.WorldIndex).ToArray());
        Assert.Equal(expected, grid.GetOccupants(target.WorldPosition).ToArray());
        Assert.Equal(expected, grid.GetOccupants(target.Index).ToArray());
        Assert.Equal(expected, grid.GetOccupants(target).ToArray());
        Assert.Empty(grid.GetOccupants(empty));

        Assert.True(grid.TryGetScanCell(target.ScanCellKey, out ScanCell scanCell));
        IVoxelOccupant[] wholeScanCell = scanCell.GetOccupants().ToArray();
        Assert.Equal(3, wholeScanCell.Length);
        Assert.Contains(first, wholeScanCell);
        Assert.Contains(second, wholeScanCell);
        Assert.Contains(unrelated, wholeScanCell);
        Assert.Equal(expected, scanCell.GetOccupantsFor(target.WorldIndex).ToArray());

        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, unrelated, neighbor.WorldIndex, out OccupantTicket neighborTicket));
        Assert.True(grid.TryGetVoxelOccupant(target, targetTicket, out IVoxelOccupant resolved));
        Assert.Same(first, resolved);
        Assert.False(grid.TryGetVoxelOccupant(target, neighborTicket, out IVoxelOccupant wrongBucket));
        Assert.Null(wrongBucket);
    }

    [Theory]
    [InlineData(GridStorageKind.Dense)]
    [InlineData(GridStorageKind.Sparse)]
    public void GetVoxelOccupantsByType_ShouldFilterOnlyExactVoxelAcrossOverloads(GridStorageKind storageKind)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = CreateSharedScanCell(world, storageKind, out Voxel target, out Voxel neighbor, out Voxel other);
        SelectedOccupant selected = new SelectedOccupant(target.WorldPosition);
        TestOccupant otherType = new TestOccupant(target.WorldPosition);
        SelectedOccupant unrelatedSelected = new SelectedOccupant(neighbor.WorldPosition);
        TestOccupant nonmatchingVoxelOccupant = new TestOccupant(other.WorldPosition);
        Assert.True(grid.TryAddVoxelOccupant(target, selected));
        Assert.True(grid.TryAddVoxelOccupant(target, otherType));
        Assert.True(grid.TryAddVoxelOccupant(neighbor, unrelatedSelected));
        Assert.True(grid.TryAddVoxelOccupant(other, nonmatchingVoxelOccupant));

        Assert.Same(selected, Assert.Single(GridScanManager.GetVoxelOccupantsByType<SelectedOccupant>(world, target.WorldIndex)));
        Assert.Same(selected, Assert.Single(grid.GetVoxelOccupantsByType<SelectedOccupant>(target.WorldPosition)));
        Assert.Same(selected, Assert.Single(grid.GetVoxelOccupantsByType<SelectedOccupant>(target.Index)));
        Assert.Same(selected, Assert.Single(grid.GetVoxelOccupantsByType<SelectedOccupant>(target)));
        AssertContainsExactly(new IVoxelOccupant[] { selected, otherType }, grid.GetVoxelOccupantsByType<TestOccupant>(target));
        Assert.Empty(grid.GetVoxelOccupantsByType<SelectedOccupant>(other));
    }

    [Theory]
    [InlineData(GridStorageKind.Dense)]
    [InlineData(GridStorageKind.Sparse)]
    public void GetConditionalOccupants_ShouldFilterOnlyExactVoxelAcrossOverloads(GridStorageKind storageKind)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = CreateSharedScanCell(world, storageKind, out Voxel target, out Voxel neighbor, out Voxel empty);
        TestOccupant matching = new TestOccupant(target.WorldPosition, 7);
        TestOccupant excluded = new TestOccupant(target.WorldPosition, 8);
        TestOccupant unrelatedMatch = new TestOccupant(neighbor.WorldPosition, 7);
        Assert.True(grid.TryAddVoxelOccupant(target, matching));
        Assert.True(grid.TryAddVoxelOccupant(target, excluded));
        Assert.True(grid.TryAddVoxelOccupant(neighbor, unrelatedMatch));
        SwiftList<IVoxelOccupant> inspected = new SwiftList<IVoxelOccupant>();
        Func<IVoxelOccupant, bool> occupantCondition = occupant =>
        {
            inspected.Add(occupant);
            return true;
        };
        Func<byte, bool> groupCondition = group => group == 7;

        Assert.Same(matching, Assert.Single(GridScanManager.GetConditionalOccupants(
            world, target.WorldIndex, occupantCondition, groupCondition)));
        AssertContainsExactly(new IVoxelOccupant[] { matching, excluded }, inspected);
        Assert.Same(matching, Assert.Single(grid.GetConditionalOccupants(target.WorldPosition, groupCondition: groupCondition)));
        Assert.Same(matching, Assert.Single(grid.GetConditionalOccupants(target.Index, groupCondition: groupCondition)));
        Assert.Same(matching, Assert.Single(grid.GetConditionalOccupants(target, groupCondition: groupCondition)));
        Assert.Empty(grid.GetConditionalOccupants(empty));

        Assert.True(grid.TryGetScanCell(target.ScanCellKey, out ScanCell scanCell));
        IVoxelOccupant[] wholeScanCellMatches = scanCell.GetConditionalOccupants(groupConditional: groupCondition).ToArray();
        Assert.Equal(2, wholeScanCellMatches.Length);
        Assert.Contains(matching, wholeScanCellMatches);
        Assert.Contains(unrelatedMatch, wholeScanCellMatches);
        AssertContainsExactly(
            new IVoxelOccupant[] { matching, unrelatedMatch },
            scanCell.GetConditionalOccupants(
                occupantCondition: occupant => !ReferenceEquals(occupant, excluded),
                groupConditional: group =>
                {
                    Assert.NotEqual((byte)8, group);
                    return group == 7;
                }));
    }

    [Theory]
    [InlineData(GridStorageKind.Dense)]
    [InlineData(GridStorageKind.Sparse)]
    public void VoxelOccupantQueries_ShouldStayIsolatedAfterBucketRemovalAndReregistration(GridStorageKind storageKind)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = CreateSharedScanCell(world, storageKind, out Voxel target, out Voxel neighbor, out _);
        TestOccupant removed = new TestOccupant(target.WorldPosition, 1);
        TestOccupant unrelated = new TestOccupant(neighbor.WorldPosition, 1);
        Assert.True(grid.TryAddVoxelOccupant(target, removed));
        Assert.True(grid.TryAddVoxelOccupant(neighbor, unrelated));
        Assert.True(GridOccupantManager.TryGetOccupancyTicket(world, removed, target.WorldIndex, out OccupantTicket staleTicket));

        Assert.True(grid.TryRemoveVoxelOccupant(target, removed));
        Assert.Empty(grid.GetOccupants(target));
        Assert.Empty(grid.GetConditionalOccupants(target));
        Assert.Empty(grid.GetVoxelOccupantsByType<TestOccupant>(target));
        Assert.Same(unrelated, Assert.Single(grid.GetOccupants(neighbor)));

        TestOccupant replacement = new TestOccupant(target.WorldPosition, 1);
        Assert.True(grid.TryAddVoxelOccupant(target, replacement));
        Assert.Same(replacement, Assert.Single(grid.GetOccupants(target)));
        Assert.Same(replacement, Assert.Single(grid.GetConditionalOccupants(target)));
        Assert.Same(replacement, Assert.Single(grid.GetVoxelOccupantsByType<TestOccupant>(target)));
        Assert.False(grid.TryGetVoxelOccupant(target, staleTicket, out IVoxelOccupant staleResult));
        Assert.Null(staleResult);
    }

    [Fact]
    public void ConditionalVoxelBucketLookup_ShouldSkipMissingStorageAndIdentitiesWithoutInvokingPredicates()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = CreateSharedScanCell(world, GridStorageKind.Dense, out Voxel target, out Voxel neighbor, out _);
        Assert.True(grid.TryGetScanCell(target.ScanCellKey, out ScanCell scanCell));
        Func<IVoxelOccupant, bool> unexpectedOccupant = _ => throw new InvalidOperationException("No matching occupant should be inspected.");
        Func<byte, bool> unexpectedGroup = _ => throw new InvalidOperationException("No matching group should be inspected.");

        Assert.Empty(scanCell.GetConditionalOccupantsFor(target.WorldIndex, unexpectedOccupant, unexpectedGroup));

        TestOccupant unrelated = new TestOccupant(neighbor.WorldPosition, 1);
        Assert.True(grid.TryAddVoxelOccupant(neighbor, unrelated));
        Assert.Empty(scanCell.GetConditionalOccupantsFor(target.WorldIndex, unexpectedOccupant, unexpectedGroup));

        TestOccupant matching = new TestOccupant(target.WorldPosition, 1);
        Assert.True(grid.TryAddVoxelOccupant(target, matching));
        WorldVoxelIndex wrongGeneration = target.WorldIndex;
        wrongGeneration.GridSpawnToken = 0;
        Assert.Empty(scanCell.GetConditionalOccupantsFor(wrongGeneration, unexpectedOccupant, unexpectedGroup));
        Assert.Same(matching, Assert.Single(scanCell.GetConditionalOccupantsFor(target.WorldIndex, null, null)));
    }

    [Fact]
    public void ConditionalVoxelBucketLookup_ShouldApplyOccupantBeforeGroupAndShortCircuitRejectedOccupants()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = CreateSharedScanCell(world, GridStorageKind.Dense, out Voxel target, out Voxel neighbor, out _);
        TestOccupant rejected = new TestOccupant(target.WorldPosition, 1);
        TestOccupant wrongGroup = new TestOccupant(target.WorldPosition, 2);
        TestOccupant matching = new TestOccupant(target.WorldPosition, 3);
        TestOccupant unrelatedMatch = new TestOccupant(neighbor.WorldPosition, 3);
        Assert.True(grid.TryAddVoxelOccupant(target, rejected));
        Assert.True(grid.TryAddVoxelOccupant(target, wrongGroup));
        Assert.True(grid.TryAddVoxelOccupant(target, matching));
        Assert.True(grid.TryAddVoxelOccupant(neighbor, unrelatedMatch));
        Assert.True(grid.TryGetScanCell(target.ScanCellKey, out ScanCell scanCell));
        SwiftList<IVoxelOccupant> inspectedOccupants = new SwiftList<IVoxelOccupant>();
        SwiftList<byte> inspectedGroups = new SwiftList<byte>();

        IVoxelOccupant result = Assert.Single(scanCell.GetConditionalOccupantsFor(
            target.WorldIndex,
            occupant =>
            {
                inspectedOccupants.Add(occupant);
                return !ReferenceEquals(occupant, rejected);
            },
            group =>
            {
                inspectedGroups.Add(group);
                return group == 3;
            }));

        Assert.Same(matching, result);
        AssertContainsExactly(new IVoxelOccupant[] { rejected, wrongGroup, matching }, inspectedOccupants);
        Assert.Equal(2, inspectedGroups.Count);
        Assert.Contains((byte)2, inspectedGroups);
        Assert.Contains((byte)3, inspectedGroups);
    }

    private static void AssertContainsExactly(IVoxelOccupant[] expected, IEnumerable<IVoxelOccupant> actual)
    {
        IVoxelOccupant[] occupants = actual.ToArray();
        Assert.Equal(expected.Length, occupants.Length);
        foreach (IVoxelOccupant expectedOccupant in expected)
            Assert.Contains(expectedOccupant, occupants);
    }

    private static VoxelGrid CreateSharedScanCell(
        GridWorld world,
        GridStorageKind storageKind,
        out Voxel target,
        out Voxel neighbor,
        out Voxel empty)
    {
        GridConfiguration configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(3, 1, 0),
            scanCellSize: 8,
            storageKind: storageKind);
        VoxelIndex targetIndex = new VoxelIndex(0, 0, 0);
        VoxelIndex neighborIndex = new VoxelIndex(1, 1, 0);
        VoxelIndex emptyIndex = new VoxelIndex(2, 0, 0);
        VoxelIndex[] configured = { targetIndex, neighborIndex, emptyIndex };
        Assert.True(world.TryAddGrid(configuration, configured, out ushort gridIndex));
        Assert.True(world.TryGetGrid(gridIndex, out VoxelGrid grid));
        Assert.True(grid.TryGetVoxel(targetIndex, out target));
        Assert.True(grid.TryGetVoxel(neighborIndex, out neighbor));
        Assert.True(grid.TryGetVoxel(emptyIndex, out empty));
        Assert.Equal(target.ScanCellKey, neighbor.ScanCellKey);
        Assert.Equal(target.ScanCellKey, empty.ScanCellKey);
        return grid;
    }

    private sealed class SelectedOccupant : TestOccupant
    {
        public SelectedOccupant(Vector3d position) : base(position) { }
    }
}
