//=======================================================================
// GridTracer.PlanarNavigationBody.Range.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace GridForge.Utility;

/// <content>Derives conservative native planar address ranges from the actual forward lattice.</content>
public static partial class GridTracer
{
    private static bool TryGetPlanarCandidateRange(VoxelGrid grid, Vector2d minimum, Vector2d maximum,
        out VoxelIndex minIndex, out VoxelIndex maxIndex)
    {
        // The ideal hex inverse is not the inverse of its quantized forward basis.
        // Use four monotone coordinate bounds instead: each takes at most 31
        // probes over an Int32 dimension, with no storage or physical-cell reads.
        int minX;
        int maxX;
        int minZ;
        int maxZ;
        if (grid.Configuration.TopologyKind == GridTopologyKind.HexPrism
            && grid.Configuration.TopologyMetrics.HexOrientation == HexOrientation.PointyTop)
        {
            minZ = FindPlanarCoordinateBound(grid, column: false, fixedIndex: 0, minimum.Y, upper: false);
            maxZ = FindPlanarCoordinateBound(grid, column: false, fixedIndex: 0, maximum.Y, upper: true) - 1;
            if (minZ > maxZ)
            {
                minIndex = default;
                maxIndex = default;
                return false;
            }
            // X increases with both q and r; the retained row extrema conservatively bound q.
            minX = FindPlanarCoordinateBound(grid, column: true, fixedIndex: maxZ, minimum.X, upper: false);
            maxX = FindPlanarCoordinateBound(grid, column: true, fixedIndex: minZ, maximum.X, upper: true) - 1;
        }
        else
        {
            // Rectangular X is independent of z; flat hex X is independent of r.
            minX = FindPlanarCoordinateBound(grid, column: true, fixedIndex: 0, minimum.X, upper: false);
            maxX = FindPlanarCoordinateBound(grid, column: true, fixedIndex: 0, maximum.X, upper: true) - 1;
            if (minX > maxX)
            {
                minIndex = default;
                maxIndex = default;
                return false;
            }
            // Rectangular Z ignores fixedIndex; flat hex Z increases with both indices.
            minZ = FindPlanarCoordinateBound(grid, column: false, fixedIndex: maxX, minimum.Y, upper: false);
            maxZ = FindPlanarCoordinateBound(grid, column: false, fixedIndex: minX, maximum.Y, upper: true) - 1;
        }
        minIndex = new VoxelIndex(minX, 0, minZ);
        maxIndex = new VoxelIndex(maxX, 0, maxZ);
        return minX <= maxX && minZ <= maxZ;
    }

    private static int FindPlanarCoordinateBound(VoxelGrid grid, bool column, int fixedIndex,
        Fixed64 bound, bool upper)
    {
        int low = 0;
        int high = column ? grid.Width : grid.Length;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            VoxelIndex index = column ? new VoxelIndex(middle, 0, fixedIndex) : new VoxelIndex(fixedIndex, 0, middle);
            Vector3d center = grid.GetWorldPosition(index);
            Fixed64 coordinate = column ? center.X : center.Z;
            if (coordinate < bound || (upper && coordinate == bound))
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }
}
