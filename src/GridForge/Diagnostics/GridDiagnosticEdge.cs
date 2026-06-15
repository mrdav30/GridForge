//=======================================================================
// GridDiagnosticEdge.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Diagnostics;

/// <summary>
/// Identifies a diagnostic prism edge by vertex indices.
/// </summary>
public readonly struct GridDiagnosticEdge
{
    /// <summary>
    /// Start vertex index.
    /// </summary>
    public readonly byte Start;

    /// <summary>
    /// End vertex index.
    /// </summary>
    public readonly byte End;

    /// <summary>
    /// Initializes a diagnostic edge descriptor.
    /// </summary>
    public GridDiagnosticEdge(byte start, byte end)
    {
        Start = start;
        End = end;
    }
}
