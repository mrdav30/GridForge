using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Collections.Generic;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class NeighborCacheBenchmarks
{
    private NeighborLookup[] _lookups;

    public int Rounds { get; set; } = 128;

    [IterationSetup(Target = nameof(ResolveBoundaryNeighbors_WithoutCache))]
    public void SetupUncachedIteration()
    {
        InitializeScenario(primeCache: false);
    }

    [IterationSetup(Target = nameof(ResolveBoundaryNeighbors_WithCache))]
    public void SetupCachedIteration()
    {
        InitializeScenario(primeCache: true);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "Boundary neighbor lookups without cache")]
    [BenchmarkCategory("Memory", "Caching", "Neighbors")]
    public int ResolveBoundaryNeighbors_WithoutCache()
    {
        return ExecuteLookups(useCache: false);
    }

    [Benchmark(Description = "Boundary neighbor lookups with cache")]
    [BenchmarkCategory("Memory", "Caching", "Neighbors")]
    public int ResolveBoundaryNeighbors_WithCache()
    {
        return ExecuteLookups(useCache: true);
    }

    private void InitializeScenario(bool primeCache)
    {
        BenchmarkEnvironment.PrepareWorld();

        GridConfiguration[] configurations = BenchmarkScenarioFactory.CreateTiledFlatGridConfigurations(
            tilesX: 3,
            tilesZ: 3,
            extent: 31,
            scanCellSize: 8,
            overlapBoundaries: true,
            originX: -31,
            originZ: -31);

        ushort centerIndex = ushort.MaxValue;
        int configurationIndex = 0;

        for (int z = 0; z < 3; z++)
        {
            for (int x = 0; x < 3; x++)
            {
                if (!GlobalGridManager.TryAddGrid(configurations[configurationIndex++], out ushort gridIndex))
                    throw new InvalidOperationException($"Unable to allocate neighbor benchmark grid ({x}, {z}).");

                if (x == 1 && z == 1)
                    centerIndex = gridIndex;
            }
        }

        if (centerIndex == ushort.MaxValue)
            throw new InvalidOperationException("Unable to resolve center grid for neighbor benchmark.");

        VoxelGrid centerGrid = GlobalGridManager.ActiveGrids[centerIndex];
        _lookups = BuildLookups(centerGrid);

        if (primeCache)
        {
            for (int i = 0; i < _lookups.Length; i++)
            {
                if (!_lookups[i].Voxel.TryGetNeighborFromDirection(_lookups[i].Direction, out _, useCache: true))
                {
                    throw new InvalidOperationException(
                        $"Unable to prime cached lookup {i} for direction {_lookups[i].Direction}.");
                }
            }
        }
    }

    private static NeighborLookup[] BuildLookups(VoxelGrid centerGrid)
    {
        List<NeighborLookup> lookups = new();

        for (int z = 0; z <= 31; z++)
        {
            lookups.Add(new NeighborLookup(GetVoxel(centerGrid, 31, z), SpatialDirection.East));
            lookups.Add(new NeighborLookup(GetVoxel(centerGrid, 0, z), SpatialDirection.West));
        }

        for (int x = 1; x < 31; x++)
        {
            lookups.Add(new NeighborLookup(GetVoxel(centerGrid, x, 31), SpatialDirection.North));
            lookups.Add(new NeighborLookup(GetVoxel(centerGrid, x, 0), SpatialDirection.South));
        }

        lookups.Add(new NeighborLookup(GetVoxel(centerGrid, 31, 31), SpatialDirection.NorthEast));
        lookups.Add(new NeighborLookup(GetVoxel(centerGrid, 0, 31), SpatialDirection.NorthWest));
        lookups.Add(new NeighborLookup(GetVoxel(centerGrid, 31, 0), SpatialDirection.SouthEast));
        lookups.Add(new NeighborLookup(GetVoxel(centerGrid, 0, 0), SpatialDirection.SouthWest));

        return lookups.ToArray();
    }

    private static Voxel GetVoxel(VoxelGrid centerGrid, int x, int z)
    {
        Vector3d position = new(x, 0, z);
        if (!centerGrid.TryGetVoxel(position, out Voxel voxel))
            throw new InvalidOperationException($"Unable to resolve center grid voxel at {position}.");

        return voxel;
    }

    private int ExecuteLookups(bool useCache)
    {
        int hitCount = 0;

        for (int round = 0; round < Rounds; round++)
        {
            for (int i = 0; i < _lookups.Length; i++)
            {
                if (_lookups[i].Voxel.TryGetNeighborFromDirection(_lookups[i].Direction, out _, useCache))
                    hitCount++;
            }
        }

        return hitCount;
    }

    private readonly struct NeighborLookup
    {
        public readonly Voxel Voxel;
        public readonly SpatialDirection Direction;

        public NeighborLookup(Voxel voxel, SpatialDirection direction)
        {
            Voxel = voxel;
            Direction = direction;
        }
    }
}
