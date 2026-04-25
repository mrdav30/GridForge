using FixedMathSharp;

namespace GridForge.Blockers;

/// <summary>
/// Immutable snapshot describing a blocker application or removal.
/// </summary>
public readonly struct BlockageEventInfo
{
    /// <summary>
    /// The world instance that produced this blocker event.
    /// </summary>
    public readonly int WorldSpawnToken;

    /// <summary>
    /// The unique token representing the blocker coverage.
    /// </summary>
    public readonly BoundsKey BlockageToken;

    /// <summary>
    /// The minimum bounds of the blocker coverage.
    /// </summary>
    public readonly Vector3d BoundsMin;

    /// <summary>
    /// The maximum bounds of the blocker coverage.
    /// </summary>
    public readonly Vector3d BoundsMax;

    /// <summary>
    /// Initializes a new immutable blockage snapshot.
    /// </summary>
    public BlockageEventInfo(
        int worldSpawnToken,
        BoundsKey blockageToken,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        WorldSpawnToken = worldSpawnToken;
        BlockageToken = blockageToken;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }
}
