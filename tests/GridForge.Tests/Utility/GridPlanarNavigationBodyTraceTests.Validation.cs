//=======================================================================
// GridPlanarNavigationBodyTraceTests.Validation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using Xunit;

namespace GridForge.Grids.Tests;

public sealed partial class GridPlanarNavigationBodyTraceTests
{
    [Fact]
    public void ArgumentGuards_RejectNullStorageNegativeBudgetsAndInvalidLayerConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridPlanarLayer(default, 0));
        VoxelGrid grid = Add(Vector3d.Zero, Vector3d.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridPlanarLayer(Layer(grid).Generation, -1));
        for (int bad = 0; bad < 6; bad++)
        {
            int argument = bad;
            Assert.ThrowsAny<ArgumentException>(() => GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(grid) }, Fixed64.Zero,
                Address(grid, default), Address(grid, default), Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.Half,
                argument == 0 ? null : _results, argument == 1 ? null : _scratch,
                argument == 2 ? -1 : 1, argument == 3 ? -1 : 1, argument == 4 ? -1 : 1, argument == 5 ? -1 : 2));
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void InvalidIdentityLayerOrContact_IsRejectedBeforeAddressWork(int bad)
    {
        VoxelGrid grid = Add(Vector3d.Zero, Vector3d.Zero);
        GridPlanarLayer layer = Layer(grid);
        WorldVoxelIndex address = Address(grid, default);
        if (bad == 0) layer = new GridPlanarLayer(new GridCoveredAddressGeneration(layer.Generation.ConfigurationKey, ushort.MaxValue, grid.SpawnToken, grid.LastChangeSequence), 0);
        if (bad == 1) layer = new GridPlanarLayer(new GridCoveredAddressGeneration(layer.Generation.ConfigurationKey, grid.GridIndex, grid.SpawnToken, grid.LastChangeSequence + 1), 0);
        if (bad == 2) layer = new GridPlanarLayer(new GridCoveredAddressGeneration(default, grid.GridIndex, grid.SpawnToken, grid.LastChangeSequence), 0);
        if (bad == 3) layer = new GridPlanarLayer(layer.Generation, 1);
        if (bad == 4) address = new WorldVoxelIndex(_world.SpawnToken + 1, grid.GridIndex, grid.SpawnToken, default);
        if (bad == 5) address = Address(grid, new VoxelIndex(-1, 0, 0));
        if (bad == 6) address = Address(grid, new VoxelIndex(1, 0, 0));
        if (bad == 7) address = Address(grid, new VoxelIndex(0, 0, -1));
        if (bad == 8) address = Address(grid, new VoxelIndex(0, 0, 1));
        Vector2d start = bad == 9 ? new Vector2d(2, 0) : Vector2d.Zero;
        Vector2d end = bad == 10 ? new Vector2d(2, 0) : Vector2d.Zero;
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { layer }, Fixed64.Zero,
            address, address, start, end, Fixed64.Zero, Fixed64.Half, _results, _scratch, 1, 1, 1, 2);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(0, report.AddressCandidateCount);
        Assert.Empty(_results);
        AssertScratchEmpty();
    }

    [Fact]
    public void UnselectedPhysicalGrid_CannotFillHoleAndUnrelatedEligibleGridCostsOnlyGeneration()
    {
        VoxelGrid sparse = Add(Vector3d.Zero, new Vector3d(1, 0, 1), new[] { default(VoxelIndex), new VoxelIndex(1, 0, 1) });
        Add(Vector3d.Zero, new Vector3d(1, 4, 1), metrics: GridTopologyMetrics.Rectangular(Fixed64.One, new Fixed64(4), Fixed64.One));
        VoxelGrid distant = Add(new Vector3d(100, 0, 100), new Vector3d(101, 0, 101));
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(sparse), Layer(distant) }, Fixed64.Zero,
            Address(sparse, default), Address(sparse, new VoxelIndex(1, 0, 1)), Vector2d.Zero, Vector2d.One,
            Fixed64.Zero, Fixed64.FromFraction(1, 8), _results, _scratch, 2, 4, 4, 6);
        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
        Assert.Equal(2, report.GridCandidateCount);
        Assert.Equal(4, report.AddressCandidateCount);
        Assert.All(_results, cell => Assert.Equal(sparse.GridIndex, cell.Cell.GridIndex));
        report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(distant), Layer(sparse) }, Fixed64.Zero,
            Address(sparse, default), Address(sparse, default), Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.Half, _results, _scratch, 2, 4, 4, 6);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Equal(2, report.GridCandidateCount);
        Assert.Equal(0, report.AddressCandidateCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CheckedBounds_RejectEachUnrepresentableExtent(int edge)
    {
        VoxelGrid grid = Add(Vector3d.Zero, Vector3d.Zero);
        Vector2d center = edge switch
        {
            0 => new Vector2d(Fixed64.MinValue, Fixed64.Zero),
            1 => new Vector2d(Fixed64.Zero, Fixed64.MinValue),
            2 => new Vector2d(Fixed64.Zero, Fixed64.MaxValue),
            _ => Vector2d.Zero
        };
        Fixed64 axis = edge == 3 ? Fixed64.MaxValue : Fixed64.Zero;
        Fixed64 radius = edge >= 3 ? Fixed64.MaxValue : Fixed64.One;
        GridNavigationBodyTraceReport report = Trace(grid, default, default, center, center, axis, radius);
        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, report.Status);
        Assert.Empty(_results);
        AssertScratchEmpty();
    }

    [Fact]
    public void InvalidWorldAndBodyDimensions_ClearPriorResults()
    {
        VoxelGrid grid = Add(Vector3d.Zero, Vector3d.Zero);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, Trace(grid, default, default, Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.Half).Status);
        GridPlanarLayer[] layers = { Layer(grid) };
        WorldVoxelIndex address = Address(grid, default);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
            Trace(grid, default, default, Vector2d.Zero, Vector2d.Zero, -Fixed64.One, Fixed64.Half).Status);
        foreach (GridWorld world in new GridWorld[] { null, _world })
        {
            if (world != null) world.Dispose();
            GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(world, layers, Fixed64.Zero,
                address, address, Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.Half, _results, _scratch, 1, 1, 1, 2);
            Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
            Assert.Empty(_results);
            AssertScratchEmpty();
        }
    }
}
