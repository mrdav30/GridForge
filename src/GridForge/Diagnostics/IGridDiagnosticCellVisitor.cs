//=======================================================================
// IGridDiagnosticCellVisitor.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Diagnostics;

/// <summary>
/// Visitor contract for diagnostic queries that stream cells directly into
/// caller-owned buffers.
/// </summary>
public interface IGridDiagnosticCellVisitor
{
    /// <summary>
    /// Handles one diagnostic cell.
    /// </summary>
    /// <param name="cell">The current diagnostic cell descriptor.</param>
    /// <returns>True to continue traversal; false to stop.</returns>
    bool Visit(in GridDiagnosticCell cell);
}
