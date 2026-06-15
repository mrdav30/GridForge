//=======================================================================
// GridDiagnosticQueryResult.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Diagnostics;

/// <summary>
/// Summary information returned from a diagnostic query.
/// </summary>
public readonly struct GridDiagnosticQueryResult
{
    /// <summary>
    /// Status of the completed query pass.
    /// </summary>
    public readonly GridDiagnosticQueryStatus Status;

    /// <summary>
    /// Number of cells written or visited.
    /// </summary>
    public readonly int CellCount;

    /// <summary>
    /// Number of cells skipped because the query stopped before traversal
    /// completed.
    /// </summary>
    public readonly int SkippedCellCount;

    /// <summary>
    /// Initializes a query result.
    /// </summary>
    public GridDiagnosticQueryResult(
        GridDiagnosticQueryStatus status,
        int cellCount,
        int skippedCellCount)
    {
        Status = status;
        CellCount = cellCount;
        SkippedCellCount = skippedCellCount;
    }
}
