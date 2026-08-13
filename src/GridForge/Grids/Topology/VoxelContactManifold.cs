//=======================================================================
// VoxelContactManifold.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;

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

/// <summary>
/// Describes exact point, edge, face, or volume contact between two cell prisms.
/// </summary>
public readonly struct VoxelContactManifold
{
    /// <summary>The source cell identity.</summary>
    public WorldVoxelIndex Source { get; }

    /// <summary>The target cell identity.</summary>
    public WorldVoxelIndex Target { get; }

    /// <summary>The target-center displacement from the source center.</summary>
    public Vector3d SourceToTarget { get; }

    /// <summary>The exact contact classification.</summary>
    public VoxelContactKind Kind { get; }

    /// <summary>The face orientation when <see cref="Kind"/> is <see cref="VoxelContactKind.Face"/>.</summary>
    public VoxelContactFaceKind FaceKind { get; }

    /// <summary>The lower bound of the shared vertical interval or horizontal face plane.</summary>
    public Fixed64 VerticalMin { get; }

    /// <summary>The upper bound of the shared vertical interval or horizontal face plane.</summary>
    public Fixed64 VerticalMax { get; }

    /// <summary>The first endpoint of a vertical face's exact XZ contact segment.</summary>
    public Vector2d HorizontalSegmentStart { get; }

    /// <summary>The second endpoint of a vertical face's exact XZ contact segment.</summary>
    public Vector2d HorizontalSegmentEnd { get; }

    /// <summary>The exact overlap polygon for a horizontal face or volume overlap.</summary>
    public GridConvexPolygon2d HorizontalPolygon { get; }

    /// <summary>The face area, or horizontal overlap area for a volume overlap.</summary>
    public Fixed64 CheckedArea { get; }

    /// <summary>Whether <see cref="CheckedArea"/> is representable without saturation.</summary>
    public bool IsAreaRepresentable { get; }

    /// <summary>Whether this manifold can be considered as an automatic portal before agent clearance checks.</summary>
    public bool IsPositiveAreaFace =>
        Kind == VoxelContactKind.Face
        && IsAreaRepresentable
        && CheckedArea > Fixed64.Zero;

    /// <summary>The exact width of a vertical face contact segment.</summary>
    public Fixed64 VerticalFaceWidth => FaceKind == VoxelContactFaceKind.Vertical
        ? Vector2d.Distance(HorizontalSegmentStart, HorizontalSegmentEnd)
        : Fixed64.Zero;

    /// <summary>The exact height of a vertical face contact interval.</summary>
    public Fixed64 VerticalFaceHeight => FaceKind == VoxelContactFaceKind.Vertical
        ? VerticalMax - VerticalMin
        : Fixed64.Zero;

    internal VoxelContactManifold(
        WorldVoxelIndex source,
        WorldVoxelIndex target,
        Vector3d sourceToTarget,
        VoxelContactKind kind,
        VoxelContactFaceKind faceKind,
        Fixed64 verticalMin,
        Fixed64 verticalMax,
        Vector2d horizontalSegmentStart,
        Vector2d horizontalSegmentEnd,
        GridConvexPolygon2d horizontalPolygon,
        Fixed64 checkedArea,
        bool isAreaRepresentable)
    {
        Source = source;
        Target = target;
        SourceToTarget = sourceToTarget;
        Kind = kind;
        FaceKind = faceKind;
        VerticalMin = verticalMin;
        VerticalMax = verticalMax;
        HorizontalSegmentStart = horizontalSegmentStart;
        HorizontalSegmentEnd = horizontalSegmentEnd;
        HorizontalPolygon = horizontalPolygon;
        CheckedArea = checkedArea;
        IsAreaRepresentable = isAreaRepresentable;
    }
}
