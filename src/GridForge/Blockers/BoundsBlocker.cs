//=======================================================================
// BoundsBlocker.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Grids;

namespace GridForge.Blockers;

/// <summary>
/// A manually placed blocker that obstructs a defined world-space bounding box.
/// </summary>
public class BoundsBlocker : Blocker
{
    private readonly FixedBoundBox _blockBounds;

    /// <summary>
    /// Initializes a new bounds blocker bound to the supplied world.
    /// </summary>
    /// <param name="world">The world whose grids this blocker should affect.</param>
    /// <param name="blockBounds">The bounding box to block.</param>
    /// <param name="isActive">Flag whether or not blocker is active.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels.</param>
    public BoundsBlocker(
        GridWorld world,
        FixedBoundBox blockBounds,
        bool isActive = true,
        bool cacheCoveredVoxels = false) : base(world, isActive, cacheCoveredVoxels)
    {
        _blockBounds = blockBounds;
    }

    /// <summary>
    /// Initializes a new bounds blocker from world-space min/max bounds.
    /// </summary>
    /// <param name="world">The world whose grids this blocker should affect.</param>
    /// <param name="boundsMin">The world-space minimum bound.</param>
    /// <param name="boundsMax">The world-space maximum bound.</param>
    /// <param name="isActive">Flag whether or not blocker is active.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels.</param>
    public BoundsBlocker(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        bool isActive = true,
        bool cacheCoveredVoxels = false)
        : this(
            world,
            FixedBoundBox.FromMinMax(boundsMin, boundsMax),
            isActive,
            cacheCoveredVoxels)
    { }

    /// <inheritdoc cref="Blocker.GetBoundsMin"/>
    protected override Vector3d GetBoundsMin() => _blockBounds.Min;

    /// <inheritdoc cref="Blocker.GetBoundsMax"/>
    protected override Vector3d GetBoundsMax() => _blockBounds.Max;
}
