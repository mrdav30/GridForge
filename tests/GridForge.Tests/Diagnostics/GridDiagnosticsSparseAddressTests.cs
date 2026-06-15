//=======================================================================
// GridDiagnosticsSparseAddressTests.cs
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
using System.Linq;
using Xunit;

namespace GridForge.Diagnostics.Tests;

[Collection("GridForgeCollection")]
public class GridDiagnosticsSparseAddressTests
{
    [Fact]
    public void PhysicalOnly_ShouldRemainDefaultForSparseQueries()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = AddSparseRectangularGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(2, 0, 2) });
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            GridDiagnosticQuery.ForBounds(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Equal(grid.ConfiguredVoxelCount, result.CellCount);
        Assert.Equal(2, results.Count);
        Assert.All(results, cell => Assert.Equal(GridDiagnosticCellKind.Physical, cell.Kind));
    }

    [Fact]
    public void PhysicalAndMissing_ShouldReturnRectangularConfiguredAndMissingCellsInsideBounds()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = AddSparseRectangularGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(1, 0, 1),
                new VoxelIndex(2, 0, 2)
            });
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                addressMode: GridDiagnosticAddressMode.PhysicalAndMissing,
                boundsMin: new Vector3d(0, 0, 0),
                boundsMax: new Vector3d(1, 0, 1)),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Equal(4, result.CellCount);
        AssertPhysicalCell(results[0], grid, new VoxelIndex(0, 0, 0));
        AssertMissingCell(results[1], grid, new VoxelIndex(0, 0, 1));
        AssertMissingCell(results[2], grid, new VoxelIndex(1, 0, 0));
        AssertPhysicalCell(results[3], grid, new VoxelIndex(1, 0, 1));
    }

    [Fact]
    public void MissingOnly_ShouldExcludeConfiguredRectangularCells()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = AddSparseRectangularGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(1, 0, 1),
                new VoxelIndex(2, 0, 2)
            });
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                addressMode: GridDiagnosticAddressMode.MissingOnly,
                boundsMin: new Vector3d(0, 0, 0),
                boundsMax: new Vector3d(2, 0, 2)),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Equal(6, result.CellCount);
        Assert.Equal(
            new[]
            {
                new VoxelIndex(0, 0, 1),
                new VoxelIndex(0, 0, 2),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(1, 0, 2),
                new VoxelIndex(2, 0, 0),
                new VoxelIndex(2, 0, 1)
            },
            results.Select(cell => cell.Index).ToArray());
        Assert.All(results, cell => AssertMissingCell(cell, grid, cell.Index));
    }

    [Fact]
    public void MissingAddressModes_ShouldEmitNoMissingCellsForDenseGrids()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult missingOnlyResult = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                addressMode: GridDiagnosticAddressMode.MissingOnly,
                boundsMin: grid.BoundsMin,
                boundsMax: grid.BoundsMax),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, missingOnlyResult.Status);
        Assert.Equal(0, missingOnlyResult.CellCount);
        Assert.Empty(results);

        GridDiagnosticQueryResult physicalAndMissingResult = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                addressMode: GridDiagnosticAddressMode.PhysicalAndMissing,
                boundsMin: grid.BoundsMin,
                boundsMax: grid.BoundsMax),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, physicalAndMissingResult.Status);
        Assert.Equal(grid.Size, physicalAndMissingResult.CellCount);
        Assert.All(results, cell => Assert.Equal(GridDiagnosticCellKind.Physical, cell.Kind));
    }

    [Theory]
    [InlineData(GridDiagnosticAddressMode.PhysicalAndMissing)]
    [InlineData(GridDiagnosticAddressMode.MissingOnly)]
    public void MissingAddressModes_ShouldRequireBoundsOrFullAddressSpaceOptIn(GridDiagnosticAddressMode addressMode)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        AddSparseRectangularGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            new[] { new VoxelIndex(0, 0, 0) });
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(addressMode: addressMode),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.MissingAddressSpaceRequiresBounds, result.Status);
        Assert.Equal(0, result.CellCount);
        Assert.Equal(0, result.SkippedCellCount);
        Assert.Empty(results);
    }

    [Fact]
    public void FullAddressSpaceOptIn_ShouldRespectMaxCells()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = AddSparseRectangularGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            new[] { new VoxelIndex(2, 0, 2) });
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                addressMode: GridDiagnosticAddressMode.MissingOnly,
                maxCells: 2,
                allowFullAddressSpaceScan: true),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.MaxCellsExceeded, result.Status);
        Assert.Equal(2, result.CellCount);
        Assert.True(result.SkippedCellCount > 0);
        AssertMissingCell(results[0], grid, new VoxelIndex(0, 0, 0));
        AssertMissingCell(results[1], grid, new VoxelIndex(0, 0, 1));
    }

    [Fact]
    public void PhysicalAndMissing_ShouldApplyMaxCellsToCombinedSparseStream()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = AddSparseRectangularGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1),
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(1, 0, 1)
            });
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                addressMode: GridDiagnosticAddressMode.PhysicalAndMissing,
                boundsMin: grid.BoundsMin,
                boundsMax: grid.BoundsMax,
                maxCells: 3),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.MaxCellsExceeded, result.Status);
        Assert.Equal(3, result.CellCount);
        Assert.True(result.SkippedCellCount > 0);
        AssertPhysicalCell(results[0], grid, new VoxelIndex(0, 0, 0));
        AssertMissingCell(results[1], grid, new VoxelIndex(0, 0, 1));
        AssertMissingCell(results[2], grid, new VoxelIndex(1, 0, 0));
    }

    [Fact]
    public void MissingSparseAddressDescriptors_ShouldNotResolveToPhysicalVoxels()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = AddSparseRectangularGrid(
            world,
            new Vector3d(0, 0, 0),
            new Vector3d(2, 0, 2),
            new[] { new VoxelIndex(0, 0, 0) });
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                addressMode: GridDiagnosticAddressMode.MissingOnly,
                boundsMin: new Vector3d(1, 0, 1),
                boundsMax: new Vector3d(1, 0, 1)),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        GridDiagnosticCell missingCell = Assert.Single(results);
        AssertMissingCell(missingCell, grid, new VoxelIndex(1, 0, 1));
        Assert.False(GridDiagnostics.TryResolvePhysicalCell(world, missingCell, out _, out _));
        Assert.False(world.TryGetGridAndVoxel(missingCell.WorldIndex, out VoxelGrid resolvedGrid, out Voxel resolvedVoxel));
        Assert.Same(grid, resolvedGrid);
        Assert.Null(resolvedVoxel);
    }

    [Fact]
    public void MissingOnly_ShouldReturnSparseHexAddressCellsWithAxialIndices()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(new Fixed64(2), Fixed64.One, HexOrientation.PointyTop);
        VoxelGrid grid = AddSparseHexGrid(
            world,
            new Vector3d(0, 0, 0),
            metrics,
            new VoxelIndex(1, 0, 1),
            new[] { new VoxelIndex(0, 0, 0) });
        SwiftList<GridDiagnosticCell> results = new();

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            world,
            new GridDiagnosticQuery(
                addressMode: GridDiagnosticAddressMode.MissingOnly,
                boundsMin: grid.BoundsMin,
                boundsMax: grid.BoundsMax),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.Completed, result.Status);
        Assert.Equal(3, result.CellCount);
        Assert.Equal(
            new[]
            {
                new VoxelIndex(0, 0, 1),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(1, 0, 1)
            },
            results.Select(cell => cell.Index).ToArray());
        Assert.All(results, cell =>
        {
            AssertMissingCell(cell, grid, cell.Index);
            Assert.Equal(GridTopologyKind.HexPrism, cell.TopologyKind);
            Assert.Equal(metrics, cell.TopologyMetrics);
            Assert.Equal(grid.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(cell.Index, metrics), cell.WorldPosition);
        });
    }

    private static VoxelGrid AddSparseRectangularGrid(
        GridWorld world,
        Vector3d min,
        Vector3d max,
        VoxelIndex[] configured)
    {
        GridConfiguration configuration = new(
            min,
            max,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);

        Assert.True(world.TryAddGrid(configuration, configured, out ushort gridIndex));
        return world.ActiveGrids[gridIndex];
    }

    private static VoxelGrid AddSparseHexGrid(
        GridWorld world,
        Vector3d min,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        VoxelIndex[] configured)
    {
        GridConfiguration configuration = new(
            min,
            min + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);

        Assert.True(world.TryAddGrid(configuration, configured, out ushort gridIndex));
        return world.ActiveGrids[gridIndex];
    }

    private static void AssertPhysicalCell(
        GridDiagnosticCell cell,
        VoxelGrid grid,
        VoxelIndex expectedIndex)
    {
        Assert.Equal(GridDiagnosticCellKind.Physical, cell.Kind);
        Assert.Equal(grid.World!.SpawnToken, cell.WorldSpawnToken);
        Assert.Equal(grid.GridIndex, cell.GridIndex);
        Assert.Equal(grid.SpawnToken, cell.GridSpawnToken);
        Assert.Equal(expectedIndex, cell.Index);
        Assert.Equal(grid.GetWorldPosition(expectedIndex), cell.WorldPosition);
        Assert.Equal(grid.Configuration.TopologyKind, cell.TopologyKind);
        Assert.Equal(grid.StorageKind, cell.StorageKind);
        Assert.Equal(grid.Configuration.TopologyMetrics, cell.TopologyMetrics);
        Assert.Equal(new WorldVoxelIndex(grid.World!.SpawnToken, grid.GridIndex, grid.SpawnToken, expectedIndex), cell.WorldIndex);
        Assert.True((cell.State & GridDiagnosticCellState.MissingSparseAddress) == 0);
    }

    private static void AssertMissingCell(
        GridDiagnosticCell cell,
        VoxelGrid grid,
        VoxelIndex expectedIndex)
    {
        Assert.Equal(GridDiagnosticCellKind.MissingSparseAddress, cell.Kind);
        Assert.Equal(grid.World!.SpawnToken, cell.WorldSpawnToken);
        Assert.Equal(grid.GridIndex, cell.GridIndex);
        Assert.Equal(grid.SpawnToken, cell.GridSpawnToken);
        Assert.Equal(expectedIndex, cell.Index);
        Assert.Equal(grid.GetWorldPosition(expectedIndex), cell.WorldPosition);
        Assert.Equal(grid.Configuration.TopologyKind, cell.TopologyKind);
        Assert.Equal(GridStorageKind.Sparse, cell.StorageKind);
        Assert.Equal(grid.Configuration.TopologyMetrics, cell.TopologyMetrics);
        Assert.Equal(new WorldVoxelIndex(grid.World!.SpawnToken, grid.GridIndex, grid.SpawnToken, expectedIndex), cell.WorldIndex);
        Assert.True((cell.State & GridDiagnosticCellState.MissingSparseAddress) != 0);
        Assert.True((cell.State & GridDiagnosticCellState.Empty) == 0);
        Assert.True((cell.State & GridDiagnosticCellState.Occupied) == 0);
        Assert.True((cell.State & GridDiagnosticCellState.Blocked) == 0);
        Assert.True((cell.State & GridDiagnosticCellState.Partitioned) == 0);
    }
}
