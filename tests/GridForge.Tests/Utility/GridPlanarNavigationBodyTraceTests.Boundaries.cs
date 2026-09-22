//=======================================================================
// GridPlanarNavigationBodyTraceTests.Boundaries.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Linq;
using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

public sealed partial class GridPlanarNavigationBodyTraceTests
{
    [Theory]
    [InlineData(0, 4, 4, 5, GridNavigationBodyTraceStatus.GridCandidateLimitExceeded, 0, 0)]
    [InlineData(1, 3, 4, 5, GridNavigationBodyTraceStatus.AddressLimitExceeded, 1, 3)]
    [InlineData(1, 4, 3, 5, GridNavigationBodyTraceStatus.OutputLimitExceeded, 1, 4)]
    [InlineData(1, 4, 4, 4, GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded, 1, 3)]
    [InlineData(1, 4, 4, 5, GridNavigationBodyTraceStatus.Complete, 1, 4)]
    [InlineData(1, 4, 4, 0, GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded, 0, 0)]
    public void Budgets_ChargeBeforeEachInspectionAndClearAbortedProof(int grids, int addresses, int output, long work,
        GridNavigationBodyTraceStatus status, int expectedGrids, int expectedAddresses)
    {
        VoxelGrid grid = Add(Vector3d.Zero, new Vector3d(1, 0, 1));
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(grid) }, Fixed64.Zero,
            Address(grid, default), Address(grid, new VoxelIndex(1, 0, 1)), Vector2d.Zero, Vector2d.One, Fixed64.Zero, Fixed64.FromFraction(1, 8),
            _results, _scratch, grids, addresses, output, work);
        Assert.Equal(status, report.Status);
        Assert.Equal(expectedGrids, report.GridCandidateCount);
        Assert.Equal(expectedAddresses, report.AddressCandidateCount);
        Assert.Equal(expectedGrids + (long)expectedAddresses, report.CandidateWorkCount);
        Assert.Equal(status == GridNavigationBodyTraceStatus.Complete ? 4 : 0, _results.Count);
        AssertScratchEmpty();
        Assert.Equal(GridNavigationBodyTraceStatus.Complete,
            Trace(grid, default, new VoxelIndex(1, 0, 1), Vector2d.Zero, Vector2d.One, Fixed64.Zero, Fixed64.FromFraction(1, 8)).Status);
    }

    [Theory]
    [InlineData(0, 16, 16, GridNavigationBodyTraceStatus.GridCandidateLimitExceeded)]
    [InlineData(1, 8, 16, GridNavigationBodyTraceStatus.AddressLimitExceeded)]
    [InlineData(1, 16, 8, GridNavigationBodyTraceStatus.OutputLimitExceeded)]
    public void ExistingCapacities_AreHardCeilingsDespiteLargerBudgets(int grids, int addresses, int output, GridNavigationBodyTraceStatus status)
    {
        VoxelGrid grid = Add(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1));
        GridNavigationBodyTraceScratch scratch = new GridNavigationBodyTraceScratch(grids, addresses);
        SwiftList<GridNavigationBodyTraceCell> results = new SwiftList<GridNavigationBodyTraceCell>(output);
        int gridCapacity = scratch.CandidateGrids.Capacity;
        int addressCapacity = scratch.AddressCandidates.Capacity;
        int resultCapacity = results.Capacity;
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(grid) }, Fixed64.Zero,
            Address(grid, new VoxelIndex(1, 0, 1)), Address(grid, new VoxelIndex(1, 0, 1)), Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.One,
            results, scratch, 99, 99, 99, 198);
        Assert.Equal(status, report.Status);
        Assert.Empty(results);
        Assert.Equal(gridCapacity, scratch.CandidateGrids.Capacity);
        Assert.Equal(addressCapacity, scratch.AddressCandidates.Capacity);
        Assert.Equal(resultCapacity, results.Capacity);
        Assert.Empty(scratch.AddressCandidates);
        Assert.Empty(scratch.UnionMembers);
    }

    [Fact]
    public void AlignedSeam_WithDifferentLayerHeightsUsesFootprintsNotPrismHeight()
    {
        VoxelGrid left = Add(Vector3d.Zero, new Vector3d(1, 0, 0), metrics: GridTopologyMetrics.Rectangular(Fixed64.One, new Fixed64(2), Fixed64.One));
        VoxelGrid right = Add(new Vector3d(2, 0, 0), new Vector3d(3, 0, 0), metrics: GridTopologyMetrics.Rectangular(Fixed64.One, new Fixed64(4), Fixed64.One));
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(left), Layer(right) }, Fixed64.Half,
            Address(left, new VoxelIndex(1, 0, 0)), Address(right, default), Vector2d.Right, new Vector2d(2, 0), Fixed64.Zero, Fixed64.Half,
            _results, _scratch, 2, 8, 8, 10);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(2, _results.Count);
        Assert.Equal(Address(left, new VoxelIndex(1, 0, 0)), _results[0].Cell);
        Assert.Equal(Address(right, default), _results[1].Cell);
    }

    [Fact]
    public void MissingAlternatives_ArePublishedAsOrDependencies()
    {
        VoxelIndex[] endpoints = { default, new VoxelIndex(1, 0, 1) };
        VoxelGrid first = Add(Vector3d.Zero, new Vector3d(1, 0, 1), endpoints);
        VoxelGrid second = Add(Vector3d.Zero, new Vector3d(1, 4, 1), endpoints,
            GridTopologyMetrics.Rectangular(Fixed64.One, new Fixed64(4), Fixed64.One));
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(first), Layer(second) }, Fixed64.Zero,
            Address(first, default), Address(first, new VoxelIndex(1, 0, 1)), Vector2d.Zero, Vector2d.One, Fixed64.Zero, Fixed64.FromFraction(1, 8),
            _results, _scratch, 2, 8, 6, 10);
        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
        Assert.Equal(6, _results.Count);
        Assert.Equal(4, _results.Count(cell => cell.Role == GridNavigationBodyTraceCellRole.RequiredCoverage));
        Assert.Equal(2, _results.Count(cell => cell.Role == GridNavigationBodyTraceCellRole.PhysicalAlternativeDependency));
        Assert.All(_results.Where(cell => cell.Role == GridNavigationBodyTraceCellRole.PhysicalAlternativeDependency),
            cell => { Assert.False(cell.IsPhysicallyPresent); Assert.Equal(second.GridIndex, cell.Cell.GridIndex); });
    }

    [Fact]
    public void LayerValidation_IsChargedAndRejectsDuplicatesStaleOrUnselectedEndpoints()
    {
        VoxelGrid grid = Add(Vector3d.Zero, new Vector3d(1, 0, 0));
        GridPlanarLayer layer = Layer(grid);
        GridPlanarLayer stale = new GridPlanarLayer(new GridCoveredAddressGeneration(layer.Generation.ConfigurationKey, grid.GridIndex,
            grid.SpawnToken + 1, grid.LastChangeSequence), 0);
        foreach (GridPlanarLayer[] layers in new[] { new[] { layer, layer }, new[] { stale }, Array.Empty<GridPlanarLayer>() })
        {
            GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, layers, Fixed64.Zero,
                Address(grid, default), Address(grid, new VoxelIndex(1, 0, 0)), Vector2d.Zero, Vector2d.Right, Fixed64.Zero, Fixed64.Half,
                _results, _scratch, 8, 128, 128, 136);
            Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
            Assert.Equal(layers.Length, report.GridCandidateCount);
            Assert.Empty(_results);
            AssertScratchEmpty();
        }
    }

    [Fact]
    public void EndpointTangency_IsClosedButBodyCoverageRemainsStrict()
    {
        VoxelGrid grid = Add(Vector3d.Zero, new Vector3d(2, 0, 0));
        // The circle touches source cell x=0 at x=.5 while its interior belongs to cell x=1.
        GridNavigationBodyTraceReport report = Trace(grid, default, new VoxelIndex(1, 0, 0), Vector2d.Right, Vector2d.Right, Fixed64.Zero, Fixed64.Half);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 0) }, _results.Select(cell => cell.Cell.VoxelIndex));
    }

    [Fact]
    public void BodyBoundsOverflow_ReturnsArithmeticOverflowWithoutSaturation()
    {
        VoxelGrid grid = Add(Vector3d.Zero, Vector3d.Zero);
        GridNavigationBodyTraceReport report = Trace(grid, default, default, new Vector2d(Fixed64.MaxValue, Fixed64.Zero),
            new Vector2d(Fixed64.MaxValue, Fixed64.Zero), Fixed64.Zero, Fixed64.One);
        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, report.Status);
        Assert.Empty(_results);
        AssertScratchEmpty();
    }
}
