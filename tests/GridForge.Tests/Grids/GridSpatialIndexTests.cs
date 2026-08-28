using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using SwiftCollections;
using SwiftCollections.Query;
using Xunit;

namespace GridForge.Grids.Tests;

public sealed class GridSpatialIndexTests
{
    [Fact]
    public void Insert_ShouldPlaceEachSlotInExactlyOneTier()
    {
        var index = new GridSpatialIndex(50);

        Assert.True(index.Insert(1, CreateXAxisBounds(3_150)));
        Assert.True(index.Insert(2, CreateXAxisBounds(3_200)));

        Assert.Equal(2, index.Count);
        Assert.Equal(1, index.OrdinaryCount);
        Assert.Equal(1, index.OversizedCount);
    }

    [Theory]
    [InlineData(3_150, 3_200, 1, 0)]
    [InlineData(3_200, 3_150, 0, 1)]
    public void Insert_WithDuplicateSlot_ShouldPreserveTheOriginalTier(
        int originalMaximum,
        int replacementMaximum,
        int expectedOrdinaryCount,
        int expectedOversizedCount)
    {
        var index = new GridSpatialIndex(50, 64UL);
        Assert.True(index.Insert(1, CreateXAxisBounds(originalMaximum)));

        Assert.False(index.Insert(1, CreateXAxisBounds(replacementMaximum)));

        Assert.Equal(1, index.Count);
        Assert.Equal(expectedOrdinaryCount, index.OrdinaryCount);
        Assert.Equal(expectedOversizedCount, index.OversizedCount);
    }

