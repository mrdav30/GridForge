//=======================================================================
// BlockageEventInfo.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
    public readonly long WorldSpawnToken;

    /// <summary>
    /// The unique token representing the blocker coverage.
    /// </summary>
    public readonly ObstacleToken BlockageToken;

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
    /// <param name="worldSpawnToken">The world instance that produced this blocker event.</param>
    /// <param name="blockageToken">The unique token representing the blocker coverage.</param>
    /// <param name="boundsMin">The minimum bounds of the blocker coverage.</param>
    /// <param name="boundsMax">The maximum bounds of the blocker coverage.</param>
    public BlockageEventInfo(
        long worldSpawnToken,
        ObstacleToken blockageToken,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        WorldSpawnToken = worldSpawnToken;
        BlockageToken = blockageToken;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }
}
