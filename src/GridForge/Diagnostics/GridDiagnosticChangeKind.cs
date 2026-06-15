//=======================================================================
// GridDiagnosticChangeKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace GridForge.Diagnostics;

/// <summary>
/// Describes the kind of runtime change captured by a diagnostic session.
/// </summary>
[Flags]
public enum GridDiagnosticChangeKind
{
    /// <summary>
    /// No diagnostic change kind is set.
    /// </summary>
    None = 0,

    /// <summary>
    /// A grid was added to the observed world.
    /// </summary>
    GridAdded = 1,

    /// <summary>
    /// A grid was removed from the observed world.
    /// </summary>
    GridRemoved = 2,

    /// <summary>
    /// A grid raised a broad change notification.
    /// </summary>
    GridChanged = 4,

    /// <summary>
    /// The observed world was reset.
    /// </summary>
    WorldReset = 8,

    /// <summary>
    /// A sparse physical voxel was configured.
    /// </summary>
    SparseVoxelAdded = 16,

    /// <summary>
    /// A sparse physical voxel was removed.
    /// </summary>
    SparseVoxelRemoved = 32,

    /// <summary>
    /// A sparse address-space range changed.
    /// </summary>
    SparseAddressChanged = 64,

    /// <summary>
    /// Obstacle state changed on a physical voxel.
    /// </summary>
    ObstacleChanged = 128,

    /// <summary>
    /// Occupant state changed on a physical voxel.
    /// </summary>
    OccupantChanged = 256
}
