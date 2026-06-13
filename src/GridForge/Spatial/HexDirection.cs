//=======================================================================
// HexDirection.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Spatial;

/// <summary>
/// Represents the 20 possible neighbor directions in a hex-prism grid.
/// </summary>
public enum HexDirection
{
    /// <summary>
    /// No hex direction from source.
    /// </summary>
    None = -1,

    /// <summary>
    /// Axial offset (+1, 0).
    /// </summary>
    East = 0,

    /// <summary>
    /// Axial offset (+1, -1).
    /// </summary>
    NorthEast = 1,

    /// <summary>
    /// Axial offset (0, -1).
    /// </summary>
    NorthWest = 2,

    /// <summary>
    /// Axial offset (-1, 0).
    /// </summary>
    West = 3,

    /// <summary>
    /// Axial offset (-1, +1).
    /// </summary>
    SouthWest = 4,

    /// <summary>
    /// Axial offset (0, +1).
    /// </summary>
    SouthEast = 5,

    /// <summary>
    /// Vertical offset (0, -1, 0).
    /// </summary>
    Below = 6,

    /// <summary>
    /// Vertical offset (+1, -1, 0).
    /// </summary>
    BelowEast = 7,

    /// <summary>
    /// Vertical offset (+1, -1, -1).
    /// </summary>
    BelowNorthEast = 8,

    /// <summary>
    /// Vertical offset (0, -1, -1).
    /// </summary>
    BelowNorthWest = 9,

    /// <summary>
    /// Vertical offset (-1, -1, 0).
    /// </summary>
    BelowWest = 10,

    /// <summary>
    /// Vertical offset (-1, -1, +1).
    /// </summary>
    BelowSouthWest = 11,

    /// <summary>
    /// Vertical offset (0, -1, +1).
    /// </summary>
    BelowSouthEast = 12,

    /// <summary>
    /// Vertical offset (0, +1, 0).
    /// </summary>
    Above = 13,

    /// <summary>
    /// Vertical offset (+1, +1, 0).
    /// </summary>
    AboveEast = 14,

    /// <summary>
    /// Vertical offset (+1, +1, -1).
    /// </summary>
    AboveNorthEast = 15,

    /// <summary>
    /// Vertical offset (0, +1, -1).
    /// </summary>
    AboveNorthWest = 16,

    /// <summary>
    /// Vertical offset (-1, +1, 0).
    /// </summary>
    AboveWest = 17,

    /// <summary>
    /// Vertical offset (-1, +1, +1).
    /// </summary>
    AboveSouthWest = 18,

    /// <summary>
    /// Vertical offset (0, +1, +1).
    /// </summary>
    AboveSouthEast = 19
}
