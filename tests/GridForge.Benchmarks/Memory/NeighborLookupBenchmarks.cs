using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class NeighborLookupBenchmarks
{
    private const int LookupCount = 128;

    private NeighborLookup[] _lookups;
    private GridWorld _world;

    public int Rounds { get; set; } = 128;

    [IterationSetup(Target = nameof(ResolveBoundaryNeighbors))]
    public void SetupIteration()
    {
        InitializeScenario();
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "Boundary neighbor lookups")]
    [BenchmarkCategory("Memory", "Neighbors")]
    public int ResolveBoundaryNeighbors()
    {
        return ExecuteLookups();
    }

    private void InitializeScenario()
    {
        _world = BenchmarkEnvironment.PrepareWorld();

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
                if (!_world.TryAddGrid(configurations[configurationIndex++], out ushort gridIndex))
                    throw new InvalidOperationException($"Unable to allocate neighbor benchmark grid ({x}, {z}).");

                if (x == 1 && z == 1)
                    centerIndex = gridIndex;
            }
        }

        if (centerIndex == ushort.MaxValue)
            throw new InvalidOperationException("Unable to resolve center grid for neighbor benchmark.");

        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];
        _lookups = BuildLookups(centerGrid);
    }

    private static NeighborLookup[] BuildLookups(VoxelGrid centerGrid)
    {
        NeighborLookup[] lookups = new NeighborLookup[LookupCount];
        int index = 0;

        for (int z = 0; z <= 31; z++)
        {
            lookups[index++] = new NeighborLookup(centerGrid, GetVoxel(centerGrid, 31, z), RectangularDirection.East);
            lookups[index++] = new NeighborLookup(centerGrid, GetVoxel(centerGrid, 0, z), RectangularDirection.West);
        }

        for (int x = 1; x < 31; x++)
        {
            lookups[index++] = new NeighborLookup(centerGrid, GetVoxel(centerGrid, x, 31), RectangularDirection.North);
            lookups[index++] = new NeighborLookup(centerGrid, GetVoxel(centerGrid, x, 0), RectangularDirection.South);
        }

        lookups[index++] = new NeighborLookup(centerGrid, GetVoxel(centerGrid, 31, 31), RectangularDirection.NorthEast);
        lookups[index++] = new NeighborLookup(centerGrid, GetVoxel(centerGrid, 0, 31), RectangularDirection.NorthWest);
        lookups[index++] = new NeighborLookup(centerGrid, GetVoxel(centerGrid, 31, 0), RectangularDirection.SouthEast);
        lookups[index++] = new NeighborLookup(centerGrid, GetVoxel(centerGrid, 0, 0), RectangularDirection.SouthWest);

        if (index != LookupCount)
            throw new InvalidOperationException($"Expected {LookupCount} neighbor lookups, but built {index}.");

        return lookups;
    }

    private static Voxel GetVoxel(VoxelGrid centerGrid, int x, int z)
    {
        Vector3d position = new(x, 0, z);
        if (!centerGrid.TryGetVoxel(position, out Voxel voxel))
            throw new InvalidOperationException($"Unable to resolve center grid voxel at {position}.");

        return voxel;
    }

    private int ExecuteLookups()
    {
        int hitCount = 0;

        for (int round = 0; round < Rounds; round++)
        {
            for (int i = 0; i < _lookups.Length; i++)
            {
                if (_lookups[i].Voxel.TryGetNeighbor(
                    _lookups[i].OwnerGrid,
                    _lookups[i].Direction,
                    out _))
                    hitCount++;
            }
        }

        return hitCount;
    }

    private readonly struct NeighborLookup
    {
        public readonly VoxelGrid OwnerGrid;
        public readonly Voxel Voxel;
        public readonly RectangularDirection Direction;

        public NeighborLookup(VoxelGrid ownerGrid, Voxel voxel, RectangularDirection direction)
        {
            OwnerGrid = ownerGrid;
            Voxel = voxel;
            Direction = direction;
        }
    }
}
