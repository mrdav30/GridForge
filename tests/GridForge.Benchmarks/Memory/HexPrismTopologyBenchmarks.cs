using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class HexPrismTopologyBenchmarks
{
    private const int GridSide = 64;
    private const int GridExtent = GridSide - 1;
    private const int LookupCount = 1 << 16;
    private const int ConstructionRounds = 32;
    private const int TraceRounds = 256;
    private const int CoverageRounds = 128;
    private const int BlockerRounds = 128;
    private const int RegistrationRounds = 16;
    private const int ScanRounds = 256;

    private readonly GridTopologyMetrics _pointyMetrics = GridTopologyMetrics.Hex(
        new Fixed64(2),
        Fixed64.One,
        HexOrientation.PointyTop);

    private readonly GridTopologyMetrics _flatMetrics = GridTopologyMetrics.Hex(
        new Fixed64(2),
        Fixed64.One,
        HexOrientation.FlatTop);

    private VoxelIndex[] _lookupIndices;
    private Vector3d[] _rectangularLookupPositions;
    private Vector3d[] _pointyLookupPositions;
    private Vector3d[] _flatLookupPositions;
    private Vector3d[] _mixedLookupPositions;
    private BenchmarkOccupant[] _registrationOccupants;
    private BenchmarkOccupant[] _scanOccupants;
    private GridWorld _world;
    private VoxelGrid _rectangularGrid;
    private VoxelGrid _pointyGrid;
    private VoxelGrid _flatGrid;
    private GridConfiguration _rectangularConstructionConfiguration;
    private GridConfiguration _pointyConstructionConfiguration;
    private GridConfiguration _flatConstructionConfiguration;
    private Vector3d _pointyLineStart;
    private Vector3d _pointyLineEnd;
    private Vector3d _coverageSmallMin;
    private Vector3d _coverageSmallMax;
    private Vector3d _coverageMediumMin;
    private Vector3d _coverageMediumMax;
    private Vector3d _coverageLargeMin;
    private Vector3d _coverageLargeMax;
    private BoundsBlocker _hexBlocker;
    private SwiftList<IVoxelOccupant> _scanResults;
    private GridScanScratch _scanScratch;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _lookupIndices = CreateLookupIndices();
    }

    [IterationSetup]
    public void SetupIteration()
    {
        InitializeScenario();
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        _hexBlocker?.Reset();
        _scanResults?.Clear();
        _scanScratch?.Clear();

        BenchmarkEnvironment.ResetWorld();

        _hexBlocker = null;
        _scanResults = null;
        _scanScratch = null;
    }

    [Benchmark(Baseline = true, Description = "Rectangular world-to-index lookup baseline")]
    [BenchmarkCategory("Hex", "Topology", "Lookup", "RectangularBaseline")]
    public int RectangularWorldToIndexLookup_Baseline()
    {
        return ExecuteWorldToIndexLookup(_rectangularGrid, _rectangularLookupPositions);
    }

    [Benchmark(Description = "Pointy-top hex world-to-index lookup")]
    [BenchmarkCategory("Hex", "Topology", "Lookup")]
    public int HexPointyWorldToIndexLookup()
    {
        return ExecuteWorldToIndexLookup(_pointyGrid, _pointyLookupPositions);
    }

    [Benchmark(Description = "Flat-top hex world-to-index lookup")]
    [BenchmarkCategory("Hex", "Topology", "Lookup")]
    public int HexFlatWorldToIndexLookup()
    {
        return ExecuteWorldToIndexLookup(_flatGrid, _flatLookupPositions);
    }

    [Benchmark(Description = "Pointy-top hex index-to-world projection")]
    [BenchmarkCategory("Hex", "Topology", "Projection")]
    public int HexPointyIndexToWorldProjection()
    {
        return ExecuteIndexToWorldProjection(_pointyGrid);
    }

    [Benchmark(Description = "Flat-top hex index-to-world projection")]
    [BenchmarkCategory("Hex", "Topology", "Projection")]
    public int HexFlatIndexToWorldProjection()
    {
        return ExecuteIndexToWorldProjection(_flatGrid);
    }

    [Benchmark(Description = "Construct and remove rectangular baseline grid")]
    [BenchmarkCategory("Hex", "Construction", "RectangularBaseline")]
    public int ConstructRectangularGrid_Baseline()
    {
        return ExecuteConstruction(_rectangularConstructionConfiguration);
    }

    [Benchmark(Description = "Construct and remove pointy-top hex grid")]
    [BenchmarkCategory("Hex", "Construction")]
    public int ConstructHexPointyGrid()
    {
        return ExecuteConstruction(_pointyConstructionConfiguration);
    }

    [Benchmark(Description = "Construct and remove flat-top hex grid")]
    [BenchmarkCategory("Hex", "Construction")]
    public int ConstructHexFlatGrid()
    {
        return ExecuteConstruction(_flatConstructionConfiguration);
    }

    [Benchmark(Description = "Trace pointy-top hex line")]
    [BenchmarkCategory("Hex", "Tracing")]
    public int TraceHexPointyLine()
    {
        int count = 0;

        for (int i = 0; i < TraceRounds; i++)
        {
            foreach (GridVoxelSet traced in GridTracer.TraceLine(_world, _pointyLineStart, _pointyLineEnd))
                count += traced.Voxels.Count;
        }

        return count;
    }

    [Benchmark(Description = "Cover small pointy-top hex bounds")]
    [BenchmarkCategory("Hex", "Coverage")]
    public int CoverHexPointyBoundsSmall()
    {
        return CountCoveredVoxels(_coverageSmallMin, _coverageSmallMax);
    }

    [Benchmark(Description = "Cover medium pointy-top hex bounds")]
    [BenchmarkCategory("Hex", "Coverage")]
    public int CoverHexPointyBoundsMedium()
    {
        return CountCoveredVoxels(_coverageMediumMin, _coverageMediumMax);
    }

    [Benchmark(Description = "Cover large pointy-top hex bounds")]
    [BenchmarkCategory("Hex", "Coverage")]
    public int CoverHexPointyBoundsLarge()
    {
        return CountCoveredVoxels(_coverageLargeMin, _coverageLargeMax);
    }

    [Benchmark(Description = "Apply and remove cached hex blocker")]
    [BenchmarkCategory("Hex", "Blocker")]
    public int ApplyRemoveHexBlocker()
    {
        int blocked = 0;

        for (int i = 0; i < BlockerRounds; i++)
        {
            _hexBlocker.ApplyBlockage();
            if (_hexBlocker.IsBlocking)
                blocked++;

            _hexBlocker.RemoveBlockage();
        }

        return blocked;
    }

    [Benchmark(Description = "Register and deregister hex occupants")]
    [BenchmarkCategory("Hex", "Occupants")]
    public int RegisterDeregisterHexOccupants()
    {
        int operations = 0;

        for (int round = 0; round < RegistrationRounds; round++)
        {
            for (int i = 0; i < _registrationOccupants.Length; i++)
            {
                if (!GridOccupantManager.TryRegister(_world, _registrationOccupants[i]))
                    throw new InvalidOperationException($"Unable to register hex benchmark occupant {i}.");

                operations++;
            }

            for (int i = 0; i < _registrationOccupants.Length; i++)
            {
                if (!GridOccupantManager.TryDeregister(_world, _registrationOccupants[i]))
                    throw new InvalidOperationException($"Unable to deregister hex benchmark occupant {i}.");

                operations++;
            }
        }

        return operations;
    }

    [Benchmark(Description = "Hex radius scan into caller-owned scratch")]
    [BenchmarkCategory("Hex", "ScanQuery", "Scratch")]
    public int ScanHexRadiusInto()
    {
        int count = 0;

        for (int i = 0; i < ScanRounds; i++)
        {
            GridScanManager.ScanRadiusInto(
                _world,
                _pointyGrid.GetWorldPosition(new VoxelIndex(48, 0, 48)),
                new Fixed64(52),
                _scanResults,
                _scanScratch);

            count += _scanResults.Count;
        }

        return count;
    }

    [Benchmark(Description = "Mixed rectangular and hex world lookup")]
    [BenchmarkCategory("Hex", "Lookup", "MixedTopology")]
    public int MixedRectangularHexWorldLookup()
    {
        int checksum = 0;

        for (int i = 0; i < _mixedLookupPositions.Length; i++)
        {
            if (_world.TryGetGridAndVoxel(_mixedLookupPositions[i], out VoxelGrid grid, out Voxel voxel))
                checksum += grid.GridIndex + voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    private void InitializeScenario()
    {
        _world = BenchmarkEnvironment.PrepareWorld(spatialGridCellSize: 128);

        GridConfiguration rectangularConfiguration = new GridConfiguration(
            new Vector3d(0, 0, 0),
            new Vector3d(GridExtent, 0, GridExtent),
            scanCellSize: 8);
        GridConfiguration pointyConfiguration = CreateHexConfiguration(
            new Vector3d(256, 0, 0),
            _pointyMetrics,
            new VoxelIndex(GridExtent, 0, GridExtent));
        GridConfiguration flatConfiguration = CreateHexConfiguration(
            new Vector3d(768, 0, 0),
            _flatMetrics,
            new VoxelIndex(GridExtent, 0, GridExtent));

        _rectangularConstructionConfiguration = new GridConfiguration(
            new Vector3d(1600, 0, 0),
            new Vector3d(1600 + GridExtent, 0, GridExtent),
            scanCellSize: 8);
        _pointyConstructionConfiguration = CreateHexConfiguration(
            new Vector3d(1800, 0, 0),
            _pointyMetrics,
            new VoxelIndex(GridExtent, 0, GridExtent));
        _flatConstructionConfiguration = CreateHexConfiguration(
            new Vector3d(2400, 0, 0),
            _flatMetrics,
            new VoxelIndex(GridExtent, 0, GridExtent));

        _rectangularGrid = AddGrid(rectangularConfiguration);
        _pointyGrid = AddGrid(pointyConfiguration);
        _flatGrid = AddGrid(flatConfiguration);

        PopulateLookupPositions();
        InitializeTraceAndCoverageBounds();
        InitializeBlocker();
        InitializeOccupants();
    }

    private VoxelGrid AddGrid(GridConfiguration configuration)
    {
        if (!_world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate hex benchmark grid.");

        return _world.ActiveGrids[gridIndex];
    }

    private int ExecuteWorldToIndexLookup(VoxelGrid grid, Vector3d[] positions)
    {
        int checksum = 0;

        for (int i = 0; i < positions.Length; i++)
        {
            if (grid.TryGetVoxelIndex(positions[i], out VoxelIndex index))
                checksum += index.x + index.z;
        }

        return checksum;
    }

    private int ExecuteIndexToWorldProjection(VoxelGrid grid)
    {
        int checksum = 0;

        for (int i = 0; i < _lookupIndices.Length; i++)
        {
            Vector3d position = grid.GetWorldPosition(_lookupIndices[i]);
            checksum += position.X.FloorToInt() + position.Z.FloorToInt();
        }

        return checksum;
    }

    private int ExecuteConstruction(GridConfiguration configuration)
    {
        int configuredVoxelCount = 0;

        for (int i = 0; i < ConstructionRounds; i++)
        {
            if (!_world.TryAddGrid(configuration, out ushort gridIndex))
                throw new InvalidOperationException("Unable to allocate construction benchmark grid.");

            configuredVoxelCount += _world.ActiveGrids[gridIndex].ConfiguredVoxelCount;

            if (!_world.TryRemoveGrid(gridIndex))
                throw new InvalidOperationException("Unable to remove construction benchmark grid.");
        }

        return configuredVoxelCount;
    }

    private int CountCoveredVoxels(Vector3d min, Vector3d max)
    {
        int count = 0;

        for (int i = 0; i < CoverageRounds; i++)
        {
            foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(_world, min, max))
                count += covered.Voxels.Count;
        }

        return count;
    }

    private void PopulateLookupPositions()
    {
        _rectangularLookupPositions = new Vector3d[_lookupIndices.Length];
        _pointyLookupPositions = new Vector3d[_lookupIndices.Length];
        _flatLookupPositions = new Vector3d[_lookupIndices.Length];
        _mixedLookupPositions = new Vector3d[_lookupIndices.Length];

        for (int i = 0; i < _lookupIndices.Length; i++)
        {
            VoxelIndex index = _lookupIndices[i];
            _rectangularLookupPositions[i] = _rectangularGrid.GetWorldPosition(index);
            _pointyLookupPositions[i] = _pointyGrid.GetWorldPosition(index);
            _flatLookupPositions[i] = _flatGrid.GetWorldPosition(index);

            switch (i % 3)
            {
                case 0:
                    _mixedLookupPositions[i] = _rectangularLookupPositions[i];
                    break;
                case 1:
                    _mixedLookupPositions[i] = _pointyLookupPositions[i];
                    break;
                default:
                    _mixedLookupPositions[i] = _flatLookupPositions[i];
                    break;
            }
        }
    }

    private void InitializeTraceAndCoverageBounds()
    {
        _pointyLineStart = _pointyGrid.GetWorldPosition(new VoxelIndex(4, 0, 4));
        _pointyLineEnd = _pointyGrid.GetWorldPosition(new VoxelIndex(56, 0, 44));

        _coverageSmallMin = _pointyGrid.GetWorldPosition(new VoxelIndex(8, 0, 8));
        _coverageSmallMax = _pointyGrid.GetWorldPosition(new VoxelIndex(14, 0, 14));
        _coverageMediumMin = _pointyGrid.GetWorldPosition(new VoxelIndex(12, 0, 12));
        _coverageMediumMax = _pointyGrid.GetWorldPosition(new VoxelIndex(36, 0, 36));
        _coverageLargeMin = _pointyGrid.GetWorldPosition(new VoxelIndex(0, 0, 0));
        _coverageLargeMax = _pointyGrid.GetWorldPosition(new VoxelIndex(63, 0, 63));
    }

    private void InitializeBlocker()
    {
        Vector3d min = _pointyGrid.GetWorldPosition(new VoxelIndex(4, 0, 4));
        Vector3d max = _pointyGrid.GetWorldPosition(new VoxelIndex(14, 0, 14));
        _hexBlocker = new BoundsBlocker(_world, new FixedBoundArea(min, max), cacheCoveredVoxels: true);
    }

    private void InitializeOccupants()
    {
        _registrationOccupants = CreateOccupants(_pointyGrid, startQ: 20, startR: 20, width: 16, length: 16);
        _scanOccupants = CreateOccupants(_pointyGrid, startQ: 40, startR: 40, width: 16, length: 16);
        _scanResults = new SwiftList<IVoxelOccupant>();
        _scanScratch = new GridScanScratch();

        for (int i = 0; i < _scanOccupants.Length; i++)
        {
            if (!GridOccupantManager.TryRegister(_world, _scanOccupants[i]))
                throw new InvalidOperationException($"Unable to register hex scan occupant {i}.");
        }

        _scanResults.Clear();
        _scanScratch.Clear();
    }

    private static GridConfiguration CreateHexConfiguration(
        Vector3d boundsMin,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex)
    {
        Vector3d boundsMax = boundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            boundsMin,
            boundsMax,
            scanCellSize: 8,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);
    }

    private static BenchmarkOccupant[] CreateOccupants(
        VoxelGrid grid,
        int startQ,
        int startR,
        int width,
        int length)
    {
        BenchmarkOccupant[] occupants = new BenchmarkOccupant[width * length];
        int index = 0;

        for (int q = startQ; q < startQ + width; q++)
        {
            for (int r = startR; r < startR + length; r++)
            {
                occupants[index] = new BenchmarkOccupant(
                    grid.GetWorldPosition(new VoxelIndex(q, 0, r)),
                    (byte)(index & 7));
                index++;
            }
        }

        return occupants;
    }

    private static VoxelIndex[] CreateLookupIndices()
    {
        VoxelIndex[] indices = new VoxelIndex[LookupCount];

        for (int i = 0; i < indices.Length; i++)
        {
            int q = (i * 37 + 11) & GridExtent;
            int r = (i * 53 + 7) & GridExtent;
            indices[i] = new VoxelIndex(q, 0, r);
        }

        return indices;
    }
}
