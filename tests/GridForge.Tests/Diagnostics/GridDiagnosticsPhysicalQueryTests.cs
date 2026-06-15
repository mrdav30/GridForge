//=======================================================================
// GridDiagnosticsPhysicalQueryTests.cs
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
using System;
using System.Linq;
using Xunit;

namespace GridForge.Diagnostics.Tests;

[Collection("GridForgeCollection")]
public class GridDiagnosticsPhysicalQueryTests
{
    [Fact]
    public void GetCellsInto_ShouldReturnDensePhysicalCellsInActiveGridOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        VoxelGrid firstGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1));
        VoxelGrid secondGrid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(8, 0, 0),
            new Vector3d(8, 0, 0));
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            GridDiagnosticQuery.AllPhysical(),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Equal(firstGrid.Size + secondGrid.Size, result.CellCount);
        Assert.Equal(result.CellCount, results.Count);
        AssertCell(results[0], firstGrid, new VoxelIndex(0, 0, 0));
        AssertCell(results[1], firstGrid, new VoxelIndex(0, 0, 1));
        AssertCell(results[2], firstGrid, new VoxelIndex(1, 0, 0));
        AssertCell(results[3], firstGrid, new VoxelIndex(1, 0, 1));
        AssertCell(results[4], secondGrid, new VoxelIndex(0, 0, 0));
        Assert.True((results[0].State & GridDiagnosticCellState.Empty) != 0);
        Assert.True((results[0].State & GridDiagnosticCellState.Boundary) != 0);
    }

    [Fact]
    public void GetCellsInto_ShouldReturnSparsePhysicalCellsInConfiguredVoxelOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        GridConfiguration configuration = CreateRectangularConfiguration(
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            GridStorageKind.Sparse);
        VoxelIndex[] configured =
        {
            new(2, 0, 2),
            new(0, 0, 1),
            new(2, 0, 2),
            new(1, 0, 0)
        };

        Assert.True(world.TryAddGrid(configuration, configured, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(storageKind: GridStorageKind.Sparse),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Equal(grid.ConfiguredVoxelCount, result.CellCount);
        Assert.Equal(3, results.Count);
        AssertCell(results[0], grid, new VoxelIndex(0, 0, 1));
        AssertCell(results[1], grid, new VoxelIndex(1, 0, 0));
        AssertCell(results[2], grid, new VoxelIndex(2, 0, 2));
    }

    [Fact]
    public void GetCellsInto_ShouldReturnSparseHexPhysicalCellsWithTopologyMetadata()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(new Fixed64(2), Fixed64.One, HexOrientation.FlatTop);
        GridConfiguration configuration = CreateHexConfiguration(
            new Vector3d(0, 0, 0),
            metrics,
            new VoxelIndex(2, 0, 2),
            GridStorageKind.Sparse);
        VoxelIndex[] configured =
        {
            new(2, 0, 2),
            new(0, 0, 1),
            new(1, 0, 0)
        };
        Assert.True(world.TryAddGrid(configuration, configured, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                topologyKind: GridTopologyKind.HexPrism,
                storageKind: GridStorageKind.Sparse),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Equal(grid.ConfiguredVoxelCount, result.CellCount);
        Assert.Equal(
            new[]
            {
                new VoxelIndex(0, 0, 1),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(2, 0, 2)
            },
            results.Select(cell => cell.Index).ToArray());
        Assert.All(results, cell =>
        {
            Assert.Equal(GridTopologyKind.HexPrism, cell.TopologyKind);
            Assert.Equal(GridStorageKind.Sparse, cell.StorageKind);
            Assert.Equal(metrics, cell.TopologyMetrics);
            AssertCell(cell, grid, cell.Index);
        });
    }

    [Fact]
    public void GetCellsInto_ShouldFilterByGridTopologyAndStorageKind()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        VoxelGrid denseRectangular = GridWorldTestFactory.AddGrid(
            world,
            CreateRectangularConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1), GridStorageKind.Dense));
        GridConfiguration sparseConfiguration = CreateRectangularConfiguration(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 0, 2),
            GridStorageKind.Sparse);
        Assert.True(world.TryAddGrid(
            sparseConfiguration,
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(2, 0, 2) },
            out ushort sparseGridIndex));
        VoxelGrid sparseRectangular = world.ActiveGrids[sparseGridIndex];
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(new Fixed64(2), Fixed64.One, HexOrientation.PointyTop);
        VoxelGrid denseHex = GridWorldTestFactory.AddGrid(
            world,
            CreateHexConfiguration(new Vector3d(32, 0, 0), hexMetrics, new VoxelIndex(1, 0, 1), GridStorageKind.Dense));
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult gridResult = GridDiagnostics.GetCellsInto(
            world,
            GridDiagnosticQuery.ForGrid(sparseRectangular.GridIndex),
            results);
        Assert.Equal(GridDiagnosticQueryStatus.Completed, gridResult.Status);
        Assert.Equal(sparseRectangular.ConfiguredVoxelCount, results.Count);
        Assert.All(results, cell => Assert.Equal(sparseRectangular.GridIndex, cell.GridIndex));

        GridDiagnosticQueryResult topologyResult = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(topologyKind: GridTopologyKind.HexPrism),
            results);
        Assert.Equal(GridDiagnosticQueryStatus.Completed, topologyResult.Status);
        Assert.Equal(denseHex.Size, results.Count);
        Assert.All(results, cell => Assert.Equal(GridTopologyKind.HexPrism, cell.TopologyKind));
        Assert.All(results, cell => Assert.Equal(hexMetrics, cell.TopologyMetrics));

        GridDiagnosticQueryResult storageResult = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(storageKind: GridStorageKind.Dense),
            results);
        Assert.Equal(GridDiagnosticQueryStatus.Completed, storageResult.Status);
        Assert.Equal(denseRectangular.Size + denseHex.Size, results.Count);
        Assert.DoesNotContain(results, cell => cell.GridIndex == sparseRectangular.GridIndex);
    }

    [Fact]
    public void GetCellsInto_ShouldExposePhysicalStateAndApplyStateFilters()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 2, 2));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 1, 1), out Voxel occupiedVoxel));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 1, 2), out Voxel blockedVoxel));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 1, 1), out Voxel partitionedVoxel));
        Assert.True(grid.TryAddVoxelOccupant(occupiedVoxel, new TestOccupant(occupiedVoxel.WorldPosition)));
        Assert.True(grid.TryAddObstacle(
            blockedVoxel,
            new BoundsKey(blockedVoxel.WorldPosition, blockedVoxel.WorldPosition)));
        Assert.True(partitionedVoxel.TryAddPartition(new TestPartition()));
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(requiredStates: GridDiagnosticCellState.Occupied),
            results);
        Assert.Single(results);
        AssertCell(results[0], grid, occupiedVoxel.Index);
        Assert.True((results[0].State & GridDiagnosticCellState.Occupied) != 0);
        Assert.True((results[0].State & GridDiagnosticCellState.Empty) == 0);

        GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(requiredStates: GridDiagnosticCellState.Blocked),
            results);
        Assert.Single(results);
        AssertCell(results[0], grid, blockedVoxel.Index);
        Assert.True((results[0].State & GridDiagnosticCellState.Blocked) != 0);
        Assert.True((results[0].State & GridDiagnosticCellState.Empty) == 0);

        GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(requiredStates: GridDiagnosticCellState.Partitioned),
            results);
        Assert.Single(results);
        AssertCell(results[0], grid, partitionedVoxel.Index);
        Assert.True((results[0].State & GridDiagnosticCellState.Partitioned) != 0);
        Assert.True((results[0].State & GridDiagnosticCellState.Empty) != 0);

        GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(requiredStates: GridDiagnosticCellState.Empty),
            results);
        Assert.Equal(grid.Size - 2, results.Count);
        Assert.DoesNotContain(results, cell => cell.Index == occupiedVoxel.Index);
        Assert.DoesNotContain(results, cell => cell.Index == blockedVoxel.Index);
        Assert.Contains(results, cell => cell.Index == partitionedVoxel.Index);

        GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(requiredStates: GridDiagnosticCellState.Boundary),
            results);
        Assert.Equal(grid.Size - 1, results.Count);
        Assert.DoesNotContain(results, cell => cell.Index == occupiedVoxel.Index);
        Assert.Contains(results, cell => cell.Index == blockedVoxel.Index);
        Assert.Contains(results, cell => cell.Index == partitionedVoxel.Index);

        GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(excludedStates: GridDiagnosticCellState.Boundary),
            results);
        Assert.Single(results);
        AssertCell(results[0], grid, occupiedVoxel.Index);
    }

    [Fact]
    public void GetCellsInto_ShouldFilterRectangularBoundsByDiagnosticAabbOverlap()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(4, 0, 4));
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            GridDiagnosticQuery.ForBounds(new Vector3d(1, 0, 1), new Vector3d(2, 0, 2)),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Equal(4, results.Count);
        Assert.Equal(
            new[]
            {
                new VoxelIndex(1, 0, 1),
                new VoxelIndex(1, 0, 2),
                new VoxelIndex(2, 0, 1),
                new VoxelIndex(2, 0, 2)
            },
            results.Select(cell => cell.Index).ToArray());
        Assert.All(results, cell => Assert.Equal(grid.GridIndex, cell.GridIndex));
    }

    [Fact]
    public void GetCellsInto_ShouldFilterHexBoundsByDiagnosticAabbOverlap()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(new Fixed64(2), Fixed64.One, HexOrientation.PointyTop);
        VoxelGrid grid = GridWorldTestFactory.AddGrid(
            world,
            CreateHexConfiguration(new Vector3d(0, 0, 0), metrics, new VoxelIndex(2, 0, 2), GridStorageKind.Dense));
        VoxelIndex targetIndex = new(1, 0, 1);
        Vector3d targetCenter = grid.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(targetIndex, metrics);
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            GridDiagnosticQuery.ForBounds(targetCenter, targetCenter),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Single(results);
        AssertCell(results[0], grid, targetIndex);
        Assert.Equal(GridTopologyKind.HexPrism, results[0].TopologyKind);
    }

    [Fact]
    public void GetCellsInto_ShouldStopAtMaxCellsAndReportInvalidGrid()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridWorldTestFactory.AddGrid(world, new Vector3d(0, 0, 0), new Vector3d(2, 0, 2));
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult maxResult = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(maxCells: 2),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.MaxCellsExceeded, maxResult.Status);
        Assert.Equal(2, maxResult.CellCount);
        Assert.Equal(2, results.Count);
        Assert.True(maxResult.SkippedCellCount > 0);

        GridDiagnosticQueryResult invalidGridResult = GridDiagnostics.GetCellsInto(
            world,
            GridDiagnosticQuery.ForGrid(99),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.InvalidGrid, invalidGridResult.Status);
        Assert.Equal(0, invalidGridResult.CellCount);
        Assert.Empty(results);
    }

    [Fact]
    public void VisitCells_ShouldTraversePhysicalCellsAndStopWhenVisitorReturnsFalse()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridWorldTestFactory.AddGrid(world, new Vector3d(0, 0, 0), new Vector3d(2, 0, 2));
        StopAfterVisitor visitor = new(3);

        GridDiagnosticQueryResult result = GridDiagnostics.VisitCells(
            world,
            GridDiagnosticQuery.AllPhysical(),
            ref visitor);

        Assert.Equal(GridDiagnosticQueryStatus.Truncated, result.Status);
        Assert.Equal(3, result.CellCount);
        Assert.Equal(3, visitor.Count);
        Assert.Equal(new VoxelIndex(0, 0, 2), visitor.LastIndex);
        Assert.True(result.SkippedCellCount > 0);
    }

    [Fact]
    public void VisitCells_ShouldAvoidWarmPathAllocationsForPhysicalQueries()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridWorldTestFactory.AddGrid(world, new Vector3d(0, 0, 0), new Vector3d(15, 0, 15));
        GridConfiguration sparseConfiguration = CreateRectangularConfiguration(
            new Vector3d(32, 0, 0),
            new Vector3d(47, 0, 15),
            GridStorageKind.Sparse);
        Assert.True(world.TryAddGrid(
            sparseConfiguration,
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(4, 0, 4),
                new VoxelIndex(8, 0, 8),
                new VoxelIndex(12, 0, 12)
            },
            out _));
        GridDiagnosticScratch scratch = new();
        CountingVisitor visitor = new();

        GridDiagnostics.VisitCells(world, GridDiagnosticQuery.AllPhysical(), ref visitor, scratch);
        scratch.Clear();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            visitor = new CountingVisitor();
            GridDiagnostics.VisitCells(world, GridDiagnosticQuery.AllPhysical(), ref visitor, scratch);
            scratch.Clear();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 256, $"Expected warmed diagnostic visitor queries below 256 bytes, but allocated {allocated} bytes.");
    }

    private static GridConfiguration CreateRectangularConfiguration(
        Vector3d min,
        Vector3d max,
        GridStorageKind storageKind) =>
        new(
            min,
            max,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: storageKind);

    private static GridConfiguration CreateHexConfiguration(
        Vector3d min,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        GridStorageKind storageKind)
    {
        Vector3d max = min + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            min,
            max,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics,
            storageKind: storageKind);
    }

    private static void AssertCell(
        GridDiagnosticCell cell,
        VoxelGrid grid,
        VoxelIndex expectedIndex)
    {
        Assert.Equal(GridDiagnosticCellKind.Physical, cell.Kind);
        Assert.Equal(grid.World!.SpawnToken, cell.WorldSpawnToken);
        Assert.Equal(grid.GridIndex, cell.GridIndex);
        Assert.Equal(grid.SpawnToken, cell.GridSpawnToken);
        Assert.Equal(expectedIndex, cell.Index);
        Assert.Equal(grid.Configuration.TopologyKind, cell.TopologyKind);
        Assert.Equal(grid.StorageKind, cell.StorageKind);
        Assert.Equal(grid.Configuration.TopologyMetrics, cell.TopologyMetrics);
        Assert.Equal(new WorldVoxelIndex(grid.World!.SpawnToken, grid.GridIndex, grid.SpawnToken, expectedIndex), cell.WorldIndex);
    }

    private struct StopAfterVisitor : IGridDiagnosticCellVisitor
    {
        private readonly int _stopAfter;

        public int Count;

        public VoxelIndex LastIndex;

        public StopAfterVisitor(int stopAfter)
        {
            _stopAfter = stopAfter;
            Count = 0;
            LastIndex = default;
        }

        public bool Visit(in GridDiagnosticCell cell)
        {
            Count++;
            LastIndex = cell.Index;
            return Count < _stopAfter;
        }
    }

    private struct CountingVisitor : IGridDiagnosticCellVisitor
    {
        public int Count;

        public bool Visit(in GridDiagnosticCell cell)
        {
            Count++;
            return true;
        }
    }
}