    [Fact]
    public void Remove_ShouldReleaseTheRecordedTier()
    {
        var index = new GridSpatialIndex(50, 64UL);
        Assert.True(index.Insert(1, CreateXAxisBounds(3_150)));
        Assert.True(index.Insert(2, CreateXAxisBounds(3_200)));

        Assert.True(index.Remove(1));
        Assert.True(index.Remove(2));
        Assert.False(index.Remove(2));

        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.OrdinaryCount);
        Assert.Equal(0, index.OversizedCount);
    }

    [Fact]
    public void ContactEnvelopeQuery_ShouldNotTraverseHugeSpatialHashVolume()
    {
        var index = new GridSpatialIndex(1);
        var contactEnvelope = new FixedBoundVolume(
            new Vector3d(-1_000_000, -1_000_000, -1_000_000),
            new Vector3d(1_000_000, 1_000_000, 1_000_000));
        Assert.True(index.Insert(
            7,
            new FixedBoundVolume(Vector3d.Zero, Vector3d.Zero),
            contactEnvelope));
        SwiftList<ushort> candidates = new SwiftList<ushort>(1);

        index.CollectContactCandidates(contactEnvelope, candidates);

        Assert.Equal(new ushort[] { 7 }, candidates);
    }

    [Fact]
    public void MissingContactEnvelope_ShouldNeverProduceOrRetainContactCandidates()
    {
        var index = new GridSpatialIndex(1);
        var candidates = new SwiftList<ushort> { ushort.MaxValue };
        FixedBoundVolume bounds = new FixedBoundVolume(Vector3d.Zero, Vector3d.Zero);

        Assert.True(index.Insert(7, bounds, contactEnvelope: null));
        index.CollectContactCandidates(bounds, candidates);
        Assert.Empty(candidates);

        Assert.True(index.Remove(7));
        candidates.Add(ushort.MaxValue);
        index.CollectContactCandidates(bounds, candidates);
        Assert.Empty(candidates);
    }

    [Fact]
    public void ContactEnvelopeRemoval_ShouldRemoveOnlyTheMatchingSlot()
    {
        var index = new GridSpatialIndex(1);
        FixedBoundVolume bounds = new FixedBoundVolume(Vector3d.Zero, Vector3d.Zero);
        var candidates = new SwiftList<ushort>();
        Assert.True(index.Insert(2, bounds, bounds));
        Assert.True(index.Insert(1, bounds, bounds));

        Assert.True(index.Remove(2));
        index.CollectContactCandidates(bounds, candidates);

        Assert.Equal(new ushort[] { 1 }, candidates);
    }

    [Fact]
    public void Clear_ShouldReleaseBothTiersForReuse()
    {
        var index = new GridSpatialIndex(50, 64UL);
        Assert.True(index.Insert(1, CreateXAxisBounds(3_150)));
        Assert.True(index.Insert(2, CreateXAxisBounds(3_200)));

        index.Clear();

        Assert.Equal(0, index.Count);
        Assert.True(index.Insert(1, CreateXAxisBounds(3_200)));
        Assert.Equal(0, index.OrdinaryCount);
        Assert.Equal(1, index.OversizedCount);
    }

    [Fact]
    public void CollectCandidates_ShouldMergeBothTiersInAscendingSlotOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(1);
        VoxelGrid oversizedGrid = AddSparseGrid(world, Vector3d.Zero, Vector3d.One);
        VoxelGrid ordinaryGrid = AddSparseGrid(world, Vector3d.One, Vector3d.One);
        var index = new GridSpatialIndex(1, 1UL);
        Assert.True(index.Insert(ordinaryGrid.GridIndex, GetBounds(ordinaryGrid)));
        Assert.True(index.Insert(oversizedGrid.GridIndex, GetBounds(oversizedGrid)));
        var candidates = new SwiftList<ushort>();

        index.CollectCandidates(
            new FixedBoundVolume(Vector3d.One, Vector3d.One),
            world.ActiveGrids,
            candidates);

        Assert.Equal(new[] { oversizedGrid.GridIndex, ordinaryGrid.GridIndex }, candidates);

        Assert.True(index.Remove(ordinaryGrid.GridIndex));
        index.CollectCandidates(
            new FixedBoundVolume(Vector3d.One, Vector3d.One),
            world.ActiveGrids,
            candidates);

        Assert.Equal(new[] { oversizedGrid.GridIndex }, candidates);
    }

    [Fact]
    public void CollectPointCandidates_ShouldMergeBothTiersInAscendingSlotOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(1);
        VoxelGrid oversizedGrid = AddSparseGrid(world, Vector3d.Zero, Vector3d.One);
        VoxelGrid ordinaryGrid = AddSparseGrid(world, Vector3d.One, Vector3d.One);
        var index = new GridSpatialIndex(1, 1UL);
        Assert.True(index.Insert(ordinaryGrid.GridIndex, GetBounds(ordinaryGrid)));
        Assert.True(index.Insert(oversizedGrid.GridIndex, GetBounds(oversizedGrid)));
        var candidates = new SwiftList<ushort> { ushort.MaxValue };

        index.CollectPointCandidates(Vector3d.One, candidates);

        Assert.Equal(new[] { oversizedGrid.GridIndex, ordinaryGrid.GridIndex }, candidates);
    }

    [Fact]
    public void EmptyIndexQueries_ShouldClearCallerOwnedCandidateStorage()
    {
        var index = new GridSpatialIndex(1);
        var candidates = new SwiftList<ushort> { ushort.MaxValue };

        index.CollectPointCandidates(Vector3d.Zero, candidates);
        Assert.Empty(candidates);

        candidates.Add(ushort.MaxValue);
        index.CollectContactCandidates(
            new FixedBoundVolume(Vector3d.Zero, Vector3d.Zero),
            candidates);
        Assert.Empty(candidates);
    }

    [Fact]
    public void CollectCandidates_WhenQueryVolumeExceedsActiveCount_ShouldScanAndFilterExactBounds()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(1);
        VoxelGrid nearGrid = AddSparseGrid(world, Vector3d.Zero, Vector3d.Zero);
        VoxelGrid farGrid = AddSparseGrid(world, new Vector3d(100, 100, 100), new Vector3d(100, 100, 100));
        var index = new GridSpatialIndex(1, 4_096UL);
        Assert.True(index.Insert(nearGrid.GridIndex, GetBounds(nearGrid)));
        Assert.True(index.Insert(farGrid.GridIndex, GetBounds(farGrid)));
        var candidates = new SwiftList<ushort> { ushort.MaxValue };

        index.CollectCandidates(
            new FixedBoundVolume(new Vector3d(-10, -10, -10), new Vector3d(10, 10, 10)),
            world.ActiveGrids,
            candidates);

        Assert.Equal(new[] { nearGrid.GridIndex }, candidates);
    }

    [Fact]
    public void CollectCandidates_AtMaximumHashCell_ShouldCompleteSafely()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(1);
        VoxelGrid grid = AddSparseGrid(world, Vector3d.Zero, Vector3d.Zero);
        var index = new GridSpatialIndex(1, 4_096UL);
        Assert.True(index.Insert(grid.GridIndex, GetBounds(grid)));
        var candidates = new SwiftList<ushort> { ushort.MaxValue };
        FixedBoundVolume query = CreateUniformBounds(Fixed64.MaxValue, Fixed64.MaxValue);

        index.CollectCandidates(query, world.ActiveGrids, candidates);

        Assert.Empty(candidates);
    }

    [Fact]
    public void CollectCandidates_WithNoActiveGrids_ShouldClearTheDestination()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        var index = new GridSpatialIndex(50, 4_096UL);
        var candidates = new SwiftList<ushort> { ushort.MaxValue };

        index.CollectCandidates(
            new FixedBoundVolume(Vector3d.Zero, Vector3d.Zero),
            world.ActiveGrids,
            candidates);

        Assert.Empty(candidates);
    }

    [Theory]
    [InlineData(0, 99, 0, 1)]
    [InlineData(-51, -1, -2, -1)]
    [InlineData(-50, 49, -1, 0)]
    [InlineData(99, 0, 0, 1)]
    [InlineData(0, 0, 0, 0)]
    public void GetCellRange_ShouldMatchFixedSpatialHashFloorContract(
        int first,
        int second,
        int expectedMin,
        int expectedMax)
    {
        FixedBoundVolume bounds = CreateUniformBounds(first, second);

        new GridSpatialIndex(50).GetCellRange(
            bounds,
            out SwiftSpatialHashCellIndex minCell,
            out SwiftSpatialHashCellIndex maxCell);

        Assert.Equal(new SwiftSpatialHashCellIndex(expectedMin, expectedMin, expectedMin), minCell);
        Assert.Equal(new SwiftSpatialHashCellIndex(expectedMax, expectedMax, expectedMax), maxCell);
    }

    [Fact]
    public void GetCellRange_WithFullDomainBounds_ShouldNotNarrowTheCellCoordinates()
    {
        FixedBoundVolume bounds = CreateUniformBounds(Fixed64.MinValue, Fixed64.MaxValue);

        new GridSpatialIndex(50).GetCellRange(
            bounds,
            out SwiftSpatialHashCellIndex minCell,
            out SwiftSpatialHashCellIndex maxCell);

        Assert.Equal(new SwiftSpatialHashCellIndex(-42_949_673, -42_949_673, -42_949_673), minCell);
        Assert.Equal(new SwiftSpatialHashCellIndex(42_949_672, 42_949_672, 42_949_672), maxCell);
    }

    [Fact]
    public void GetCellRange_ImmediatelyBeforeBoundary_ShouldUseExactRawFloor()
    {
        Fixed64 coordinate = Fixed64.FromRaw(((long)50 << 32) - 1L);
        FixedBoundVolume bounds = CreateUniformBounds(coordinate, coordinate);

        new GridSpatialIndex(50).GetCellRange(
            bounds,
            out SwiftSpatialHashCellIndex minCell,
            out SwiftSpatialHashCellIndex maxCell);

        Assert.Equal(new SwiftSpatialHashCellIndex(0, 0, 0), minCell);
        Assert.Equal(minCell, maxCell);
    }

    [Theory]
    [InlineData(3_100, true)]
    [InlineData(3_150, true)]
    [InlineData(3_200, false)]
    public void FitsHashCellBudget_ShouldClassifyThresholdBoundariesWithoutOverflow(
        int maximum,
        bool expected)
    {
        FixedBoundVolume bounds = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)maximum, Fixed64.Zero, Fixed64.Zero));

        Assert.Equal(
            expected,
            new GridSpatialIndex(50, 64UL).FitsHashCellBudget(bounds));
    }

    [Fact]
    public void FitsHashCellBudget_WithFullDomainBounds_ShouldReturnFalse()
    {
        FixedBoundVolume bounds = CreateUniformBounds(Fixed64.MinValue, Fixed64.MaxValue);

        Assert.False(new GridSpatialIndex(50, 4_096UL).FitsHashCellBudget(bounds));
    }

    [Theory]
    [InlineData(4, 4, 4, true)]
    [InlineData(4, 4, 5, false)]
    [InlineData(4, 17, 1, false)]
    public void FitsHashCellBudget_ShouldCompareThreeDimensionalProductsWithoutOverflow(
        int xCells,
        int yCells,
        int zCells,
        bool expected)
    {
        var bounds = new FixedBoundVolume(
            Vector3d.Zero,
            new Vector3d(
                (Fixed64)((xCells - 1) * 50),
                (Fixed64)((yCells - 1) * 50),
                (Fixed64)((zCells - 1) * 50)));

        Assert.Equal(expected, new GridSpatialIndex(50, 64UL).FitsHashCellBudget(bounds));
    }

    [Fact]
    public void FitsHashCellBudget_WithZeroBudget_ShouldReturnFalse()
    {
        Assert.False(new GridSpatialIndex(50, 0UL).FitsHashCellBudget(
            new FixedBoundVolume(Vector3d.Zero, Vector3d.Zero)));
    }

    [Fact]
    public void FitsHashCellBudget_WhenMaximumCellIsWithinBudget_ShouldReturnTrue()
    {
        FixedBoundVolume bounds = CreateUniformBounds(Fixed64.MaxValue, Fixed64.MaxValue);

        Assert.True(new GridSpatialIndex(1, 4_096UL).FitsHashCellBudget(bounds));
    }

    [Theory]
    [InlineData(1, 1, 1, 1, false)]
    [InlineData(2, 1, 1, 1, true)]
    [InlineData(2, 2, 1, 3, true)]
    [InlineData(2, 2, 2, 7, true)]
    [InlineData(2, 2, 2, 8, false)]
    public void ShouldScanActiveGrids_ShouldCompareVolumeWithoutOverflow(
        int xCells,
        int yCells,
        int zCells,
        int activeGridCount,
        bool expected)
    {
        var bounds = new FixedBoundVolume(
            Vector3d.Zero,
            new Vector3d(xCells - 1, yCells - 1, zCells - 1));

        Assert.Equal(
            expected,
            new GridSpatialIndex(1).ShouldScanActiveGrids(bounds, activeGridCount));
    }

    private static FixedBoundVolume CreateUniformBounds(int first, int second) =>
        CreateUniformBounds((Fixed64)first, (Fixed64)second);

    private static FixedBoundVolume CreateUniformBounds(Fixed64 first, Fixed64 second) =>
        new(
            new Vector3d(first, first, first),
            new Vector3d(second, second, second));

    private static FixedBoundVolume CreateXAxisBounds(int maximum) =>
        new(
            Vector3d.Zero,
            new Vector3d((Fixed64)maximum, Fixed64.Zero, Fixed64.Zero));

    private static FixedBoundVolume GetBounds(VoxelGrid grid) =>
        new(grid.BoundsMin, grid.BoundsMax);

    private static VoxelGrid AddSparseGrid(GridWorld world, Vector3d min, Vector3d max)
    {
        GridConfiguration configuration = new(min, max, storageKind: GridStorageKind.Sparse);
        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));
        return world.ActiveGrids[gridIndex];
    }
}
