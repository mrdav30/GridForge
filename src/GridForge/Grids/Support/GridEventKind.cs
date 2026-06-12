//=======================================================================
// GridEventKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids;

/// <summary>
/// Describes the reason a grid event was raised.
/// </summary>
public enum GridEventKind
{
    /// <summary>
    /// The event source did not provide a more specific reason.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// A grid was added to a world.
    /// </summary>
    GridAdded = 1,

    /// <summary>
    /// A grid was removed from a world.
    /// </summary>
    GridRemoved = 2,

    /// <summary>
    /// A grid changed without a more specific classified mutation.
    /// </summary>
    GridChanged = 3,

    /// <summary>
    /// A sparse voxel was configured at runtime.
    /// </summary>
    SparseVoxelAdded = 4,

    /// <summary>
    /// A sparse voxel was removed at runtime.
    /// </summary>
    SparseVoxelRemoved = 5,
}
