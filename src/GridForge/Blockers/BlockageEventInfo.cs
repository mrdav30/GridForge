using FixedMathSharp;
using GridForge.Configuration;

namespace GridForge.Blockers;

/// <summary>
/// Immutable snapshot describing a blocker application or removal.
/// </summary>
public readonly struct BlockageEventInfo
{
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
        BoundsKey blockageToken,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        BlockageToken = blockageToken;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }
}
