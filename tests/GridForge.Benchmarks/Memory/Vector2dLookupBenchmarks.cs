using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class Vector2dLookupBenchmarks
{
    private GridWorld _world;
    private Vector3d[] _positions3D;
    private Vector2d[] _positions2D;

    public int QueryCount { get; set; } = 1024;

    [IterationSetup]
    public void SetupIteration()
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools: false);

        GridConfiguration[] configurations = BenchmarkScenarioFactory.CreateTiledFlatGridConfigurations(
            tilesX: 2,
            tilesZ: 2,
            extent: 63,
            scanCellSize: 8);

        for (int i = 0; i < configurations.Length; i++)
        {
            if (!_world.TryAddGrid(configurations[i], out _))
                throw new InvalidOperationException($"Unable to allocate lookup benchmark grid {i}.");
        }

        _positions3D = new Vector3d[QueryCount];
        _positions2D = new Vector2d[QueryCount];

        for (int i = 0; i < QueryCount; i++)
        {
            int x = i & 127;
            int z = (i * 17) & 127;
            _positions3D[i] = new Vector3d(x, 0, z);
            _positions2D[i] = new Vector2d(x, z);
        }
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "TryGetVoxel Vector3d lookups")]
    [BenchmarkCategory("Lookup", "Vector3d")]
    public int TryGetVoxel_Vector3d()
    {
        int checksum = 0;

        for (int i = 0; i < _positions3D.Length; i++)
        {
            if (_world.TryGetVoxel(_positions3D[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "TryGetVoxel Vector2d lookups")]
    [BenchmarkCategory("Lookup", "Vector2d")]
    public int TryGetVoxel_Vector2d()
    {
        int checksum = 0;

        for (int i = 0; i < _positions2D.Length; i++)
        {
            if (_world.TryGetVoxel(_positions2D[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }
}
