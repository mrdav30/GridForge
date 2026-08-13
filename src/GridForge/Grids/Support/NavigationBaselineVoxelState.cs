//=======================================================================
// NavigationBaselineVoxelState.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Immutable post-mutation state for one requested topology-local address.
/// </summary>
public readonly struct NavigationBaselineVoxelState
{
    /// <summary>
    /// The requested topology-local address.
    /// </summary>
    public readonly VoxelIndex VoxelIndex;

    /// <summary>
    /// Whether a physical voxel is present at the requested address.
    /// </summary>
    public readonly bool IsPresent;

    /// <summary>
    /// The physical voxel obstacle count, or zero when no voxel is present.
    /// </summary>
    public readonly byte ObstacleCount;

    /// <summary>
    /// Initializes a navigation baseline address state.
    /// </summary>
    public NavigationBaselineVoxelState(VoxelIndex voxelIndex, bool isPresent, byte obstacleCount)
    {
        VoxelIndex = voxelIndex;
        IsPresent = isPresent;
        ObstacleCount = obstacleCount;
    }
}
