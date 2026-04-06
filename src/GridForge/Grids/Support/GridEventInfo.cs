using FixedMathSharp;
using GridForge.Configuration;

namespace GridForge.Grids;

/// <summary>
/// Immutable snapshot describing a grid at the time a global grid notification is raised.
/// </summary>
public readonly struct GridEventInfo
{
    /// <summary>
    /// The stable slot index assigned to the grid within <see cref="GlobalGridManager.ActiveGrids"/>.
    /// </summary>
    public readonly ushort GridIndex;

    /// <summary>
    /// The unique spawn token for the specific grid instance occupying <see cref="GridIndex"/>.
    /// </summary>
    public readonly int GridSpawnToken;

    /// <summary>
    /// The snapped configuration for the grid when the notification was raised.
    /// </summary>
    public readonly GridConfiguration Configuration;

    /// <summary>
    /// The per-grid version recorded when the notification was raised.
    /// </summary>
    public readonly uint GridVersion;

    /// <summary>
    /// The minimum snapped bounds of the grid.
    /// </summary>
    public readonly Vector3d BoundsMin => Configuration.BoundsMin;

    /// <summary>
    /// The maximum snapped bounds of the grid.
    /// </summary>
    public readonly Vector3d BoundsMax => Configuration.BoundsMax;

    /// <summary>
    /// Initializes a new immutable grid event snapshot.
    /// </summary>
    public GridEventInfo(
        ushort gridIndex,
        int gridSpawnToken,
        GridConfiguration configuration,
        uint gridVersion)
    {
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        Configuration = configuration;
        GridVersion = gridVersion;
    }

    /// <summary>
    /// Creates an exact bounds key from the stored grid configuration.
    /// </summary>
    public readonly BoundsKey ToBoundsKey() => Configuration.ToBoundsKey();
}
