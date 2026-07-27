//=======================================================================
// GridTracer.GridCoverage.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System.Runtime.CompilerServices;

namespace GridForge.Utility;

/// <content>
/// Grid coverage utilities for resolving scan cells and voxels that intersect
/// a given world-space query bounds, including specialized handling for hex-prism topologies.
/// </content>
public static partial class GridTracer
{
    private static void AddCoveredScanCellsForGrid(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ScanCell> voxelRedundancyCheck)
    {
        if (currentGrid.Topology.Kind == GridTopologyKind.HexPrism)
        {
            AddCoveredHexScanCellsForGrid(
                currentGrid,
                queryMin,
                queryMax,
                scanCells,
                voxelRedundancyCheck);
            return;
        }

        if (!TryGetCoveredScanCellRange(
                currentGrid,
                queryMin,
                queryMax,
                out int xMin,
                out int yMin,
                out int zMin,
                out int xMax,
                out int yMax,
                out int zMax))
        {
            return;
        }

        currentGrid.AddScanCellsInRange(
            xMin,
            yMin,
            zMin,
            xMax,
            yMax,
            zMax,
            scanCells,
            voxelRedundancyCheck);
    }

    private static void AddCoveredVoxelsForGrid(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        SwiftList<Voxel> voxelList = SwiftListPool<Voxel>.Shared.Rent();
        AddCoveredGridVoxels(
            currentGrid,
            queryMin,
            queryMax,
            voxelList,
            voxelRedundancyCheck);

        if (voxelList.Count > 0)
            gridVoxelMapping.Add(currentGrid, voxelList);
        else
            SwiftListPool<Voxel>.Shared.Release(voxelList);
    }

    private static void AddCoveredGridVoxels(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (currentGrid.Topology.Kind == GridTopologyKind.HexPrism)
        {
            AddCoveredHexGridVoxels(
                currentGrid,
                queryMin,
                queryMax,
                voxelList);
            return;
        }

        if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
                currentGrid,
                queryMin,
                queryMax,
                out VoxelIndex minIndex,
                out VoxelIndex maxIndex))
        {
            return;
        }

        currentGrid.AddVoxelsInIndexRange(
            minIndex,
            maxIndex,
            voxelList,
            voxelRedundancyCheck);
    }

    private static void AddCoveredHexGridVoxels(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftList<Voxel> voxelList)
    {
        if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
                currentGrid,
                queryMin,
                queryMax,
                out VoxelIndex minIndex,
                out VoxelIndex maxIndex))
        {
            return;
        }

        Fixed64 horizontalExpansion =
            currentGrid.Topology.Metrics.CellRadius;
        Fixed64 coverageMinX = queryMin.X - horizontalExpansion;
        Fixed64 coverageMaxX = queryMax.X + horizontalExpansion;
        Fixed64 coverageMinZ = queryMin.Z - horizontalExpansion;
        Fixed64 coverageMaxZ = queryMax.Z + horizontalExpansion;

        for (long x = minIndex.x; x <= maxIndex.x; x++)
        {
            for (long y = minIndex.y; y <= maxIndex.y; y++)
            {
                for (long z = minIndex.z; z <= maxIndex.z; z++)
                {
                    if (currentGrid.TryGetVoxel(
                            (int)x,
                            (int)y,
                            (int)z,
                            out Voxel? voxel)
                        && IsHexVoxelCenterInHorizontalCoverage(
                            voxel!,
                            coverageMinX,
                            coverageMaxX,
                            coverageMinZ,
                            coverageMaxZ))
                    {
                        voxelList.Add(voxel!);
                    }
                }
            }
        }
    }

    private static void AddCoveredHexScanCellsForGrid(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ScanCell> scanCellRedundancyCheck)
    {
        if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
                currentGrid,
                queryMin,
                queryMax,
                out VoxelIndex minIndex,
                out VoxelIndex maxIndex))
        {
            return;
        }

        currentGrid.AddScanCellsInRange(
            minIndex.x / currentGrid.ScanCellSize,
            minIndex.y / currentGrid.ScanCellSize,
            minIndex.z / currentGrid.ScanCellSize,
            maxIndex.x / currentGrid.ScanCellSize,
            maxIndex.y / currentGrid.ScanCellSize,
            maxIndex.z / currentGrid.ScanCellSize,
            scanCells,
            scanCellRedundancyCheck);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsHexVoxelCenterInHorizontalCoverage(
        Voxel voxel,
        Fixed64 coverageMinX,
        Fixed64 coverageMaxX,
        Fixed64 coverageMinZ,
        Fixed64 coverageMaxZ)
    {
        Vector3d position = voxel.WorldPosition;
        return position.X >= coverageMinX
            && position.X <= coverageMaxX
            && position.Z >= coverageMinZ
            && position.Z <= coverageMaxZ;
    }
}
