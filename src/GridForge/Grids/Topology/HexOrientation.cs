namespace GridForge.Grids.Topology;

/// <summary>
/// Describes how a hex-prism grid projects axial coordinates into world XZ space.
/// </summary>
public enum HexOrientation
{
    /// <summary>
    /// Hexes have flat horizontal tops in the projected XZ footprint.
    /// </summary>
    FlatTop = 0,

    /// <summary>
    /// Hexes have pointed horizontal tops in the projected XZ footprint.
    /// </summary>
    PointyTop = 1
}
