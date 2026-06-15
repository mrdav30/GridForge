//=======================================================================
// GridDiagnosticAddressMode.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Diagnostics;

/// <summary>
/// Selects which parts of a grid's physical or sparse address space should be
/// exposed through diagnostic queries.
/// </summary>
public enum GridDiagnosticAddressMode
{
    /// <summary>
    /// Return only configured physical voxels.
    /// </summary>
    PhysicalOnly = 0,

    /// <summary>
    /// Return configured physical voxels and missing sparse address-space cells.
    /// Dense grids return physical cells only.
    /// </summary>
    PhysicalAndMissing = 1,

    /// <summary>
    /// Return only missing sparse address-space cells.
    /// Dense grids return no cells.
    /// </summary>
    MissingOnly = 2
}
