//=======================================================================
// GridNavigationBaseline.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Configuration;

namespace GridForge.Grids;

/// <summary>
/// Atomic requested-address snapshot for initializing an external navigation overlay.
/// </summary>
public sealed class GridNavigationBaseline
{
    private readonly NavigationBaselineVoxelState[] _voxelStates;

    /// <summary>
    /// The world-local high-water change sequence captured with the state.
    /// </summary>
    public ulong HighWaterSequence { get; }

    /// <summary>
    /// The exact process-unique identity of the source world.
    /// </summary>
    public long WorldSpawnToken { get; }

    /// <summary>
    /// The exact world-local generation of the active grid.
    /// </summary>
    public long GridSpawnToken { get; }

    /// <summary>
    /// The last world-local change sequence applied to this exact grid generation.
    /// Unrelated grids do not advance this value.
    /// </summary>
    public ulong GridHighWaterSequence { get; }

    /// <summary>
    /// The active grid's recyclable world-local slot.
    /// </summary>
    public ushort GridIndex { get; }

    /// <summary>
    /// The requested normalized configuration identity.
    /// </summary>
    public GridConfigurationKey ConfigurationKey { get; }

    /// <summary>
    /// Requested address states in the caller's validated ascending order.
    /// </summary>
    public ReadOnlySpan<NavigationBaselineVoxelState> VoxelStates => _voxelStates;

    internal GridNavigationBaseline(
        ulong highWaterSequence,
        long worldSpawnToken,
        long gridSpawnToken,
        ulong gridHighWaterSequence,
        ushort gridIndex,
        GridConfigurationKey configurationKey,
        NavigationBaselineVoxelState[] voxelStates)
    {
        HighWaterSequence = highWaterSequence;
        WorldSpawnToken = worldSpawnToken;
        GridSpawnToken = gridSpawnToken;
        GridHighWaterSequence = gridHighWaterSequence;
        GridIndex = gridIndex;
        ConfigurationKey = configurationKey;
        _voxelStates = voxelStates;
    }
}
