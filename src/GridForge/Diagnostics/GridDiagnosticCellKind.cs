//=======================================================================
// GridDiagnosticCellKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Diagnostics;

/// <summary>
/// Identifies whether a diagnostic cell describes a physical voxel or a sparse
/// address-space location where a voxel could exist.
/// </summary>
public enum GridDiagnosticCellKind
{
    /// <summary>
    /// The descriptor represents a configured physical voxel.
    /// </summary>
    Physical = 0,

    /// <summary>
    /// The descriptor represents an unconfigured sparse address-space cell.
    /// </summary>
    MissingSparseAddress = 1
}
