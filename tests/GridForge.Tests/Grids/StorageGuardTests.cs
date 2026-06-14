using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class StorageGuardTests
{
    [Fact]
    public void DenseStorage_ShouldNoOpWhenUninitialized()
    {
        DenseVoxelGridStorage storage = new();
        SwiftList<Voxel> voxels = new SwiftList<Voxel>();
        SwiftHashSet<Voxel> voxelRedundancy = new SwiftHashSet<Voxel>();
        SwiftList<ScanCell> scanCells = new SwiftList<ScanCell>();
        SwiftHashSet<ScanCell> scanCellRedundancy = new SwiftHashSet<ScanCell>();

        Assert.Equal(GridStorageKind.Dense, storage.Kind);
        Assert.Equal(0, storage.ConfiguredVoxelCount);
        Assert.False(storage.TryGetScanCell(0, out ScanCell scanCell));
        Assert.Null(scanCell);
        Assert.Empty(storage.EnumerateVoxels().ToArray());

        storage.AddVoxelsInIndexRange(new VoxelIndex(0, 0, 0), new VoxelIndex(1, 1, 1), voxels, voxelRedundancy);
        storage.AddScanCellsInRange(new VoxelGrid(), 0, 0, 0, 1, 1, 1, scanCells, scanCellRedundancy);
        storage.Reset(new VoxelGrid());

        Assert.Empty(voxels);
        Assert.Empty(scanCells);
    }

    [Fact]
    public void DenseStorage_ShouldSkipDuplicateRangeResults()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 1)),
            out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        DenseVoxelGridStorage storage = GetDenseStorage(grid);
        SwiftList<Voxel> voxels = new SwiftList<Voxel>();
        SwiftHashSet<Voxel> voxelRedundancy = new SwiftHashSet<Voxel>();
        SwiftList<ScanCell> scanCells = new SwiftList<ScanCell>();
        SwiftHashSet<ScanCell> scanCellRedundancy = new SwiftHashSet<ScanCell>();

        storage.AddVoxelsInIndexRange(new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 1), voxels, voxelRedundancy);
        int firstCount = voxels.Count;
        storage.AddVoxelsInIndexRange(new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 1), voxels, voxelRedundancy);
        storage.AddScanCellsInRange(grid, 0, 0, 0, 0, 0, 0, scanCells, scanCellRedundancy);
        int firstScanCellCount = scanCells.Count;
        storage.AddScanCellsInRange(grid, -1, 0, 0, 0, 0, 0, scanCells, scanCellRedundancy);

        Assert.Equal(4, firstCount);
        Assert.Equal(firstCount, voxels.Count);
        Assert.Equal(1, firstScanCellCount);
        Assert.Equal(firstScanCellCount, scanCells.Count);
    }

    [Fact]
    public void SparseStorage_ShouldNoOpWhenUninitialized()
    {
        SparseVoxelGridStorage storage = new();
        SwiftList<Voxel> voxels = new SwiftList<Voxel>();
        SwiftHashSet<Voxel> voxelRedundancy = new SwiftHashSet<Voxel>();
        SwiftList<ScanCell> scanCells = new SwiftList<ScanCell>();
        SwiftHashSet<ScanCell> scanCellRedundancy = new SwiftHashSet<ScanCell>();
        VoxelGrid grid = new VoxelGrid();

        Assert.Equal(GridStorageKind.Sparse, storage.Kind);
        Assert.Equal(0, storage.ConfiguredVoxelCount);
        Assert.False(storage.TryGetVoxel(0, 0, 0, out Voxel voxel));
        Assert.Null(voxel);
        Assert.False(storage.TryGetClosestVoxel(grid, new VoxelIndex(0, 0, 0), Vector3d.Zero, out Voxel closest, out Fixed64 distanceSquared));
        Assert.Null(closest);
        Assert.Equal(Fixed64.MaxValue, distanceSquared);
        Assert.False(storage.TryGetScanCell(0, out ScanCell scanCell));
        Assert.Null(scanCell);
        Assert.Empty(storage.EnumerateVoxels().ToArray());
        Assert.False(storage.TryRemoveVoxel(grid, new VoxelIndex(0, 0, 0), out Voxel removed));
        Assert.Null(removed);

        storage.AddVoxelsInIndexRange(new VoxelIndex(0, 0, 0), new VoxelIndex(1, 1, 1), voxels, voxelRedundancy);
        storage.AddScanCellsInRange(grid, 0, 0, 0, 1, 1, 1, scanCells, scanCellRedundancy);
        storage.Reset(grid);

        Assert.Empty(voxels);
        Assert.Empty(scanCells);
    }

    [Fact]
    public void SparseStorage_ShouldHandleDirectMissesAgainstInitializedMaps()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(3, 0, 3),
                scanCellSize: 2,
                storageKind: GridStorageKind.Sparse),
            out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        SparseVoxelGridStorage storage = new SparseVoxelGridStorage();
        SwiftList<ScanCell> scanCells = new SwiftList<ScanCell>();
        SwiftHashSet<ScanCell> scanCellRedundancy = new SwiftHashSet<ScanCell>();
        storage.Initialize(grid, Array.Empty<VoxelIndex>());

        Assert.False(storage.TryAddVoxel(grid, new VoxelIndex(99, 0, 0), out Voxel invalidGridVoxel));
        Assert.Null(invalidGridVoxel);

        Assert.True(storage.TryAddVoxel(grid, new VoxelIndex(0, 0, 0), out Voxel added));
        Assert.NotNull(added);
        Assert.False(storage.TryAddVoxel(grid, new VoxelIndex(0, 0, 0), out Voxel duplicate));
        Assert.Null(duplicate);
        Assert.False(storage.TryRemoveVoxel(grid, new VoxelIndex(-1, 0, 0), out Voxel invalidRemoved));
        Assert.Null(invalidRemoved);
        Assert.False(storage.TryRemoveVoxel(grid, new VoxelIndex(-2, 0, 0), out Voxel invalidCellRemoved));
        Assert.Null(invalidCellRemoved);
        Assert.False(storage.TryRemoveVoxel(grid, new VoxelIndex(2, 0, 0), out Voxel missingBlock));
        Assert.Null(missingBlock);
        Assert.False(storage.TryRemoveVoxel(grid, new VoxelIndex(1, 0, 0), out Voxel missing));
        Assert.Null(missing);

        storage.AddScanCellsInRange(grid, -1, 0, 0, -1, 0, 0, scanCells, scanCellRedundancy);
        Assert.Empty(scanCells);

        SwiftList<Voxel> voxels = new SwiftList<Voxel>();
        SwiftHashSet<Voxel> voxelRedundancy = new SwiftHashSet<Voxel>();
        storage.AddVoxelsInIndexRange(new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 0), voxels, voxelRedundancy);
        Assert.Single(voxels);
        Assert.Same(added, voxels[0]);

        storage.AddVoxelsInIndexRange(new VoxelIndex(-4, 0, 0), new VoxelIndex(-1, 0, 0), voxels, voxelRedundancy);
        storage.AddVoxelsInIndexRange(new VoxelIndex(4, 0, 0), new VoxelIndex(4, 0, 0), voxels, voxelRedundancy);
        Assert.Single(voxels);

        Assert.True(storage.TryRemoveVoxel(grid, new VoxelIndex(0, 0, 0), out Voxel removed));
        Assert.Same(added, removed);
        storage.Reset(grid);
    }

    [Fact]
    public void SparseStorage_PrivateClosestQueryHelpers_ShouldHandleCapacityAndPruningEdges()
    {
        int[] stack = new int[1];
        int stackCount = 0;

        InvokePushChildIfWithinBest(0, Fixed64.One, stack, ref stackCount, Fixed64.Zero);
        Assert.Equal(0, stackCount);

        InvokePushChildIfWithinBest(0, Fixed64.Zero, stack, ref stackCount, Fixed64.One);
        Assert.Equal(1, stackCount);
        Assert.Equal(0, stack[0]);

        Assert.Equal(1, InvokeGetClosestVoxelTreeCapacity(1));
        Assert.Equal(4, InvokeGetClosestVoxelTreeCapacity(2));
        Assert.Equal(int.MaxValue, InvokeGetClosestVoxelTreeCapacity(int.MaxValue));

        Voxel candidate = CreateInitializedVoxel(new VoxelIndex(0, 0, 0));
        Voxel current = CreateInitializedVoxel(new VoxelIndex(1, 0, 0));

        try
        {
            Assert.True(InvokeIsBetterClosestVoxel(candidate, Fixed64.Zero, null, Fixed64.One));
            Assert.True(InvokeIsBetterClosestVoxel(candidate, Fixed64.Zero, current, Fixed64.One));
            Assert.False(InvokeIsBetterClosestVoxel(current, Fixed64.One, candidate, Fixed64.One));
        }
        finally
        {
            candidate.Reset();
            current.Reset();
        }
    }

    [Fact]
    public void SparseVoxelBlock_ShouldNoOpWhenUninitialized()
    {
        SparseVoxelBlock block = new SparseVoxelBlock();
        SwiftList<Voxel> voxels = new SwiftList<Voxel>();
        SwiftHashSet<Voxel> voxelRedundancy = new SwiftHashSet<Voxel>();
        VoxelGrid grid = new VoxelGrid();
        VoxelIndex index = new VoxelIndex(0, 0, 0);

        Assert.False(block.TryGetVoxel(index, out Voxel voxel));
        Assert.Null(voxel);
        Assert.False(block.TryRemoveVoxel(grid, index, out Voxel removed));
        Assert.Null(removed);

        block.AddVoxelsInIndexRange(index, index, voxels, voxelRedundancy);
        block.Reset(grid);

        Assert.Empty(voxels);
        Assert.Equal(-1, block.CellKey);
        Assert.Null(block.ScanCell);
        Assert.Equal(0, block.Count);
    }

    [Fact]
    public void SparseVoxelBlock_ShouldGrowFromZeroCapacityAndReuseExistingCapacity()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0)),
            out ushort gridIndex));
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        SparseVoxelBlock block = new SparseVoxelBlock();

        block.Initialize(grid, cellKey: 0, capacity: 0);

        Assert.True(block.TryAddVoxel(grid, new VoxelIndex(0, 0, 0), out Voxel first));
        Assert.NotNull(first);
        Assert.True(block.TryAddVoxel(grid, new VoxelIndex(1, 0, 0), out Voxel second));
        Assert.NotNull(second);
        Assert.Equal(2, block.Count);

        block.Reset(grid);
    }

    private static DenseVoxelGridStorage GetDenseStorage(VoxelGrid grid)
    {
        FieldInfo field = typeof(VoxelGrid).GetField(
            "_denseStorage",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find VoxelGrid._denseStorage.");

        return (DenseVoxelGridStorage)field.GetValue(grid);
    }

    private static int InvokeGetClosestVoxelTreeCapacity(int voxelCapacity)
    {
        MethodInfo method = typeof(SparseVoxelGridStorage).GetMethod(
            "GetClosestVoxelTreeCapacity",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SparseVoxelGridStorage.GetClosestVoxelTreeCapacity.");

        return (int)method.Invoke(null, new object[] { voxelCapacity });
    }

    private static void InvokePushChildIfWithinBest(
        int childIndex,
        Fixed64 childDistanceSquared,
        int[] stack,
        ref int stackCount,
        Fixed64 bestDistanceSquared)
    {
        MethodInfo method = typeof(SparseVoxelGridStorage).GetMethod(
            "PushChildIfWithinBest",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SparseVoxelGridStorage.PushChildIfWithinBest.");
        object[] arguments = { childIndex, childDistanceSquared, stack, stackCount, bestDistanceSquared };

        method.Invoke(null, arguments);
        stackCount = (int)arguments[3];
    }

    private static bool InvokeIsBetterClosestVoxel(
        Voxel candidate,
        Fixed64 candidateDistanceSquared,
        Voxel current,
        Fixed64 currentDistanceSquared)
    {
        MethodInfo method = typeof(SparseVoxelGridStorage).GetMethod(
            "IsBetterClosestVoxel",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SparseVoxelGridStorage.IsBetterClosestVoxel.");

        return (bool)method.Invoke(null, new object[] { candidate, candidateDistanceSquared, current, currentDistanceSquared });
    }

    private static Voxel CreateInitializedVoxel(VoxelIndex index)
    {
        Voxel voxel = new Voxel();
        voxel.Initialize(
            new WorldVoxelIndex(1, 1, 1, index),
            new Vector3d(index.x, index.y, index.z),
            scanCellKey: 0,
            isBoundaryVoxel: false,
            gridVersion: 1);
        return voxel;
    }
}
