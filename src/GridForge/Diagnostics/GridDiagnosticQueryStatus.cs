//=======================================================================
// GridDiagnosticQueryStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Diagnostics;

/// <summary>
/// Describes the result of a diagnostic query pass.
/// </summary>
public enum GridDiagnosticQueryStatus
{
    /// <summary>
    /// The query completed without truncation or validation failure.
    /// </summary>
    Completed = 0,

    /// <summary>
    /// The supplied world was null or inactive.
    /// </summary>
    InactiveWorld = 1,

    /// <summary>
    /// The query requested a grid that could not be resolved.
    /// </summary>
    InvalidGrid = 2,

    /// <summary>
    /// The query requested missing sparse address cells without bounds or an
    /// explicit full-address-space scan opt-in.
    /// </summary>
    MissingAddressSpaceRequiresBounds = 3,

    /// <summary>
    /// The query reached its maximum cell budget before traversal completed.
    /// </summary>
    MaxCellsExceeded = 4,

    /// <summary>
    /// Traversal stopped because the visitor requested early termination.
    /// </summary>
    Truncated = 5
}
