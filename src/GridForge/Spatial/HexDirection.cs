//=======================================================================
// HexDirection.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Spatial;

/// <summary>
/// Represents the 20 possible neighbor directions in a hex-prism grid.
/// Offsets are expressed as axial Q, vertical layer, and axial R.
/// </summary>
public enum HexDirection
{
    /// <summary>
    /// No hex direction from source.
    /// </summary>
    None = -1,

    /// <summary>
    /// Axial offset (Q + 1, R unchanged).
    /// </summary>
    QPositive = 0,

    /// <summary>
    /// Axial offset (Q + 1, R - 1).
    /// </summary>
    QPositiveRNegative = 1,

    /// <summary>
    /// Axial offset (Q unchanged, R - 1).
    /// </summary>
    RNegative = 2,

    /// <summary>
    /// Axial offset (Q - 1, R unchanged).
    /// </summary>
    QNegative = 3,

    /// <summary>
    /// Axial offset (Q - 1, R + 1).
    /// </summary>
    QNegativeRPositive = 4,

    /// <summary>
    /// Axial offset (Q unchanged, R + 1).
    /// </summary>
    RPositive = 5,

    /// <summary>
    /// Vertical offset (0, -1, 0).
    /// </summary>
    Below = 6,

    /// <summary>
    /// Vertical offset (Q + 1, layer - 1, R unchanged).
    /// </summary>
    BelowQPositive = 7,

    /// <summary>
    /// Vertical offset (Q + 1, layer - 1, R - 1).
    /// </summary>
    BelowQPositiveRNegative = 8,

    /// <summary>
    /// Vertical offset (Q unchanged, layer - 1, R - 1).
    /// </summary>
    BelowRNegative = 9,

    /// <summary>
    /// Vertical offset (Q - 1, layer - 1, R unchanged).
    /// </summary>
    BelowQNegative = 10,

    /// <summary>
    /// Vertical offset (Q - 1, layer - 1, R + 1).
    /// </summary>
    BelowQNegativeRPositive = 11,

    /// <summary>
    /// Vertical offset (Q unchanged, layer - 1, R + 1).
    /// </summary>
    BelowRPositive = 12,

    /// <summary>
    /// Vertical offset (0, +1, 0).
    /// </summary>
    Above = 13,

    /// <summary>
    /// Vertical offset (Q + 1, layer + 1, R unchanged).
    /// </summary>
    AboveQPositive = 14,

    /// <summary>
    /// Vertical offset (Q + 1, layer + 1, R - 1).
    /// </summary>
    AboveQPositiveRNegative = 15,

    /// <summary>
    /// Vertical offset (Q unchanged, layer + 1, R - 1).
    /// </summary>
    AboveRNegative = 16,

    /// <summary>
    /// Vertical offset (Q - 1, layer + 1, R unchanged).
    /// </summary>
    AboveQNegative = 17,

    /// <summary>
    /// Vertical offset (Q - 1, layer + 1, R + 1).
    /// </summary>
    AboveQNegativeRPositive = 18,

    /// <summary>
    /// Vertical offset (Q unchanged, layer + 1, R + 1).
    /// </summary>
    AboveRPositive = 19
}
