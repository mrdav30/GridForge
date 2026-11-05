//=======================================================================
// VoxelContactFaceKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Topology;

/// <summary>
/// Identifies the world-plane orientation of a face contact.
/// </summary>
public enum VoxelContactFaceKind : byte
{
    /// <summary>The manifold is not a face.</summary>
    None,
    /// <summary>A horizontal footprint segment extruded through a vertical interval.</summary>
    Vertical,
    /// <summary>A convex XZ polygon on a shared Y plane.</summary>
    Horizontal
}
