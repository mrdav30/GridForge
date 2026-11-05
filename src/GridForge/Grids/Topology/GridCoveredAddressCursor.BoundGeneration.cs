//=======================================================================
// GridCoveredAddressCursor.BoundGeneration.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace GridForge.Grids.Topology;

public sealed partial class GridCoveredAddressCursor
{
    internal readonly struct BoundGeneration
    {
        internal BoundGeneration(
            GridCoveredAddressGeneration generation,
            VoxelIndex minimum,
            VoxelIndex maximum,
            bool hasRange)
        {
            Generation = generation;
            Minimum = minimum;
            Maximum = maximum;
            HasRange = hasRange;
        }

        internal GridCoveredAddressGeneration Generation { get; }
        internal VoxelIndex Minimum { get; }
        internal VoxelIndex Maximum { get; }
        internal bool HasRange { get; }
    }
}
