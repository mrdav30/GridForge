using FixedMathSharp;
using GridForge.Grids;

namespace GridForge.Blockers;

/// <summary>
/// A manually placed blocker that obstructs a defined bounding area.
/// </summary>
public class BoundsBlocker : Blocker
{
    private BoundingArea _blockArea;

    /// <summary>
    /// Initializes a new bounds blocker bound to the supplied world.
    /// </summary>
    /// <param name="world">The world whose grids this blocker should affect.</param>
    /// <param name="blockArea">The bounding area to block.</param>
    /// <param name="isActive">Flag whether or not blocker is active.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels.</param>
    public BoundsBlocker(
        GridWorld world,
        BoundingArea blockArea,
        bool isActive = true,
        bool cacheCoveredVoxels = false) : base(world, isActive, cacheCoveredVoxels)
    {
        _blockArea = blockArea;
    }

    /// <inheritdoc cref="Blocker.GetBoundsMin"/>
    protected override Vector3d GetBoundsMin() => _blockArea.Min;

    /// <inheritdoc cref="Blocker.GetBoundsMax"/>
    protected override Vector3d GetBoundsMax() => _blockArea.Max;
}
