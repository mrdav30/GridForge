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
    /// <value>The durable normalized grid identity.</value>
    public GridConfigurationKey ConfigurationKey { get; }

    /// <summary>The recyclable world-local grid slot.</summary>
    /// <value>The recyclable world-local grid slot.</value>
    public ushort GridIndex { get; }

    /// <summary>The exact process-unique grid allocation identity.</summary>
    /// <value>The exact process-unique grid allocation identity.</value>
    public long GridSpawnToken { get; }

    /// <summary>The grid's last committed change sequence captured for this result.</summary>
    /// <value>The grid's last committed change sequence captured for this result.</value>
    public ulong GridLastChangeSequence { get; }

    /// <summary>The topology-local address, whether or not a physical voxel currently exists.</summary>
    /// <value>The topology-local address, whether or not a physical voxel currently exists.</value>
    public VoxelIndex VoxelIndex { get; }

    internal GridCoveredAddress(GridCoveredAddressGeneration generation, VoxelIndex voxelIndex)
    {
        ConfigurationKey = generation.ConfigurationKey;
        GridIndex = generation.GridIndex;
        GridSpawnToken = generation.GridSpawnToken;
        GridLastChangeSequence = generation.GridLastChangeSequence;
        VoxelIndex = voxelIndex;
    }
}
