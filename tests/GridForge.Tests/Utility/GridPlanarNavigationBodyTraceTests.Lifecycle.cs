//=======================================================================
// GridPlanarNavigationBodyTraceTests.Lifecycle.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

public sealed partial class GridPlanarNavigationBodyTraceTests
{
    [Fact]
    public async Task ConcurrentSparseEdits_ReturnOnlyCoherentGenerationEvidence()
    {
        VoxelIndex hole = new VoxelIndex(0, 0, 1);
        VoxelGrid grid = Add(Vector3d.Zero, new Vector3d(1, 0, 1), new[]
        { default(VoxelIndex), new VoxelIndex(1, 0, 0), new VoxelIndex(1, 0, 1) });
        ulong initial = grid.LastChangeSequence;
        using ManualResetEventSlim start = new ManualResetEventSlim();
        Task mutation = Task.Run(() =>
        {
            start.Wait(TestContext.Current.CancellationToken);
            for (int i = 0; i < 128; i++)
            {
                Assert.True(grid.TryAddVoxel(hole, out _));
                Assert.True(grid.TryRemoveVoxel(hole));
            }
        }, TestContext.Current.CancellationToken);
        start.Set();
        for (int i = 0; i < 128; i++)
        {
            GridPlanarLayer selected = Layer(grid);
            GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { selected }, Fixed64.Zero,
                Address(grid, default), Address(grid, new VoxelIndex(1, 0, 1)), Vector2d.Zero, Vector2d.One, Fixed64.Zero, Fixed64.FromFraction(1, 8),
                _results, _scratch, 1, 4, 4, 5);
            if (report.Status == GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry)
                Assert.Empty(_results); // The captured generation changed before the proof acquired its lock.
            else
            {
                bool present = ((selected.Generation.GridLastChangeSequence - initial) & 1UL) != 0;
                Assert.Equal(present ? GridNavigationBodyTraceStatus.Complete : GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
                Assert.Equal(4, _results.Count);
                Assert.Equal(present, Assert.Single(_results, cell => cell.Cell.VoxelIndex == hole).IsPhysicallyPresent);
                Assert.All(_results, cell => Assert.Equal(selected.Generation.GridLastChangeSequence, cell.GridLastChangeSequence));
            }
            AssertScratchEmpty();
        }
        await mutation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage,
            Trace(grid, default, new VoxelIndex(1, 0, 1), Vector2d.Zero, Vector2d.One, Fixed64.Zero, Fixed64.FromFraction(1, 8)).Status);
    }

    [Fact]
    public void RemovedAndReusedSlot_CannotAcceptOldGenerationOrEndpoint()
    {
        VoxelGrid grid = Add(Vector3d.Zero, Vector3d.Zero);
        GridPlanarLayer old = Layer(grid);
        WorldVoxelIndex oldAddress = Address(grid, default);
        Assert.True(_world.TryRemoveGrid(grid.GridIndex));
        VoxelGrid replacement = Add(Vector3d.Zero, Vector3d.Zero);
        Assert.NotEqual(old.Generation.GridSpawnToken, replacement.SpawnToken);
        foreach (GridPlanarLayer selected in new[] { old, Layer(replacement) })
        {
            GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { selected }, Fixed64.Zero,
                oldAddress, oldAddress, Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.Half, _results, _scratch, 1, 1, 1, 2);
            Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
            Assert.Empty(_results);
        }
        Assert.Equal(GridNavigationBodyTraceStatus.Complete,
            Trace(replacement, default, default, Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.Half).Status);
    }

    [Fact]
    public void SameFootprintDistinctEndpoints_RemainPinnedWhilePresentAlternativesSatisfySides()
    {
        VoxelGrid first = Add(Vector3d.Zero, new Vector3d(1, 0, 1), new[] { default(VoxelIndex) });
        VoxelGrid second = Add(Vector3d.Zero, new Vector3d(1, 4, 1),
            metrics: GridTopologyMetrics.Rectangular(Fixed64.One, new Fixed64(4), Fixed64.One));
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(first), Layer(second) }, Fixed64.Zero,
            Address(first, default), Address(second, default), new Vector2d(Fixed64.Half, Fixed64.Half), new Vector2d(Fixed64.Half, Fixed64.Half),
            Fixed64.Zero, Fixed64.FromFraction(1, 4), _results, _scratch, 2, 8, 5, 10);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(5, _results.Count);
        Assert.Contains(_results, cell => cell.Cell == Address(first, default));
        Assert.Equal(4, _results.Count(cell => cell.Cell.GridIndex == second.GridIndex));
        Assert.All(_results, cell => Assert.True(cell.IsPhysicallyPresent));
    }

    [Fact]
    public void UnionScratchCapacity_IsIndependentOfPreviouslyGrownAddressScratch()
    {
        VoxelGrid grid = Add(Vector3d.Zero, new Vector3d(1, 0, 0));
        GridNavigationBodyTraceScratch scratch = new GridNavigationBodyTraceScratch(1, 0);
        scratch.AddressCandidates.EnsureCapacity(8);
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(grid) }, Fixed64.Zero,
            Address(grid, default), Address(grid, new VoxelIndex(1, 0, 0)), Vector2d.Zero, Vector2d.Right, Fixed64.Zero, Fixed64.Half,
            _results, scratch, 1, 2, 2, 3);
        Assert.Equal(GridNavigationBodyTraceStatus.AddressLimitExceeded, report.Status);
        Assert.Empty(_results);
        Assert.Equal(0, scratch.UnionMembers.Capacity);
        Assert.Empty(scratch.AddressCandidates);
    }

    [Fact]
    public void UnionScratchExhaustion_AfterEightMembersClearsAllEvidence()
    {
        VoxelGrid grid = Add(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1));
        GridNavigationBodyTraceScratch scratch = new GridNavigationBodyTraceScratch(1, 8);
        scratch.AddressCandidates.EnsureCapacity(16);
        WorldVoxelIndex center = Address(grid, new VoxelIndex(1, 0, 1));
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(grid) }, Fixed64.Zero,
            center, center, Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.One, _results, scratch, 1, 9, 9, 10);
        Assert.Equal(GridNavigationBodyTraceStatus.AddressLimitExceeded, report.Status);
        Assert.Equal(9, report.AddressCandidateCount);
        Assert.Empty(_results);
        Assert.Empty(scratch.UnionMembers);
        Assert.Equal(8, scratch.UnionMembers.Capacity);
    }
}
