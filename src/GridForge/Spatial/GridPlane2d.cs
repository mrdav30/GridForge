//=======================================================================
// GridPlane2d.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

/// <summary>
/// Provides GridForge's 2D XZ-plane projection helpers.
/// </summary>
/// <remarks>
/// In GridForge 2D query APIs, <see cref="Vector2d.X"/> maps to world X,
/// <see cref="Vector2d.Y"/> maps to world Z, and the supplied layer maps to world Y.
/// </remarks>
public static class GridPlane2d
{
    /// <summary>
    /// Converts an XZ-plane 2D position into a world-space 3D position.
    /// </summary>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to apply. Defaults to zero.</param>
    /// <returns>The equivalent world-space position.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d ToWorld(Vector2d position, Fixed64 layerY = default)
    {
        return new Vector3d(position.X, layerY, position.Y);
    }

    /// <summary>
    /// Projects a world-space 3D position into GridForge's 2D XZ plane.
    /// </summary>
    /// <param name="position">The world-space position to project.</param>
    /// <returns>A 2D position containing world X and world Z.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2d FromWorld(Vector3d position)
    {
        return new Vector2d(position.X, position.Z);
    }

    /// <summary>
    /// Converts XZ-plane 2D bounds into layer-locked world-space 3D bounds.
    /// </summary>
    /// <param name="min">The 2D minimum bound whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="max">The 2D maximum bound whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to apply to both bounds. Defaults to zero.</param>
    /// <returns>The equivalent layer-locked world-space bounds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Vector3d min, Vector3d max) ToWorldBounds(
        Vector2d min,
        Vector2d max,
        Fixed64 layerY = default)
    {
        return (ToWorld(min, layerY), ToWorld(max, layerY));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Fixed64 DistanceSquaredXZ(Vector3d a, Vector3d b)
    {
        Fixed64 deltaX = a.X - b.X;
        Fixed64 deltaZ = a.Z - b.Z;
        return (deltaX * deltaX) + (deltaZ * deltaZ);
    }
}
