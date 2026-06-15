//=======================================================================
// GridDiagnosticCellState.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace GridForge.Diagnostics;

/// <summary>
/// Compact state flags exposed for diagnostic cell filtering and adapters.
/// </summary>
[Flags]
public enum GridDiagnosticCellState : byte
{
    /// <summary>
    /// No diagnostic state flags are set.
    /// </summary>
    None = 0,

    /// <summary>
    /// The physical voxel has no occupants and no blockers.
    /// </summary>
    Empty = 1,

    /// <summary>
    /// The physical voxel has one or more occupants.
    /// </summary>
    Occupied = 2,

    /// <summary>
    /// The physical voxel has one or more blockers.
    /// </summary>
    Blocked = 4,

    /// <summary>
    /// The physical voxel lies on the boundary of its grid.
    /// </summary>
    Boundary = 8,

    /// <summary>
    /// The physical voxel has one or more partitions.
    /// </summary>
    Partitioned = 16,

    /// <summary>
    /// The descriptor represents a missing sparse address-space cell.
    /// </summary>
    MissingSparseAddress = 32
}
