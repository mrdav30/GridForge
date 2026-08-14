//=======================================================================
// GridCoveredAddress.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;
using GridForge.Spatial;

namespace GridForge.Grids.Topology;

/// <summary>
/// Identifies one topology-local address covered by a query and its exact grid generation.
/// </summary>
public readonly struct GridCoveredAddress
{
    /// <summary>The durable normalized grid identity.</summary>
    public GridConfigurationKey ConfigurationKey { get; }

    /// <summary>The recyclable world-local grid slot.</summary>
    public ushort GridIndex { get; }

    /// <summary>The exact process-unique grid allocation identity.</summary>
    public long GridSpawnToken { get; }

    /// <summary>The grid-local high-water sequence captured for this result.</summary>
    public ulong GridHighWaterSequence { get; }

    /// <summary>The topology-local address, whether or not a physical voxel currently exists.</summary>
    public VoxelIndex VoxelIndex { get; }

    internal GridCoveredAddress(GridCoveredAddressGeneration generation, VoxelIndex voxelIndex)
    {
        ConfigurationKey = generation.ConfigurationKey;
        GridIndex = generation.GridIndex;
        GridSpawnToken = generation.GridSpawnToken;
        GridHighWaterSequence = generation.GridHighWaterSequence;
        VoxelIndex = voxelIndex;
    }
}
