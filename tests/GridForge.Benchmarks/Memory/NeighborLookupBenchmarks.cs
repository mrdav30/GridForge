using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class NeighborLookupBenchmarks
{
    private const int LookupCount = 128;
    private const int ContactLookupCount = 64;
    private const int MixedLookupCount = 16;
    private const int ContactResultCapacity = 128;
    private const int RectangularDirectionResultCapacity = 26;
    private const int HexDirectionResultCapacity = 20;

    private NeighborLookup[] _lookups;
    private ContactLookup[] _contactLookups;
    private Voxel[] _voxels;
    private VoxelGrid _grid;
    private GridWorld _world;
    private SwiftList<Voxel> _contactResults;
    private SwiftList<(RectangularDirection Direction, Voxel Voxel)> _rectangularResults;
    private SwiftList<(HexDirection Direction, Voxel Voxel)> _hexResults;

    public int Rounds { get; set; } = 128;

    [IterationSetup(Target = nameof(ResolveBoundaryNeighbors))]
    public void SetupIteration()
    {
        InitializeScenario();
    }

    [IterationSetup(Target = nameof(ResolveSourceGridContactNeighbors))]
    public void SetupSourceGridContactIteration()
    {
        InitializeSourceGridContactScenario();
    }

    [IterationSetup(Target = nameof(ResolveSameTopologyContactNeighbors))]
    public void SetupSameTopologyContactIteration()
    {
        InitializeSameTopologyContactScenario();
    }

    [IterationSetup(Target = nameof(ResolveNoMixedCandidateContactNeighbors))]
    public void SetupNoMixedCandidateContactIteration()
    {
        InitializeNoMixedCandidateContactScenario();
    }

    [IterationSetup(Target = nameof(ResolvePointyTopMixedContactNeighbors))]
    public void SetupPointyTopMixedContactIteration()
    {
        InitializeMixedContactScenario(HexOrientation.PointyTop);
    }

    [IterationSetup(Target = nameof(ResolveFlatTopMixedContactNeighbors))]
    public void SetupFlatTopMixedContactIteration()
    {
        InitializeMixedContactScenario(HexOrientation.FlatTop);
    }

    [IterationSetup(Target = nameof(ResolveManyCandidateGridContactNeighbors))]
    public void SetupManyCandidateGridContactIteration()
    {
        InitializeManyCandidateGridContactScenario();
    }

    [IterationSetup(Target = nameof(ResolveSparseMissingContactNeighbors))]
    public void SetupSparseMissingContactIteration()
    {
        InitializeSparseMissingContactScenario();
    }

    [IterationSetup(Target = nameof(ResolveRectangularDirectionLabeledNeighbors))]
    public void SetupRectangularDirectionLabeledIteration()
    {
        InitializeDirectionLabeledRectangularScenario();
    }

    [IterationSetup(Target = nameof(ResolveHexDirectionLabeledNeighbors))]
    public void SetupHexDirectionLabeledIteration()
    {
        InitializeDirectionLabeledHexScenario();
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        _contactResults?.Clear();
        _rectangularResults?.Clear();
        _hexResults?.Clear();
        BenchmarkEnvironment.ResetWorld();
        _lookups = null;
        _contactLookups = null;
        _voxels = null;
        _grid = null;
        _world = null;
        _contactResults = null;
        _rectangularResults = null;
        _hexResults = null;
    }

    [Benchmark(Baseline = true, Description = "Boundary neighbor lookups")]
    [BenchmarkCategory("Memory", "Neighbors")]
    public int ResolveBoundaryNeighbors()
    {
        return ExecuteLookups();
    }

    [Benchmark(Description = "Source-grid contact neighbors")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact")]
    public int ResolveSourceGridContactNeighbors()
    {
        return ExecuteContactLookups();
    }

    [Benchmark(Description = "Same-topology conjoined contact neighbors")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact")]
    public int ResolveSameTopologyContactNeighbors()
    {
        return ExecuteContactLookups();
    }

    [Benchmark(Description = "No mixed contact candidates nearby")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact")]
    public int ResolveNoMixedCandidateContactNeighbors()
    {
        return ExecuteContactLookups();
    }

    [Benchmark(Description = "Pointy-top mixed contact neighbors")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact", "Hex")]
    public int ResolvePointyTopMixedContactNeighbors()
    {
        return ExecuteContactLookups();
    }

    [Benchmark(Description = "Flat-top mixed contact neighbors")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact", "Hex")]
    public int ResolveFlatTopMixedContactNeighbors()
    {
        return ExecuteContactLookups();
    }

    [Benchmark(Description = "Many nearby candidate grids contact query")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact")]
    public int ResolveManyCandidateGridContactNeighbors()
    {
        return ExecuteContactLookups();
    }

    [Benchmark(Description = "Sparse target mostly missing contact query")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact", "Sparse")]
    public int ResolveSparseMissingContactNeighbors()
    {
        return ExecuteContactLookups();
    }

    [Benchmark(Description = "Rectangular direction-labeled neighbors")]
    [BenchmarkCategory("Memory", "Neighbors", "Directed")]
    public int ResolveRectangularDirectionLabeledNeighbors()
    {
        int hitCount = 0;

        for (int round = 0; round < Rounds; round++)
        {
            for (int i = 0; i < _voxels.Length; i++)
            {
                _voxels[i].GetRectangularNeighborsInto(_grid, _rectangularResults);
                hitCount += _rectangularResults.Count;
            }
        }

        return hitCount;
    }

    [Benchmark(Description = "Hex direction-labeled neighbors")]
    [BenchmarkCategory("Memory", "Neighbors", "Directed", "Hex")]
    public int ResolveHexDirectionLabeledNeighbors()
    {
        int hitCount = 0;

        for (int round = 0; round < Rounds; round++)
        {
            for (int i = 0; i < _voxels.Length; i++)
            {
                _voxels[i].GetHexNeighborsInto(_grid, _hexResults);
                hitCount += _hexResults.Count;
            }
        }

        return hitCount;
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

    private void InitializeSourceGridContactScenario()
    {
        InitializeSingleDenseRectangularGrid();
        _contactLookups = BuildContactLookups(_grid, VoxelNeighborScope.SourceGrid, ContactLookupCount);
        _contactResults = new SwiftList<Voxel>(ContactResultCapacity);
    }

    private void InitializeSameTopologyContactScenario()
    {
        _world = BenchmarkEnvironment.PrepareWorld();

        GridConfiguration[] configurations = BenchmarkScenarioFactory.CreateTiledFlatGridConfigurations(
            tilesX: 2,
            tilesZ: 1,
            extent: 31,
            scanCellSize: 8,
            overlapBoundaries: true);

        if (!_world.TryAddGrid(configurations[0], out ushort sourceIndex))
            throw new InvalidOperationException("Unable to allocate same-topology source grid.");

        if (!_world.TryAddGrid(configurations[1], out _))
            throw new InvalidOperationException("Unable to allocate same-topology target grid.");

        _grid = _world.ActiveGrids[sourceIndex];
        _contactLookups = BuildBoundaryContactLookups(_grid, VoxelNeighborScope.SameTopologyGrids, ContactLookupCount);
        _contactResults = new SwiftList<Voxel>(ContactResultCapacity);
    }

    private void InitializeNoMixedCandidateContactScenario()
    {
        InitializeSingleDenseRectangularGrid();
        _contactLookups = BuildContactLookups(_grid, VoxelNeighborScope.MixedTopologyGrids, ContactLookupCount);
        _contactResults = new SwiftList<Voxel>(ContactResultCapacity);
    }

    private void InitializeMixedContactScenario(HexOrientation orientation)
    {
        _world = BenchmarkEnvironment.PrepareWorld(spatialGridCellSize: 16);

        GridConfiguration rectangularConfiguration = new(Vector3d.Zero, Vector3d.Zero);
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation);
        GridConfiguration hexConfiguration = CreateHexConfiguration(
            new Vector3d(1, 0, 0),
            hexMetrics,
            new VoxelIndex(0, 0, 0));

        if (!_world.TryAddGrid(rectangularConfiguration, out ushort rectangularIndex))
            throw new InvalidOperationException("Unable to allocate mixed-contact rectangular grid.");

        if (!_world.TryAddGrid(hexConfiguration, out _))
            throw new InvalidOperationException("Unable to allocate mixed-contact hex grid.");

        _grid = _world.ActiveGrids[rectangularIndex];
        _contactLookups = BuildContactLookups(_grid, VoxelNeighborScope.MixedTopologyGrids, MixedLookupCount);
        _contactResults = new SwiftList<Voxel>(ContactResultCapacity);
    }

    private void InitializeManyCandidateGridContactScenario()
    {
        _world = BenchmarkEnvironment.PrepareWorld(spatialGridCellSize: 64);

        if (!_world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out ushort sourceIndex))
            throw new InvalidOperationException("Unable to allocate many-candidate source grid.");

        int added = 0;
        for (int z = 0; z < 8; z++)
        {
            for (int x = 1; x <= 8; x++)
            {
                Vector3d min = new(x * 3, 0, z * 3);
                GridConfiguration configuration = new(min, min);
                if (!_world.TryAddGrid(configuration, out _))
                    throw new InvalidOperationException("Unable to allocate nearby candidate grid.");

                added++;
            }
        }

        if (added != 64)
            throw new InvalidOperationException($"Expected 64 nearby candidate grids, but allocated {added}.");

        _grid = _world.ActiveGrids[sourceIndex];
        _contactLookups = BuildContactLookups(_grid, VoxelNeighborScope.All, MixedLookupCount);
        _contactResults = new SwiftList<Voxel>(ContactResultCapacity);
    }

    private void InitializeSparseMissingContactScenario()
    {
        _world = BenchmarkEnvironment.PrepareWorld(spatialGridCellSize: 32);

        GridTopologyMetrics wideMetrics = GridTopologyMetrics.Rectangular(new Fixed64(16), Fixed64.One, new Fixed64(16));
        GridConfiguration sourceConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: wideMetrics);
        GridConfiguration sparseConfiguration = new(
            new Vector3d(-16, 0, -16),
            new Vector3d(16, 0, 16),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, Fixed64.One),
            storageKind: GridStorageKind.Sparse);

        VoxelIndex[] configuredVoxels =
        {
            new(16, 0, 16)
        };

        if (!_world.TryAddGrid(sourceConfiguration, out ushort sourceIndex))
            throw new InvalidOperationException("Unable to allocate sparse-missing source grid.");

        if (!_world.TryAddGrid(sparseConfiguration, configuredVoxels, out _))
            throw new InvalidOperationException("Unable to allocate sparse-missing target grid.");

        _grid = _world.ActiveGrids[sourceIndex];
        _contactLookups = BuildContactLookups(_grid, VoxelNeighborScope.SameTopologyGrids, MixedLookupCount);
        _contactResults = new SwiftList<Voxel>(ContactResultCapacity);
    }

    private void InitializeDirectionLabeledRectangularScenario()
    {
        InitializeSingleDenseRectangularGrid();
        _voxels = BuildInteriorVoxels(_grid, ContactLookupCount);
        _rectangularResults = new SwiftList<(RectangularDirection Direction, Voxel Voxel)>(RectangularDirectionResultCapacity);
    }

    private void InitializeDirectionLabeledHexScenario()
    {
        _world = BenchmarkEnvironment.PrepareWorld(spatialGridCellSize: 64);

        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(7, 1, 7));

        if (!_world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate hex direction benchmark grid.");

        _grid = _world.ActiveGrids[gridIndex];
        _voxels = BuildHexInteriorVoxels(_grid, ContactLookupCount);
        _hexResults = new SwiftList<(HexDirection Direction, Voxel Voxel)>(HexDirectionResultCapacity);
    }

    private void InitializeSingleDenseRectangularGrid()
    {
        _world = BenchmarkEnvironment.PrepareWorld();

        if (!_world.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(31, 0, 31), scanCellSize: 8),
                out ushort gridIndex))
        {
            throw new InvalidOperationException("Unable to allocate dense rectangular benchmark grid.");
        }

        _grid = _world.ActiveGrids[gridIndex];
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

    private static ContactLookup[] BuildContactLookups(
        VoxelGrid grid,
        VoxelNeighborScope scope,
        int count)
    {
        ContactLookup[] lookups = new ContactLookup[count];

        int index = 0;
        for (int z = 1; z < grid.Length - 1 && index < count; z++)
        {
            for (int x = 1; x < grid.Width - 1 && index < count; x++)
                lookups[index++] = new ContactLookup(grid, GetVoxel(grid, x, z), scope);
        }

        if (index == 0)
            lookups[index++] = new ContactLookup(grid, GetVoxel(grid, 0, 0), scope);

        RepeatContactLookups(lookups, index);
        return lookups;
    }

    private static ContactLookup[] BuildBoundaryContactLookups(
        VoxelGrid grid,
        VoxelNeighborScope scope,
        int count)
    {
        ContactLookup[] lookups = new ContactLookup[count];
        int index = 0;

        for (int z = 0; z <= 31 && index < count; z++)
            lookups[index++] = new ContactLookup(grid, GetVoxel(grid, 31, z), scope);

        RepeatContactLookups(lookups, index);
        return lookups;
    }

    private static Voxel[] BuildInteriorVoxels(VoxelGrid grid, int count)
    {
        Voxel[] voxels = new Voxel[count];
        int index = 0;

        for (int z = 1; z < grid.Length - 1 && index < count; z++)
        {
            for (int x = 1; x < grid.Width - 1 && index < count; x++)
                voxels[index++] = GetVoxel(grid, x, z);
        }

        RepeatVoxels(voxels, index);

        return voxels;
    }

    private static Voxel[] BuildHexInteriorVoxels(VoxelGrid grid, int count)
    {
        Voxel[] voxels = new Voxel[count];
        int index = 0;

        for (int z = 1; z < grid.Length - 1 && index < count; z++)
        {
            for (int x = 1; x < grid.Width - 1 && index < count; x++)
            {
                if (!grid.TryGetVoxel(new VoxelIndex(x, 0, z), out Voxel voxel))
                    throw new InvalidOperationException($"Unable to resolve hex benchmark voxel at {x},0,{z}.");

                voxels[index++] = voxel;
            }
        }

        RepeatVoxels(voxels, index);

        return voxels;
    }

    private static void RepeatContactLookups(ContactLookup[] lookups, int populatedCount)
    {
        for (int index = populatedCount; index < lookups.Length; index++)
            lookups[index] = lookups[index % populatedCount];
    }

    private static void RepeatVoxels(Voxel[] voxels, int populatedCount)
    {
        for (int index = populatedCount; index < voxels.Length; index++)
            voxels[index] = voxels[index % populatedCount];
    }

    private static Voxel GetVoxel(VoxelGrid centerGrid, int x, int z)
    {
        Vector3d position = new(x, 0, z);
        if (!centerGrid.TryGetVoxel(position, out Voxel voxel))
            throw new InvalidOperationException($"Unable to resolve center grid voxel at {position}.");

        return voxel;
    }

    private int ExecuteContactLookups()
    {
        int hitCount = 0;

        for (int round = 0; round < Rounds; round++)
        {
            for (int i = 0; i < _contactLookups.Length; i++)
            {
                ContactLookup lookup = _contactLookups[i];
                lookup.Voxel.GetNeighborsInto(lookup.OwnerGrid, _contactResults, lookup.Scope);
                hitCount += _contactResults.Count;
            }
        }

        return hitCount;
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

    private readonly struct ContactLookup
    {
        public readonly VoxelGrid OwnerGrid;
        public readonly Voxel Voxel;
        public readonly VoxelNeighborScope Scope;

        public ContactLookup(VoxelGrid ownerGrid, Voxel voxel, VoxelNeighborScope scope)
        {
            OwnerGrid = ownerGrid;
            Voxel = voxel;
            Scope = scope;
        }
    }

    private static GridConfiguration CreateHexConfiguration(
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex) =>
        CreateHexConfiguration(Vector3d.Zero, metrics, maxIndex);

    private static GridConfiguration CreateHexConfiguration(
        Vector3d boundsMin,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex)
    {
        Vector3d boundsMax = boundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            boundsMin,
            boundsMax,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);
    }
}
