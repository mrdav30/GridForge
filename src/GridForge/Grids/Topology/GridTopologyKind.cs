namespace GridForge.Grids.Topology;

/// <summary>
/// Identifies the cell topology used by a grid.
/// </summary>
public enum GridTopologyKind
{
    /// <summary>
    /// Rectangular-prism cells addressed by local X, Y, and Z coordinates.
    /// </summary>
    RectangularPrism = 0,

    /// <summary>
    /// Hexagonal-prism cells addressed by axial X/Z coordinates and vertical Y layers.
    /// </summary>
    HexPrism = 1
}
