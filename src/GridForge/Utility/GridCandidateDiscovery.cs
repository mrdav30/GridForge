//=======================================================================
// GridCandidateDiscovery.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids;
using SwiftCollections;
using SwiftCollections.Utility;
using System.Runtime.CompilerServices;

namespace GridForge.Utility;

/// <summary>
/// Resolves spatial-hash candidates without making query volume proportional
/// to an otherwise sparse world.
/// </summary>
internal static class GridCandidateDiscovery
{
    internal static void CollectInStableSlotOrder(
        GridWorld world,
        int cellXMin,
        int cellYMin,
        int cellZMin,
        int cellXMax,
        int cellYMax,
        int cellZMax,
        SwiftHashSet<ushort> processedGrids,
        SwiftList<ushort> candidateGrids)
    {
        processedGrids.Clear();
        candidateGrids.Clear();

        if (ShouldScanActiveGrids(
                world,
                cellXMin,
                cellYMin,
                cellZMin,
                cellXMax,
                cellYMax,
                cellZMax))
        {
            foreach (VoxelGrid grid in world.ActiveGrids)
                candidateGrids.Add(grid.GridIndex);

            return;
        }

        for (long cellZ = cellZMin; cellZ <= cellZMax; cellZ++)
        {
            for (long cellY = cellYMin; cellY <= cellYMax; cellY++)
            {
                for (long cellX = cellXMin; cellX <= cellXMax; cellX++)
                {
                    int cellIndex = SwiftHashTools.CombineHashCodes(
                        (int)cellX,
                        (int)cellY,
                        (int)cellZ);
                    if (!world.SpatialGridHash.TryGetValue(
                            cellIndex,
                            out SwiftHashSet<ushort> gridList))
                    {
                        continue;
                    }

                    foreach (ushort gridIndex in gridList)
                    {
                        if (world.ActiveGrids.IsAllocated(gridIndex)
                            && processedGrids.Add(gridIndex))
                        {
                            candidateGrids.Add(gridIndex);
                        }
                    }
                }
            }
        }

        candidateGrids.SortInPlace();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldScanActiveGrids(
        GridWorld world,
        int cellXMin,
        int cellYMin,
        int cellZMin,
        int cellXMax,
        int cellYMax,
        int cellZMax)
    {
        ulong activeGridCount = (ulong)world.ActiveGrids.Count;
        if (activeGridCount == 0UL)
            return true;

        ulong xCount = (ulong)((long)cellXMax - cellXMin + 1L);
        if (xCount > activeGridCount)
            return true;

        ulong yCount = (ulong)((long)cellYMax - cellYMin + 1L);
        if (yCount > activeGridCount / xCount)
            return true;

        ulong xyCount = xCount * yCount;
        ulong zCount = (ulong)((long)cellZMax - cellZMin + 1L);
        return zCount > activeGridCount / xyCount;
    }
}
