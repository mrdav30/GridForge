using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class VoxelGridTests : IDisposable
{
    private GridWorld _world;

    public static TheoryData<RectangularDirection, VoxelIndex> BoundaryDirectionCases => CreateBoundaryDirectionCases();

    public VoxelGridTests()
    {
        _world = GridWorldTestFactory.CreateWorld();
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Initialize_ShouldSetCorrectDimensions()
    {
        var start = new Vector3d(-10, 0, -10);
        var end = new Vector3d(10, 0, 10);
        var config = new GridConfiguration(start, end);
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        Fixed64 cellSize = GridWorld.DefaultRectangularCellSize;
        int width = ((end.X - start.X) / cellSize).FloorToInt() + 1;
        int height = ((end.Y - start.Y) / cellSize).FloorToInt() + 1;
        int length = ((end.Z - start.Z) / cellSize).FloorToInt() + 1;

        Assert.Equal(width, grid.Width);
        Assert.Equal(height, grid.Height);
        Assert.Equal(length, grid.Length);
        Assert.True(grid.IsActive);
    }

    [Fact]
    public void Initialize_ShouldUseRectangularTopologyMetricsForDimensionsAndVoxelCenters()
    {
        Fixed64 cellSize = (Fixed64)0.5;
        GridConfiguration config = new(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 1, 1),
            topologyMetrics: GridTopologyMetrics.Rectangular(cellSize));

        Assert.True(_world.TryAddGrid(config, out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.Equal(3, grid.Width);
        Assert.Equal(3, grid.Height);
        Assert.Equal(3, grid.Length);
        Assert.True(grid.TryGetVoxel(new VoxelIndex(2, 2, 2), out Voxel maxVoxel));
        Assert.Equal(new Vector3d(1, 1, 1), maxVoxel.WorldPosition);
        Assert.True(grid.TryGetVoxelIndex(Vector3d.FromDouble(0.75, 0.75, 0.75), out VoxelIndex resolvedIndex));
        Assert.Equal(new VoxelIndex(1, 1, 1), resolvedIndex);
    }

    [Fact]
    public void DenseStorageBoundary_ShouldExposeStorageNeutralPhysicalVoxelEnumeration()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.Equal(GridStorageKind.Dense, grid.StorageKind);
        Assert.Equal(grid.Size, grid.ConfiguredVoxelCount);

        VoxelIndex[] voxelIndices = grid
            .EnumerateVoxels()
            .Select(voxel => voxel.Index)
            .ToArray();

        Assert.Equal(
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(0, 0, 1),
                new VoxelIndex(0, 1, 0),
                new VoxelIndex(0, 1, 1),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(1, 0, 1),
                new VoxelIndex(1, 1, 0),
                new VoxelIndex(1, 1, 1)
            },
            voxelIndices);
    }

    [Fact]
    public void StorageKindAndDenseBacking_ShouldReflectActiveStorageStrategy()
    {
        VoxelGrid inactiveGrid = new VoxelGrid();
        Assert.Equal(GridStorageKind.Dense, inactiveGrid.StorageKind);
        Assert.Equal(0, inactiveGrid.ConfiguredVoxelCount);
        Assert.Null(inactiveGrid.Voxels);

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort denseIndex));
        VoxelGrid denseGrid = _world.ActiveGrids[denseIndex];
        Assert.Equal(GridStorageKind.Dense, denseGrid.StorageKind);
        Assert.NotNull(denseGrid.Voxels);

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(10, 0, 0),
                new Vector3d(11, 0, 1),
                storageKind: GridStorageKind.Sparse),
            new[] { new VoxelIndex(0, 0, 0) },
            out ushort sparseIndex));
        VoxelGrid sparseGrid = _world.ActiveGrids[sparseIndex];
        Assert.Equal(GridStorageKind.Sparse, sparseGrid.StorageKind);
        Assert.Null(sparseGrid.Voxels);
    }

    [Fact]
    public void DenseStorageBoundary_ShouldKeepDenseCollectionsOutOfPublicSurface()
    {
        const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

        Assert.Null(typeof(VoxelGrid).GetProperty("Voxels", PublicInstance));
        Assert.Null(typeof(VoxelGrid).GetProperty("ScanCells", PublicInstance));
    }

    [Fact]
    public void GetVoxel_ShouldReturnCorrectVoxel()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        bool found = grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        Assert.True(found);
        Assert.NotNull(voxel);
    }

    [Fact]
    public void IsVoxelAllocated_ShouldReturnCorrectState()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.True(grid.IsVoxelAllocated(10, 0, 10));
        Assert.True(grid.IsVoxelAllocated(20, 0, 20));
    }

    [Fact]
    public void GetScanCell_ShouldReturnValidCell()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        bool found = grid.TryGetScanCell(new Vector3d(0, 0, 0), out ScanCell scanCell);

        Assert.True(found);
        Assert.NotNull(scanCell);
    }

    [Fact]
    public void GetActiveScanCells_ShouldReturnExpectedCount()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        int count = 0;
        foreach (var cell in grid.GetActiveScanCells())
            count++;

        Assert.Equal(grid.ActiveScanCells?.Count ?? 0, count);
    }

    [Fact]
    public void Grid_ShouldCorrectlyManageNeighbors()
    {
        var config1 = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        var config2 = new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(30, 0, 30));

        _world.TryAddGrid(config1, out ushort index1);
        _world.TryAddGrid(config2, out ushort index2);

        VoxelGrid grid1 = _world.ActiveGrids[index1];
        VoxelGrid grid2 = _world.ActiveGrids[index2];

        Assert.True(grid1.IsConjoined);
        Assert.True(grid2.IsConjoined);

        // since tests run in parallel...there maybe other grids in play
        Assert.True(grid1.NeighborCount >= 1);
        Assert.True(grid1.NeighborCount >= 1);

        // get the direction before removal
        int neighborIndex = (int)VoxelGrid.GetRectangularNeighborDirection(grid1, grid2);

        _world.TryRemoveGrid(grid2.GridIndex);

        if (grid1.Neighbors != null)
        {
            if (grid1.Neighbors.ContainsKey(neighborIndex))
                Assert.DoesNotContain(grid2.GridIndex, grid1.Neighbors[neighborIndex]);
        }
        else
            Assert.False(grid1.IsConjoined);

        Assert.False(grid2.IsActive);
    }

    [Fact]
    public void Reset_ShouldReturnEarlyWhenGridIsInactive()
    {
        VoxelGrid detachedGrid = new();

        InvokeGridReset(detachedGrid);

        Assert.False(detachedGrid.IsActive);
        Assert.Equal(0, detachedGrid.NeighborCount);
        Assert.False(detachedGrid.IsConjoined);
    }

    [Fact]
    public void TryGetVoxelIndex_ShouldHandleNegativePositionsAndFractionalRectangularMetrics()
    {
        ResetWorld();

        try
        {
            var config = new GridConfiguration(
                Vector3d.FromDouble(-1.5, 0, -1.5),
                Vector3d.FromDouble(1.5, 0, 1.5),
                topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)0.5));

            Assert.True(_world.TryAddGrid(config, out ushort index));

            VoxelGrid grid = _world.ActiveGrids[index];

            Assert.True(grid.TryGetVoxelIndex(Vector3d.FromDouble(-1.25, 0, -0.75), out VoxelIndex negativeIndex));
            Assert.Equal(new VoxelIndex(0, 0, 1), negativeIndex);

            Assert.Equal(
                Vector3d.FromDouble(-1.5, 0, -1.0),
                grid.FloorToGrid(Vector3d.FromDouble(-1.26, 0, -0.74)));
            Assert.Equal(
                Vector3d.FromDouble(-1.0, 0, -0.5),
                grid.CeilToGrid(Vector3d.FromDouble(-1.26, 0, -0.74)));
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetScanCellKey_ShouldTransitionAcrossScanCellBoundaries()
    {
        var config = new GridConfiguration(
            new Vector3d(0, 0, 0),
            new Vector3d(15, 0, 15),
            scanCellSize: 4);

        Assert.True(_world.TryAddGrid(config, out ushort index));

        VoxelGrid grid = _world.ActiveGrids[index];
        Vector3d beforeBoundary = Vector3d.FromDouble(3.9, 0, 3.9);
        Vector3d atBoundary = new(4, 0, 4);

        int firstCellKey = grid.GetScanCellKey(beforeBoundary);
        int secondCellKey = grid.GetScanCellKey(atBoundary);

        Assert.NotEqual(firstCellKey, secondCellKey);
        Assert.Equal((0, 0, 0), grid.SnapToScanCell(beforeBoundary));
        Assert.Equal((1, 0, 1), grid.SnapToScanCell(atBoundary));
    }

    [Fact]
    public void TryGetVoxelIndex_ShouldIncludeExactBoundsMaxAndRejectOutsidePositions()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.True(grid.TryGetVoxelIndex(new Vector3d(2, 0, 2), out VoxelIndex maxIndex));
        Assert.Equal(new VoxelIndex(2, 0, 2), maxIndex);
        Assert.True(grid.IsVoxelAllocated(maxIndex.x, maxIndex.y, maxIndex.z));

        Assert.False(grid.TryGetVoxelIndex(Vector3d.FromDouble(2.01, 0, 2), out _));
        Assert.False(grid.TryGetVoxelIndex(Vector3d.FromDouble(2, 0.01, 2), out _));
        Assert.False(grid.TryGetVoxelIndex(Vector3d.FromDouble(2, 0, 2.01), out _));
        Assert.False(grid.IsVoxelAllocated(3, 0, 2));
        Assert.False(grid.IsVoxelAllocated(2, 1, 2));
        Assert.False(grid.IsVoxelAllocated(2, 0, 3));
        Assert.False(grid.TryGetVoxel(new VoxelIndex(3, 0, 2), out _));
    }

    [Fact]
    public void TryGetVoxel_WithVector2d_ShouldUseDefaultAndExplicitLayers()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];
        Vector2d position = new(1, 1);

        Assert.True(grid.TryGetVoxelIndex(position, out VoxelIndex defaultIndex));
        Assert.Equal(new VoxelIndex(1, 0, 1), defaultIndex);
        Assert.True(grid.TryGetVoxel(position, out Voxel defaultVoxel));
        Assert.Equal(defaultIndex, defaultVoxel.Index);

        Assert.True(grid.TryGetVoxelIndex(position, (Fixed64)2, out VoxelIndex layeredIndex));
        Assert.Equal(new VoxelIndex(1, 2, 1), layeredIndex);
        Assert.True(grid.TryGetVoxel(position, (Fixed64)2, out Voxel layeredVoxel));
        Assert.Equal(layeredIndex, layeredVoxel.Index);
    }

    [Fact]
    public void TryGetVoxel_WithVector2d_ShouldIncludeExactBoundsMaxAndRejectOutsidePositions()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.True(grid.TryGetVoxelIndex(new Vector2d(2, 2), (Fixed64)2, out VoxelIndex maxIndex));
        Assert.Equal(new VoxelIndex(2, 2, 2), maxIndex);
        Assert.True(grid.TryGetVoxel(new Vector2d(2, 2), (Fixed64)2, out Voxel maxVoxel));
        Assert.Equal(maxIndex, maxVoxel.Index);

        Assert.False(grid.TryGetVoxelIndex(Vector2d.FromDouble(2.01, 1), (Fixed64)2, out _));
        Assert.False(grid.TryGetVoxelIndex(new Vector2d(1, 1), (Fixed64)3, out _));
        Assert.False(grid.TryGetVoxel(Vector2d.FromDouble(1, 2.01), (Fixed64)2, out _));
    }

    [Fact]
    public void ScanCellQueries_ShouldReturnGracefulDefaultsForInvalidKeysAndIndices()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 2),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.Equal(-1, grid.GetScanCellKey(new Vector3d(8, 0, 8)));
        Assert.Equal(-1, grid.GetScanCellKey(new VoxelIndex(99, 0, 99)));
        Assert.Equal(-1, grid.GetScanCellKey(new VoxelIndex(-3, 0, 0)));
        Assert.False(grid.TryGetScanCell(-1, out _));
        Assert.False(grid.TryGetScanCell(999, out _));
        Assert.False(grid.TryGetScanCell(new VoxelIndex(99, 0, 99), out _));
    }

    [Fact]
    public void InactiveGridQueries_ShouldReturnGracefulDefaultsAcrossForwardingSurfaces()
    {
        VoxelGrid inactiveGrid = new();
        SwiftList<Voxel> voxels = new SwiftList<Voxel>();
        SwiftHashSet<Voxel> voxelRedundancy = new SwiftHashSet<Voxel>();
        SwiftList<ScanCell> scanCells = new SwiftList<ScanCell>();
        SwiftHashSet<ScanCell> scanCellRedundancy = new SwiftHashSet<ScanCell>();

        Assert.Equal(0, inactiveGrid.NeighborSlotCount);
        Assert.Null(inactiveGrid.TopologyKind);
        Assert.Null(inactiveGrid.ScanCells);
        Assert.False(inactiveGrid.IsInBounds(Vector3d.Zero));
        Assert.False(inactiveGrid.IsValidVoxelIndex(0, 0, 0));
        Assert.False(inactiveGrid.TryGetVoxelIndex(Vector3d.Zero, out _));
        Assert.False(inactiveGrid.IsVoxelAllocated(0, 0, 0));
        Assert.False(inactiveGrid.TryGetVoxel(0, 0, 0, out _));
        Assert.Empty(inactiveGrid.EnumerateVoxels());
        Assert.False(inactiveGrid.TryGetClosestVoxel(Vector3d.Zero, out _));
        Assert.False(inactiveGrid.TryGetScanCell(0, out _));
        Assert.Empty(inactiveGrid.GetActiveScanCells());

        inactiveGrid.AddVoxelsInIndexRange(new VoxelIndex(0, 0, 0), new VoxelIndex(0, 0, 0), voxels, voxelRedundancy);
        inactiveGrid.AddScanCellsInRange(0, 0, 0, 0, 0, 0, scanCells, scanCellRedundancy);
        InvokeGridReset(inactiveGrid);

        Assert.Empty(voxels);
        Assert.Empty(scanCells);
    }

    [Fact]
    public void TopologySpecificNeighborQueries_ShouldReturnDefaultsForInactiveMismatchedAndInvalidSlots()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort rectangularIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(10, 0, 10),
                new Vector3d(12, 0, 12),
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: GridTopologyMetrics.Hex(new Fixed64(1), Fixed64.One)),
            out ushort hexIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexIndex];

        Assert.Equal(RectangularDirection.None, VoxelGrid.GetRectangularNeighborDirection(new VoxelGrid(), new VoxelGrid()));
        Assert.Equal(RectangularDirection.None, VoxelGrid.GetRectangularNeighborDirection(new VoxelGrid(), rectangularGrid));
        Assert.Equal(RectangularDirection.None, VoxelGrid.GetRectangularNeighborDirection(rectangularGrid, new VoxelGrid()));
        Assert.Equal(RectangularDirection.None, VoxelGrid.GetRectangularNeighborDirection(rectangularGrid, hexGrid));
        Assert.Equal(HexDirection.None, VoxelGrid.GetHexNeighborDirection(new VoxelGrid(), new VoxelGrid()));
        Assert.Equal(HexDirection.None, VoxelGrid.GetHexNeighborDirection(new VoxelGrid(), hexGrid));
        Assert.Equal(HexDirection.None, VoxelGrid.GetHexNeighborDirection(hexGrid, new VoxelGrid()));
        Assert.Equal(HexDirection.None, VoxelGrid.GetHexNeighborDirection(hexGrid, rectangularGrid));
        Assert.False(InvokeTryGetNeighborSlot(new VoxelGrid(), rectangularGrid, out int detachedSlot));
        Assert.Equal(-1, detachedSlot);
        Assert.False(InvokeTryGetNeighborSlot(rectangularGrid, new VoxelGrid(), out int missingNeighborSlot));
        Assert.Equal(-1, missingNeighborSlot);
        Assert.False(new VoxelGrid().TryGetNeighborSlot(RectangularDirection.East, out _));
        Assert.False(new VoxelGrid().TryGetNeighborSlot(HexDirection.QPositive, out _));
        Assert.True(rectangularGrid.TryGetNeighborSlot(RectangularDirection.East, out _));
        Assert.False(rectangularGrid.TryGetNeighborSlot((RectangularDirection)int.MaxValue, out _));
        Assert.False(rectangularGrid.TryGetNeighborSlot(HexDirection.QPositive, out _));
        Assert.False(hexGrid.TryGetNeighborSlot(RectangularDirection.East, out _));
        Assert.True(hexGrid.TryGetNeighborSlot(HexDirection.QPositive, out _));
        Assert.False(hexGrid.TryGetNeighborSlot((HexDirection)int.MaxValue, out _));
        Assert.False(hexGrid.IsFacingBoundary(new VoxelIndex(0, 0, 0), RectangularDirection.East));
        Assert.False(rectangularGrid.IsFacingBoundary(new VoxelIndex(0, 0, 0), HexDirection.QPositive));
    }

    [Fact]
    public void GridNeighborManagement_ShouldRejectInvalidAndDuplicateRelationshipsAndReleaseLastNeighborSet()
    {
        using DiagnosticCaptureScope diagnostics = new();
        VoxelGrid centerGrid = CreateStandaloneGrid(
            10,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        VoxelGrid eastGrid = CreateStandaloneGrid(
            11,
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0));
        VoxelGrid secondEastGrid = CreateStandaloneGrid(
            13,
            new Vector3d(2, 0, 0),
            new Vector3d(2, 0, 0));
        VoxelGrid sameCenterGrid = CreateStandaloneGrid(
            12,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));

        try
        {
            Assert.False(InvokeTryAddGridNeighbor(centerGrid, sameCenterGrid));
            Assert.True(InvokeTryAddGridNeighbor(centerGrid, eastGrid));
            Assert.False(InvokeTryAddGridNeighbor(centerGrid, eastGrid));
            Assert.True(InvokeTryAddGridNeighbor(centerGrid, secondEastGrid));
            Assert.True(centerGrid.IsConjoined);
            Assert.Equal(2, centerGrid.NeighborCount);
            Assert.False(InvokeTryRemoveGridNeighbor(centerGrid, sameCenterGrid));
            Assert.True(InvokeTryRemoveGridNeighbor(centerGrid, eastGrid));
            Assert.True(centerGrid.IsConjoined);
            Assert.NotNull(centerGrid.Neighbors);
            Assert.Equal(1, centerGrid.NeighborCount);
            Assert.False(InvokeTryRemoveGridNeighbor(centerGrid, eastGrid));
            Assert.True(InvokeTryRemoveGridNeighbor(centerGrid, secondEastGrid));
            Assert.False(centerGrid.IsConjoined);
            Assert.Null(centerGrid.Neighbors);
            Assert.Equal(0, centerGrid.NeighborCount);
            Assert.False(InvokeTryRemoveGridNeighbor(centerGrid, eastGrid));
            Assert.Contains(diagnostics.Messages, message => message.Message.Contains("unused neighbor collection"));
        }
        finally
        {
            InvokeGridReset(centerGrid);
            InvokeGridReset(eastGrid);
            InvokeGridReset(secondEastGrid);
            InvokeGridReset(sameCenterGrid);
            centerGrid.World?.Dispose();
            eastGrid.World?.Dispose();
            secondEastGrid.World?.Dispose();
            sameCenterGrid.World?.Dispose();
        }
    }

    [Fact]
    public void DiagnosticsEnabled_ShouldLogVoxelGridGuardPaths()
    {
        using DiagnosticCaptureScope diagnostics = new();
        VoxelGrid inactiveGrid = new();

        Assert.False(inactiveGrid.IsValidVoxelIndex(0, 0, 0));
        Assert.False(inactiveGrid.TryGetVoxelIndex(Vector3d.Zero, out _));
        Assert.False(inactiveGrid.TryGetClosestVoxel(Vector3d.Zero, out _));

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.False(grid.IsValidVoxelIndex(-1, 0, 0));
        Assert.False(grid.TryGetVoxelIndex(new Vector3d(99, 0, 99), out _));
        Assert.Equal(-1, grid.GetScanCellKey(new VoxelIndex(int.MaxValue, 0, 0)));

        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("not currently active"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("not currently allocated"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("not valid for this grid"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("does not fall in the bounds"));
        Assert.Contains(diagnostics.Messages, message => message.Message.Contains("Scan Cell overlay"));
    }

    [Fact]
    public void TopologySpecificNeighborDirections_ShouldRejectMismatchedTopologyPairs()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort rectangularIndex));
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration hexConfiguration = new(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: hexMetrics);
        Assert.True(_world.TryAddGrid(hexConfiguration, out ushort hexIndex));

        VoxelGrid rectangularGrid = _world.ActiveGrids[rectangularIndex];
        VoxelGrid hexGrid = _world.ActiveGrids[hexIndex];

        Assert.Equal(RectangularDirection.None, VoxelGrid.GetRectangularNeighborDirection(rectangularGrid, hexGrid));
        Assert.Equal(HexDirection.None, VoxelGrid.GetHexNeighborDirection(rectangularGrid, hexGrid));
    }

    [Fact]
    public void IncrementVersion_ShouldWrapFromUIntMaxValue()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        SetGridVersion(grid, uint.MaxValue);

        Assert.Equal(1u, InvokeIncrementVersion(grid));
        Assert.Equal(1u, grid.Version);
    }

    [Fact]
    public void IsGridOverlapValid_ShouldRespectExplicitTolerance()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            out ushort firstIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(2, 0, 0), new Vector3d(2, 0, 0)),
            out ushort secondIndex));

        VoxelGrid firstGrid = _world.ActiveGrids[firstIndex];
        VoxelGrid secondGrid = _world.ActiveGrids[secondIndex];

        Assert.False(VoxelGrid.IsGridOverlapValid(firstGrid, secondGrid, tolerance: Fixed64.Zero));
        Assert.True(VoxelGrid.IsGridOverlapValid(firstGrid, secondGrid, tolerance: (Fixed64)2));
    }

    [Fact]
    public void GetActiveScanCells_ShouldReturnEmptyWhenGridHasNoOccupants()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(5, 0, 5)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.Empty(grid.GetActiveScanCells());
    }

    [Fact]
    public void GetActiveScanCells_ShouldReturnOnlyOccupiedCells()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 2),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];
        TestOccupant firstOccupant = new(new Vector3d(0, 0, 0));
        TestOccupant secondOccupant = new(new Vector3d(4, 0, 4));

        Assert.True(grid.TryAddVoxelOccupant(firstOccupant));
        Assert.True(grid.TryAddVoxelOccupant(secondOccupant));
        Assert.True(grid.TryGetScanCell(firstOccupant.Position, out ScanCell firstCell));
        Assert.True(grid.TryGetScanCell(secondOccupant.Position, out ScanCell secondCell));

        ScanCell[] activeCells = grid.GetActiveScanCells().ToArray();

        Assert.Equal(2, activeCells.Length);
        Assert.All(activeCells, scanCell => Assert.True(scanCell.IsOccupied));
        Assert.Contains(firstCell, activeCells);
        Assert.Contains(secondCell, activeCells);
        Assert.DoesNotContain(
            activeCells,
            scanCell => scanCell.CellKey == grid.GetScanCellKey(new Vector3d(2, 0, 2)));
    }

    [Theory]
    [MemberData(nameof(BoundaryDirectionCases))]
    public void IsFacingBoundary_ShouldMatchRectangularOffsets(
        RectangularDirection direction,
        VoxelIndex boundaryIndex)
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.IsFacingBoundary(boundaryIndex, direction));
    }

    [Fact]
    public void IsFacingBoundary_ShouldReturnFalseForCenterVoxel()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelIndex centerIndex = new(1, 1, 1);

        foreach (RectangularDirection direction in RectangularDirectionUtility.All)
            Assert.False(grid.IsFacingBoundary(centerIndex, direction));
    }

    [Fact]
    public void BoundaryQueries_ShouldRejectInvalidDirections()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.False(grid.IsFacingBoundary(new VoxelIndex(0, 0, 0), (RectangularDirection)(-2)));
    }

    [Fact]
    public void Grid_ShouldHandleComplexConnectionsDuringDynamicLoadAndUnload()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10)),
            out ushort centerIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(20, 0, 10)),
            out _));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 10), new Vector3d(10, 0, 20)),
            out _));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(20, 0, 20)),
            out ushort northEastIndex));

        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];

        Assert.Equal(3, centerGrid.NeighborCount);
        Assert.Equal(3, centerGrid.GetAllGridNeighbors().Select(grid => grid.GridIndex).Distinct().Count());

        Assert.True(_world.TryRemoveGrid(northEastIndex));

        Assert.Equal(2, centerGrid.NeighborCount);
        Assert.Equal(2, centerGrid.GetAllGridNeighbors().Select(grid => grid.GridIndex).Distinct().Count());
    }

    [Fact]
    public void ReleasedGridAndScanCell_ShouldNotLeakStateWhenReused()
    {
        GridConfiguration centerConfig = new(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1),
            scanCellSize: 8);
        GridConfiguration eastConfig = new(
            new Vector3d(1, 0, 0),
            new Vector3d(2, 0, 1),
            scanCellSize: 8);
        TestOccupant occupant = new(new Vector3d(0, 0, 0), 4);
        BoundsKey obstacleToken = new(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1));

        Assert.True(_world.TryAddGrid(centerConfig, out ushort centerIndex));
        Assert.True(_world.TryAddGrid(eastConfig, out ushort eastIndex));

        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];

        Assert.True(centerGrid.TryAddVoxelOccupant(occupant));
        Assert.True(centerGrid.TryAddObstacle(new Vector3d(1, 0, 1), obstacleToken));
        Assert.True(centerGrid.IsConjoined);
        Assert.True(centerGrid.IsOccupied);
        Assert.Equal(1, centerGrid.ObstacleCount);

        Assert.True(_world.TryRemoveGrid(eastIndex));
        Assert.True(_world.TryRemoveGrid(centerIndex));
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(_world, occupant));

        Assert.True(_world.TryAddGrid(centerConfig, out ushort reusedIndex));

        VoxelGrid reusedGrid = _world.ActiveGrids[reusedIndex];

        Assert.False(reusedGrid.IsConjoined);
        Assert.Equal(0, reusedGrid.NeighborCount);
        Assert.False(reusedGrid.IsOccupied);
        Assert.Equal(0, reusedGrid.ObstacleCount);
        Assert.Empty(reusedGrid.GetAllGridNeighbors());
        Assert.Empty(reusedGrid.GetActiveScanCells());
        Assert.True(reusedGrid.TryGetScanCell(new Vector3d(0, 0, 0), out ScanCell reusedScanCell));
        Assert.False(reusedScanCell.IsOccupied);
        Assert.Equal(0, reusedScanCell.CellOccupantCount);
        Assert.Empty(reusedGrid.GetOccupants(new Vector3d(0, 0, 0)));
    }

    [Fact]
    public void ReleasedGridQueries_ShouldFailGracefullyWhenGridIsInactive()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(_world.TryRemoveGrid(gridIndex));
        Assert.False(grid.TryGetVoxelIndex(new Vector3d(1, 0, 1), out _));
        Assert.False(grid.TryGetVoxel(0, 0, 0, out _));
        Assert.False(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out _));
    }

    [Fact]
    public void TryGetVoxelIndex_ShouldUseOwningGridTopologyMetricsEvenWhenOtherGridsDiffer()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        GridConfiguration fractionalConfig = new(
            new Vector3d(2, 0, 0),
            new Vector3d(3, 0, 1),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)0.5));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelGrid fractionalGrid = GridWorldTestFactory.AddGrid(_world, fractionalConfig);

        Assert.True(grid.TryGetVoxelIndex(new Vector3d(1, 0, 1), out VoxelIndex resolvedIndex));
        Assert.Equal(new VoxelIndex(1, 0, 1), resolvedIndex);
        Assert.True(fractionalGrid.TryGetVoxelIndex(new Vector3d(3, 0, 1), out VoxelIndex fractionalIndex));
        Assert.Equal(new VoxelIndex(2, 0, 2), fractionalIndex);
    }

    [Fact]
    public void ClearPools_ShouldDropReleasedScanCellMaps()
    {
        GridConfiguration config = new(
            new Vector3d(0, 0, 0),
            new Vector3d(3, 0, 3),
            scanCellSize: 2);

        Assert.True(_world.TryAddGrid(config, out ushort firstIndex));
        VoxelGrid firstGrid = _world.ActiveGrids[firstIndex];
        object firstScanCellMap = firstGrid.ScanCells;

        Assert.True(_world.TryRemoveGrid(firstIndex));

        Type poolsType = typeof(GridWorld).Assembly.GetType("GridForge.Grids.Pools");
        Assert.NotNull(poolsType);

        MethodInfo clearPools = poolsType.GetMethod("ClearPools", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(clearPools);
        clearPools.Invoke(null, null);

        Assert.True(_world.TryAddGrid(config, out ushort secondIndex));
        VoxelGrid secondGrid = _world.ActiveGrids[secondIndex];

        Assert.NotSame(firstScanCellMap, secondGrid.ScanCells);
    }

    [Fact]
    public void GetRectangularNeighborDirection_ShouldFollowCardinalAxisOffsets()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort centerIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(2, 0, 1)),
            out ushort eastIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 1), new Vector3d(1, 0, 2)),
            out ushort northIndex));

        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];
        VoxelGrid eastGrid = _world.ActiveGrids[eastIndex];
        VoxelGrid northGrid = _world.ActiveGrids[northIndex];

        Assert.Equal(RectangularDirection.East, VoxelGrid.GetRectangularNeighborDirection(centerGrid, eastGrid));
        Assert.Equal(RectangularDirection.North, VoxelGrid.GetRectangularNeighborDirection(centerGrid, northGrid));
    }

    [Fact]
    public void ClearPools_ShouldAllowFreshGridAllocationAfterPoolReset()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        Assert.True(_world.TryRemoveGrid(gridIndex));

        Type poolsType = typeof(VoxelGrid).Assembly.GetType("GridForge.Grids.Pools");
        MethodInfo clearPools = poolsType?.GetMethod(
            "ClearPools",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(clearPools);
        clearPools.Invoke(null, null);

        Assert.True(_world.TryAddGrid(config, out ushort reusedIndex));
        VoxelGrid reusedGrid = _world.ActiveGrids[reusedIndex];

        Assert.True(reusedGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));
        Assert.NotNull(voxel);
    }

    [Fact]
    public void GridConfigurationAndBoundsKey_ShouldProvideStableIdentityForMatchingBounds()
    {
        GridConfiguration first = new(
            new Vector3d(0, 0, 0),
            new Vector3d(5, 0, 5),
            scanCellSize: 2);
        GridConfiguration second = new(
            new Vector3d(0, 0, 0),
            new Vector3d(5, 0, 5),
            scanCellSize: 16);
        BoundsKey firstKey = first.ToBoundsKey();
        BoundsKey secondKey = second.ToBoundsKey();
        BoundsKey differentKey = new(new Vector3d(1, 0, 1), new Vector3d(5, 0, 5));
        GridConfiguration reversedBounds = new(new Vector3d(5, 1, 5), new Vector3d(1, -1, 1));

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first.BoundsMin, firstKey.BoundsMin);
        Assert.Equal(first.BoundsMax, firstKey.BoundsMax);
        Assert.Equal(firstKey, secondKey);
        Assert.Equal(firstKey.GetHashCode(), secondKey.GetHashCode());
        Assert.NotEqual(firstKey, differentKey);
        Assert.Equal(new Vector3d(1, -1, 1), reversedBounds.BoundsMin);
        Assert.Equal(new Vector3d(5, 1, 5), reversedBounds.BoundsMax);
    }

    private static TheoryData<RectangularDirection, VoxelIndex> CreateBoundaryDirectionCases()
    {
        TheoryData<RectangularDirection, VoxelIndex> cases = new();

        for (int i = 0; i < RectangularDirectionUtility.Offsets.Length; i++)
        {
            (int x, int y, int z) offset = RectangularDirectionUtility.Offsets[i];
            VoxelIndex boundaryIndex = new(
                offset.x < 0 ? 0 : offset.x > 0 ? 2 : 1,
                offset.y < 0 ? 0 : offset.y > 0 ? 2 : 1,
                offset.z < 0 ? 0 : offset.z > 0 ? 2 : 1);

            cases.Add((RectangularDirection)i, boundaryIndex);
        }

        return cases;
    }

    private static VoxelGrid CreateStandaloneGrid(ushort globalIndex, Vector3d min, Vector3d max)
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = new();
        InvokeGridInitialize(grid, world, globalIndex, new GridConfiguration(min, max));
        return grid;
    }

    private static void InvokeGridInitialize(VoxelGrid grid, GridWorld world, ushort globalIndex, GridConfiguration configuration)
    {
        MethodInfo initializeMethod = typeof(VoxelGrid).GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(GridWorld), typeof(ushort), typeof(GridConfiguration), typeof(IGridTopology), typeof(VoxelIndex[]) },
            null);

        Assert.NotNull(initializeMethod);
        Assert.True(GridWorld.TryNormalizeConfiguration(
            configuration,
            out GridConfiguration normalizedConfiguration,
            out IGridTopology topology,
            out _));
        initializeMethod.Invoke(grid, new object[] { world, globalIndex, normalizedConfiguration, topology, Array.Empty<VoxelIndex>() });
    }

    private static void InvokeGridReset(VoxelGrid grid)
    {
        MethodInfo resetMethod = typeof(VoxelGrid).GetMethod(
            "Reset",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(resetMethod);
        resetMethod.Invoke(grid, null);
    }

    private static bool InvokeTryAddGridNeighbor(VoxelGrid grid, VoxelGrid neighborGrid)
    {
        MethodInfo addNeighborMethod = typeof(VoxelGrid).GetMethod(
            "TryAddGridNeighbor",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(addNeighborMethod);
        return (bool)addNeighborMethod.Invoke(grid, new object[] { neighborGrid });
    }

    private static bool InvokeTryRemoveGridNeighbor(VoxelGrid grid, VoxelGrid neighborGrid)
    {
        MethodInfo removeNeighborMethod = typeof(VoxelGrid).GetMethod(
            "TryRemoveGridNeighbor",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(removeNeighborMethod);
        return (bool)removeNeighborMethod.Invoke(grid, new object[] { neighborGrid });
    }

    private static bool InvokeTryGetNeighborSlot(VoxelGrid grid, VoxelGrid neighborGrid, out int slot)
    {
        MethodInfo method = typeof(VoxelGrid).GetMethod(
            "TryGetNeighborSlot",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(VoxelGrid), typeof(VoxelGrid), typeof(int).MakeByRefType() },
            null);

        Assert.NotNull(method);
        object[] arguments = { grid, neighborGrid, -1 };
        bool result = (bool)method.Invoke(null, arguments);
        slot = (int)arguments[2];
        return result;
    }

    private static uint InvokeIncrementVersion(VoxelGrid grid)
    {
        MethodInfo incrementMethod = typeof(VoxelGrid).GetMethod(
            "IncrementVersion",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(incrementMethod);
        return (uint)incrementMethod.Invoke(grid, null);
    }

    private static void SetGridVersion(VoxelGrid grid, uint version)
    {
        FieldInfo versionField = typeof(VoxelGrid).GetField(
            "<Version>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(versionField);
        versionField.SetValue(grid, version);
    }

    private void ResetWorld(int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        _world.Dispose();
        _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize);
    }
}
