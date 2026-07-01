//=======================================================================
// AreaBlocker.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Grids;
using GridForge.Spatial;

namespace GridForge.Blockers;

/// <summary>
/// A manually placed blocker that obstructs a two-dimensional XZ-plane area on one world Y layer.
/// </summary>
public class AreaBlocker : Blocker
{
    private readonly FixedBoundArea _blockArea;
    private readonly Fixed64 _layerY;

    /// <summary>
    /// Initializes a new area blocker bound to the supplied world.
    /// </summary>
    /// <param name="world">The world whose grids this blocker should affect.</param>
    /// <param name="blockArea">The XZ-plane area to block.</param>
    /// <param name="layerY">The world Y layer to block. Defaults to zero.</param>
    /// <param name="isActive">Flag whether or not blocker is active.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels.</param>
    public AreaBlocker(
        GridWorld world,
        FixedBoundArea blockArea,
        Fixed64 layerY = default,
        bool isActive = true,
        bool cacheCoveredVoxels = false) : base(world, isActive, cacheCoveredVoxels)
    {
        _blockArea = blockArea;
        _layerY = layerY;
    }

    /// <summary>
    /// Initializes a new area blocker from XZ-plane min/max bounds.
    /// </summary>
    /// <param name="world">The world whose grids this blocker should affect.</param>
    /// <param name="boundsMin">The 2D minimum bound whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="boundsMax">The 2D maximum bound whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to block. Defaults to zero.</param>
    /// <param name="isActive">Flag whether or not blocker is active.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels.</param>
    public AreaBlocker(
        GridWorld world,
        Vector2d boundsMin,
        Vector2d boundsMax,
        Fixed64 layerY = default,
        bool isActive = true,
        bool cacheCoveredVoxels = false)
        : this(
            world,
            FixedBoundArea.FromMinMax(boundsMin, boundsMax),
            layerY,
            isActive,
            cacheCoveredVoxels)
    { }

    /// <inheritdoc cref="Blocker.GetBoundsMin"/>
    protected override Vector3d GetBoundsMin() => GridPlane2d.ToWorld(_blockArea.Min, _layerY);

    /// <inheritdoc cref="Blocker.GetBoundsMax"/>
    protected override Vector3d GetBoundsMax() => GridPlane2d.ToWorld(_blockArea.Max, _layerY);
}
