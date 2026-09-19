using System;
using System.Linq;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Query;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridScanCoverageOrderTests
{
    [Theory]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    public void CoveredScanCells_ShouldPreserveGridThenXyzOrderAcrossOverlappingGridsAndScratchReuse(
        GridStorageKind storageKind,
        GridTopologyKind topologyKind,
        HexOrientation orientation)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelIndex[] occupiedIndices = storageKind == GridStorageKind.Sparse
            ? new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(0, 2, 2), new VoxelIndex(2, 0, 0), new VoxelIndex(2, 2, 2) }
            : new[]
            {
                new VoxelIndex(0, 0, 0), new VoxelIndex(0, 0, 2),
                new VoxelIndex(0, 2, 0), new VoxelIndex(0, 2, 2),
                new VoxelIndex(2, 0, 0), new VoxelIndex(2, 0, 2),
                new VoxelIndex(2, 2, 0), new VoxelIndex(2, 2, 2)
            };
        int[] expectedKeys = storageKind == GridStorageKind.Sparse
            ? new[] { 0, 6, 1, 7 }
            : new[] { 0, 4, 2, 6, 1, 5, 3, 7 };
        VoxelGrid first = AddCoverageGrid(world, Vector3d.Zero, storageKind, topologyKind, orientation, occupiedIndices);
        VoxelGrid second = AddCoverageGrid(world, new Vector3d(0, 1, 0), storageKind, topologyKind, orientation, occupiedIndices);
        Assert.Equal((3, 3, 3), (first.Width, first.Height, first.Length));
        Assert.Equal((3, 3, 3), (second.Width, second.Height, second.Length));
        Assert.True(first.BoundsMax.Y > second.BoundsMin.Y);

        TestOccupant[] expectedOccupants = RegisterInReverseOrder(first, occupiedIndices)
            .Concat(RegisterInReverseOrder(second, occupiedIndices)).ToArray();
        (ushort GridIndex, int CellKey)[] expectedCells = expectedKeys.Select(key => (first.GridIndex, key))
            .Concat(expectedKeys.Select(key => (second.GridIndex, key))).ToArray();
        Vector3d queryMin = new Vector3d(-20, -20, -20);
        Vector3d queryMax = new Vector3d(20, 20, 20);
        SwiftList<ScanCell> cells = new SwiftList<ScanCell>();
        SwiftList<TestOccupant> occupants = new SwiftList<TestOccupant>();
        GridScanScratch scratch = new GridScanScratch();

        Assert.Equal(expectedCells, GridTracer.GetCoveredScanCells(world, queryMin, queryMax)
            .Select(cell => (cell.GridIndex, cell.CellKey)));
        GridTracer.GetCoveredScanCellsInto(world, queryMin, queryMax, cells);
        Assert.Equal(expectedCells, cells.Select(cell => (cell.GridIndex, cell.CellKey)));

        for (int repeat = 0; repeat < 2; repeat++)
        {
            GridTracer.GetCoveredScanCellsInto(world, queryMin, queryMax, cells, scratch);
            Assert.Equal(expectedCells, cells.Select(cell => (cell.GridIndex, cell.CellKey)));
            GridScanManager.ScanRadiusInto(world, Vector3d.Zero, (Fixed64)20, occupants, scratch);
            Assert.Equal(expectedOccupants, occupants);

            // Reusing the same scratch must not retain prior candidates or results.
            Vector3d outside = new Vector3d(100, 100, 100);
            GridTracer.GetCoveredScanCellsInto(world, outside, outside, cells, scratch);
            Assert.Empty(cells);
            GridScanManager.ScanRadiusInto(world, outside, Fixed64.One, occupants, scratch);
            Assert.Empty(occupants);
        }
    }

    [Fact]
    public void CoveredScanCells_ShouldReturnEachCellOnceWhenOverlappingGridsSpanSpatialHashCells()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 2);
        VoxelGrid first = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, new Vector3d(3, 0, 0), scanCellSize: 1);
        VoxelGrid second = GridWorldTestFactory.AddGrid(world, new Vector3d(1, 0, 0), new Vector3d(4, 0, 0), scanCellSize: 1);
        // The expanded query covers 16 hash cells. More active grids force the
        // spatial-index path instead of the cheaper small-world linear scan.
        for (int i = 0; i < 16; i++)
        {
            Vector3d outside = new Vector3d(100 + i * 10, 0, 0);
            GridWorldTestFactory.AddGrid(world, new GridConfiguration(outside, outside, storageKind: GridStorageKind.Sparse));
        }
        Assert.False(new GridSpatialIndex(2).ShouldScanActiveGrids(
            new FixedBoundVolume(new Vector3d(-1, -1, -1), new Vector3d(5, 1, 1)), world.ActiveGrids.Count));
        (ushort GridIndex, int CellKey)[] expected =
        {
            (first.GridIndex, 0), (first.GridIndex, 1), (first.GridIndex, 2), (first.GridIndex, 3),
            (second.GridIndex, 0), (second.GridIndex, 1), (second.GridIndex, 2), (second.GridIndex, 3)
        };
        SwiftList<ScanCell> cells = new SwiftList<ScanCell>();
        GridScanScratch scratch = new GridScanScratch();

        for (int repeat = 0; repeat < 2; repeat++)
        {
            GridTracer.GetCoveredScanCellsInto(world, Vector3d.Zero, new Vector3d(4, 0, 0), cells, scratch);
            Assert.Equal(expected, cells.Select(cell => (cell.GridIndex, cell.CellKey)));
            GridTracer.GetCoveredScanCellsInto(world, Vector3d.Zero, Vector3d.Zero, cells, scratch);
            Assert.Equal(new[] { (first.GridIndex, 0) }, cells.Select(cell => (cell.GridIndex, cell.CellKey)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanRadiusInto_ShouldObserveRegistrationIntoLaterInitiallyEmptyCandidate(bool useScratch)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(world, Vector3d.Zero, new Vector3d(1, 0, 0), scanCellSize: 1);
        TestOccupant first = new TestOccupant(Vector3d.Zero);
        TestOccupant later = new TestOccupant(new Vector3d(1, 0, 0));
        Assert.True(grid.TryAddVoxelOccupant(new VoxelIndex(0, 0, 0), first));
        Assert.True(grid.TryGetScanCell(new VoxelIndex(1, 0, 0), out ScanCell laterCell));
        Assert.False(laterCell.IsOccupied);
        SwiftList<TestOccupant> results = new SwiftList<TestOccupant>();
        SwiftList<IVoxelOccupant> filtered = new SwiftList<IVoxelOccupant>();
        Func<IVoxelOccupant, bool> filter = occupant =>
        {
            filtered.Add(occupant);
            if (ReferenceEquals(occupant, first))
            {
                Assert.False(laterCell.IsOccupied);
                Assert.True(grid.TryAddVoxelOccupant(new VoxelIndex(1, 0, 0), later));
            }
            return true;
        };

        if (useScratch)
            GridScanManager.ScanRadiusInto(world, Vector3d.Zero, (Fixed64)2, results, new GridScanScratch(), filter);
        else
            GridScanManager.ScanRadiusInto(world, Vector3d.Zero, (Fixed64)2, results, filter);

        Assert.True(laterCell.IsOccupied);
        Assert.Equal(new[] { first, later }, results);
        Assert.Equal(new IVoxelOccupant[] { first, later }, filtered);
    }

    private static VoxelGrid AddCoverageGrid(
        GridWorld world,
        Vector3d origin,
        GridStorageKind storageKind,
        GridTopologyKind topologyKind,
        HexOrientation orientation,
        VoxelIndex[] occupiedIndices)
    {
        GridTopologyMetrics metrics = topologyKind == GridTopologyKind.HexPrism
            ? GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation)
            : default;
        Vector3d extent = topologyKind == GridTopologyKind.HexPrism
            ? HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(2, 2, 2), metrics)
            : new Vector3d(2, 2, 2);
        GridConfiguration configuration = new GridConfiguration(origin, origin + extent, 2, topologyKind, metrics, storageKind);
        ushort gridIndex;
        if (storageKind == GridStorageKind.Sparse)
            Assert.True(world.TryAddGrid(configuration, occupiedIndices.Reverse().ToArray(), out gridIndex));
        else
            Assert.True(world.TryAddGrid(configuration, out gridIndex));
        return world.ActiveGrids[gridIndex];
    }

    private static TestOccupant[] RegisterInReverseOrder(VoxelGrid grid, VoxelIndex[] indices)
    {
        TestOccupant[] occupants = new TestOccupant[indices.Length];
        for (int i = indices.Length - 1; i >= 0; i--)
        {
            Assert.True(grid.TryGetVoxel(indices[i], out Voxel voxel));
            occupants[i] = new TestOccupant(voxel.WorldPosition);
            Assert.True(grid.TryAddVoxelOccupant(voxel, occupants[i]));
        }
        return occupants;
    }
}
