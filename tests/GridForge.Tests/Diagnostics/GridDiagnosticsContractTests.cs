using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Tests;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;
using Xunit;

namespace GridForge.Diagnostics.Tests;

[Collection("GridForgeCollection")]
public class GridDiagnosticsContractTests
{
    [Fact]
    public void GridDiagnosticQueryFactories_ShouldUsePhysicalOnlyDefaultsAndFilters()
    {
        GridDiagnosticQuery allPhysical = GridDiagnosticQuery.AllPhysical();

        Assert.Equal(65536, GridDiagnosticQuery.DefaultMaxCells);
        Assert.Equal(GridDiagnosticAddressMode.PhysicalOnly, allPhysical.AddressMode);
        Assert.Equal(GridDiagnosticQuery.DefaultMaxCells, allPhysical.MaxCells);
        Assert.Null(allPhysical.GridIndex);
        Assert.Null(allPhysical.TopologyKind);
        Assert.Null(allPhysical.StorageKind);
        Assert.Null(allPhysical.BoundsMin);
        Assert.Null(allPhysical.BoundsMax);

        GridDiagnosticQuery gridQuery = GridDiagnosticQuery.ForGrid(4);
        Assert.Equal((ushort)4, gridQuery.GridIndex);
        Assert.Equal(GridDiagnosticAddressMode.PhysicalOnly, gridQuery.AddressMode);

        Vector3d boundsMin = new(1, 0, 1);
        Vector3d boundsMax = new(3, 0, 3);
        GridDiagnosticQuery boundsQuery = GridDiagnosticQuery.ForBounds(boundsMin, boundsMax);
        Assert.Equal(boundsMin, boundsQuery.BoundsMin);
        Assert.Equal(boundsMax, boundsQuery.BoundsMax);
        Assert.Equal(GridDiagnosticAddressMode.PhysicalOnly, boundsQuery.AddressMode);
    }

    [Fact]
    public void GetCellsInto_ShouldClearResultsAndReturnInactiveWorld()
    {
        using GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();
        SwiftList<GridDiagnosticCell> results = new();
        results.Add(default);

        GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(
            inactiveWorld,
            GridDiagnosticQuery.AllPhysical(),
            results);

        Assert.Equal(GridDiagnosticQueryStatus.InactiveWorld, result.Status);
        Assert.Equal(0, result.CellCount);
        Assert.Equal(0, result.SkippedCellCount);
        Assert.Empty(results);
    }

    [Fact]
    public void GetCellsInto_ShouldThrowWhenResultStorageIsNull()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();

        Assert.Throws<ArgumentNullException>(() => GridDiagnostics.GetCellsInto(
            world,
            GridDiagnosticQuery.AllPhysical(),
            null));
    }

    [Fact]
    public void VisitCells_ShouldReturnInactiveWorldWithoutVisiting()
    {
        using GridWorld inactiveWorld = GridWorldTestFactory.CreateWorld();
        inactiveWorld.Dispose();
        CountingVisitor visitor = new();

        GridDiagnosticQueryResult result = GridDiagnostics.VisitCells(
            inactiveWorld,
            GridDiagnosticQuery.AllPhysical(),
            ref visitor);

        Assert.Equal(GridDiagnosticQueryStatus.InactiveWorld, result.Status);
        Assert.Equal(0, result.CellCount);
        Assert.Equal(0, visitor.Count);
    }

    [Fact]
    public void TryResolvePhysicalCell_ShouldResolvePhysicalDescriptorAndRejectMissingDescriptor()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));

        GridDiagnosticCell physicalCell = new(
            GridDiagnosticCellKind.Physical,
            world.SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            voxel.Index,
            voxel.WorldPosition,
            grid.Configuration.TopologyKind,
            grid.StorageKind,
            grid.Configuration.TopologyMetrics,
            GridDiagnosticCellState.Empty,
            voxel.WorldIndex);

        Assert.True(GridDiagnostics.TryResolvePhysicalCell(
            world,
            physicalCell,
            out VoxelGrid resolvedGrid,
            out Voxel resolvedVoxel));
        Assert.Same(grid, resolvedGrid);
        Assert.Same(voxel, resolvedVoxel);

        GridDiagnosticCell missingCell = new(
            GridDiagnosticCellKind.MissingSparseAddress,
            world.SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            voxel.Index,
            voxel.WorldPosition,
            grid.Configuration.TopologyKind,
            GridStorageKind.Sparse,
            grid.Configuration.TopologyMetrics,
            GridDiagnosticCellState.MissingSparseAddress,
            voxel.WorldIndex);

        Assert.False(GridDiagnostics.TryResolvePhysicalCell(
            world,
            missingCell,
            out _,
            out _));

        GridDiagnosticCell staleCell = new(
            GridDiagnosticCellKind.Physical,
            world.SpawnToken + 1,
            grid.GridIndex,
            grid.SpawnToken,
            voxel.Index,
            voxel.WorldPosition,
            grid.Configuration.TopologyKind,
            grid.StorageKind,
            grid.Configuration.TopologyMetrics,
            GridDiagnosticCellState.Empty,
            voxel.WorldIndex);

        Assert.False(GridDiagnostics.TryResolvePhysicalCell(
            world,
            staleCell,
            out _,
            out _));
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
