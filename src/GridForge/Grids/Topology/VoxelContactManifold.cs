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
/// Describes exact point, edge, face, or volume contact between two cell prisms.
/// </summary>
public readonly struct VoxelContactManifold
{
    /// <summary>The source cell identity.</summary>
    /// <value>The source cell identity.</value>
    public WorldVoxelIndex Source { get; }

    /// <summary>The target cell identity.</summary>
    /// <value>The target cell identity.</value>
    public WorldVoxelIndex Target { get; }

    /// <summary>The target-center displacement from the source center.</summary>
    /// <value>The target-center displacement from the source center.</value>
    public Vector3d SourceToTarget { get; }

    /// <summary>The exact contact classification.</summary>
    /// <value>The exact contact classification.</value>
    public VoxelContactKind Kind { get; }

    /// <summary>The face orientation when <see cref="Kind"/> is <see cref="VoxelContactKind.Face"/>.</summary>
    /// <value>The face orientation when <see cref="P:GridForge.Grids.Topology.VoxelContactManifold.Kind" /> is <see cref="F:GridForge.Grids.Topology.VoxelContactKind.Face" />.</value>
    public VoxelContactFaceKind FaceKind { get; }

    /// <summary>The lower bound of the shared vertical interval or horizontal face plane.</summary>
    /// <value>The lower bound of the shared vertical interval or horizontal face plane.</value>
    public Fixed64 VerticalMin { get; }

    /// <summary>The upper bound of the shared vertical interval or horizontal face plane.</summary>
    /// <value>The upper bound of the shared vertical interval or horizontal face plane.</value>
    public Fixed64 VerticalMax { get; }

    /// <summary>The first endpoint of a vertical face's exact XZ contact segment.</summary>
    /// <value>The first endpoint of a vertical face's exact XZ contact segment.</value>
    public Vector2d HorizontalSegmentStart { get; }

    /// <summary>The second endpoint of a vertical face's exact XZ contact segment.</summary>
    /// <value>The second endpoint of a vertical face's exact XZ contact segment.</value>
    public Vector2d HorizontalSegmentEnd { get; }

    /// <summary>The exact overlap polygon for a horizontal face or volume overlap.</summary>
    /// <value>The exact overlap polygon for a horizontal face or volume overlap.</value>
    public GridConvexPolygon2d HorizontalPolygon { get; }

    /// <summary>The face area, or horizontal overlap area for a volume overlap.</summary>
    /// <value>The face area, or horizontal overlap area for a volume overlap.</value>
    public Fixed64 CheckedArea { get; }

    /// <summary>Whether <see cref="CheckedArea"/> is representable without saturation.</summary>
    /// <value>Whether <see cref="P:GridForge.Grids.Topology.VoxelContactManifold.CheckedArea" /> is representable without saturation.</value>
    public bool IsAreaRepresentable { get; }

    /// <summary>Whether this manifold can be considered as an automatic portal before agent clearance checks.</summary>
    /// <value>Whether this manifold can be considered as an automatic portal before agent clearance checks.</value>
    public bool IsPositiveAreaFace =>
        Kind == VoxelContactKind.Face
        && IsAreaRepresentable
        && CheckedArea > Fixed64.Zero;

    /// <summary>The exact width of a vertical face contact segment.</summary>
    /// <value>The exact width of a vertical face contact segment.</value>
    public Fixed64 VerticalFaceWidth => FaceKind == VoxelContactFaceKind.Vertical
        ? Vector2d.Distance(HorizontalSegmentStart, HorizontalSegmentEnd)
        : Fixed64.Zero;

    /// <summary>The exact height of a vertical face contact interval.</summary>
    /// <value>The exact height of a vertical face contact interval.</value>
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
