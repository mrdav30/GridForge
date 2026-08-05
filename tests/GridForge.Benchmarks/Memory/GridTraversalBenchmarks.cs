using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridTraversalBenchmarks
{
    private GridWorld _world;
    private Voxel[] _voxels;
    private SwiftHashSet<WorldVoxelIndex> _visited;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools: false);
        GridConfiguration configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(63, 0, 63));

        if (!_world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate traversal benchmark grid.");

        _voxels = CaptureVoxels(_world.ActiveGrids[gridIndex]);
        _visited = new SwiftHashSet<WorldVoxelIndex>(_voxels.Length * 2);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark]
    [BenchmarkCategory("Traversal", "Identity")]
    public int TryVisitUniqueAcrossVoxels()
    {
        _visited.Clear();
        GridTraversalState traversal = new GridTraversalState(
            _world,
            GridTraversalPaddingMode.MaxCellEdge);
        int visitCount = 0;

        for (int i = 0; i < _voxels.Length; i++)
        {
            if (traversal.TryVisitUnique(_voxels[i], _visited, out _))
                visitCount++;
        }

        for (int i = 0; i < _voxels.Length; i++)
        {
            if (traversal.TryVisitUnique(_voxels[i], _visited, out _))
                visitCount++;
        }

        return visitCount;
    }

    private static Voxel[] CaptureVoxels(VoxelGrid grid)
    {
        Voxel[] voxels = new Voxel[grid.ConfiguredVoxelCount];
        int index = 0;
        foreach (Voxel voxel in grid.EnumerateVoxels())
            voxels[index++] = voxel;

        return voxels;
    }
}
