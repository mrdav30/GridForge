//=======================================================================
// BoundsBlocker.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Grids;
using GridForge.Spatial;

namespace GridForge.Blockers;

/// <summary>
/// A manually placed blocker that obstructs a defined bounding area.
/// </summary>
public class BoundsBlocker : Blocker
{
    private FixedBoundArea _blockArea;

    /// <summary>
    /// Initializes a new bounds blocker bound to the supplied world.
    /// </summary>
    /// <param name="world">The world whose grids this blocker should affect.</param>
    /// <param name="blockArea">The bounding area to block.</param>
    /// <param name="isActive">Flag whether or not blocker is active.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels.</param>
    public BoundsBlocker(
        GridWorld world,
        FixedBoundArea blockArea,
        bool isActive = true,
        bool cacheCoveredVoxels = false) : base(world, isActive, cacheCoveredVoxels)
    {
        _blockArea = blockArea;
    }

    /// <summary>
    /// Initializes a new bounds blocker from XZ-plane bounds on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The world whose grids this blocker should affect.</param>
    /// <param name="boundsMin">The 2D minimum bound whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="boundsMax">The 2D maximum bound whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to block. Defaults to zero.</param>
    /// <param name="isActive">Flag whether or not blocker is active.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels.</param>
    public BoundsBlocker(
        GridWorld world,
        Vector2d boundsMin,
        Vector2d boundsMax,
        Fixed64 layerY = default,
        bool isActive = true,
        bool cacheCoveredVoxels = false)
        : this(
            world,
            CreateBlockArea(boundsMin, boundsMax, layerY),
            isActive,
            cacheCoveredVoxels)
    {
    }

    /// <inheritdoc cref="Blocker.GetBoundsMin"/>
    protected override Vector3d GetBoundsMin() => _blockArea.Min;

    /// <inheritdoc cref="Blocker.GetBoundsMax"/>
    protected override Vector3d GetBoundsMax() => _blockArea.Max;

    private static FixedBoundArea CreateBlockArea(Vector2d boundsMin, Vector2d boundsMax, Fixed64 layerY)
    {
        (Vector3d min, Vector3d max) = GridPlane2d.ToWorldBounds(boundsMin, boundsMax, layerY);
        return new FixedBoundArea(min, max);
    }
}
