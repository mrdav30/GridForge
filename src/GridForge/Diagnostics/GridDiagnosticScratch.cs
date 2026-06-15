//=======================================================================
// GridDiagnosticScratch.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Diagnostics;

/// <summary>
/// Reusable scratch storage for allocation-conscious diagnostic queries.
/// </summary>
public sealed class GridDiagnosticScratch
{
    /// <summary>
    /// Clears reusable scratch state. Phase 1 does not allocate scratch
    /// collections yet.
    /// </summary>
    public void Clear()
    {
    }
}
