//=======================================================================
// GridTraversalPaddingMode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Utility;

/// <summary>
/// Selects which topology edge measurement a grid traversal should use for padding.
/// </summary>
public enum GridTraversalPaddingMode
{
    /// <summary>
    /// Use the largest three-dimensional cell edge.
    /// </summary>
    MaxCellEdge,

    /// <summary>
    /// Use the largest X/Z-plane cell edge.
    /// </summary>
    PlanarMaxCellEdge
}
