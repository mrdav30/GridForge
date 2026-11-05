//=======================================================================
// VoxelContactKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Topology;

/// <summary>
/// Classifies the exact shared geometry of two closed cell prisms.
/// </summary>
public enum VoxelContactKind : byte
{
    /// <summary>The prisms do not intersect.</summary>
    Separated,
    /// <summary>The prisms share one point.</summary>
    Point,
    /// <summary>The prisms share a positive-length line.</summary>
    Edge,
    /// <summary>The prisms share a positive-area face.</summary>
    Face,
    /// <summary>The prism interiors overlap in three dimensions.</summary>
    VolumeOverlap
}
