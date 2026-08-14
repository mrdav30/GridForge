//=======================================================================
// GridNavigationPortal.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;
using FixedMathSharp;

namespace GridForge.Grids.Topology;

/// <summary>
/// Stores one exact, agent-independent navigation crossing compiled from two cell prisms.
/// </summary>
/// <remarks>
/// The value retains no live grid state. Profile resolution uses only the stored face geometry,
/// direction, and conservative fixed-point capacities.
/// </remarks>
public readonly struct GridNavigationPortal
{
    /// <summary>The orientation of the shared positive-area face.</summary>
    public VoxelContactFaceKind FaceKind { get; }

    /// <summary>The exact target-center displacement from the source center.</summary>
    public Vector3d SourceToTarget { get; }

    /// <summary>The canonical foot crossing on the shared face.</summary>
    public Vector3d CanonicalFacePoint { get; }

    /// <summary>The greatest conservatively representable horizontal body radius at the crossing.</summary>
    public Fixed64 MaximumHorizontalRadius { get; }

    /// <summary>The greatest body height supported by both directed sides of the crossing.</summary>
    public Fixed64 MaximumBodyHeight { get; }

    /// <summary>Whether this value contains a compiled positive-area navigation face.</summary>
    public bool IsValid =>
        FaceKind is VoxelContactFaceKind.Vertical or VoxelContactFaceKind.Horizontal
        && MaximumHorizontalRadius >= Fixed64.Zero
        && MaximumBodyHeight > Fixed64.Zero;

    internal GridNavigationPortal(
        VoxelContactFaceKind faceKind,
        Vector3d sourceToTarget,
        Vector3d canonicalFacePoint,
        Fixed64 maximumHorizontalRadius,
        Fixed64 maximumBodyHeight)
    {
        FaceKind = faceKind;
        SourceToTarget = sourceToTarget;
        CanonicalFacePoint = canonicalFacePoint;
        MaximumHorizontalRadius = maximumHorizontalRadius;
        MaximumBodyHeight = maximumBodyHeight;
    }

    /// <summary>
    /// Attempts to rigidly translate the canonical face point without fixed-point saturation.
    /// </summary>
    /// <param name="offset">The exact translation applied to the canonical face point.</param>
    /// <param name="translated">The translated portal, or <see langword="default"/> on failure.</param>
    /// <returns>
    /// <see langword="true"/> when this portal is valid and the translated face point is representable;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryTranslate(Vector3d offset, out GridNavigationPortal translated)
    {
        translated = default;
        if (!IsValid
            || !Vector3d.TryAdd(CanonicalFacePoint, offset, out Vector3d translatedFacePoint))
        {
            return false;
        }

        translated = new GridNavigationPortal(
            FaceKind,
            SourceToTarget,
            translatedFacePoint,
            MaximumHorizontalRadius,
            MaximumBodyHeight);
        return true;
    }

    /// <summary>
    /// Attempts to fit a fixed-point body profile and resolve its directed source and target foot anchors.
    /// </summary>
    /// <param name="horizontalRadius">The required nonnegative horizontal body radius.</param>
    /// <param name="bodyHeight">The required positive body height.</param>
    /// <param name="sourceFootAnchor">The last canonical foot anchor on the source side.</param>
    /// <param name="targetFootAnchor">The first canonical foot anchor on the target side.</param>
    /// <returns><see langword="true"/> when the profile fits and both anchors are representable.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryResolveProfile(
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        out Vector3d sourceFootAnchor,
        out Vector3d targetFootAnchor)
    {
        SwiftThrowHelper.ThrowIfArgument(
            horizontalRadius < Fixed64.Zero,
            nameof(horizontalRadius),
            "Horizontal radius must be nonnegative.");
        SwiftThrowHelper.ThrowIfArgument(
            bodyHeight <= Fixed64.Zero,
            nameof(bodyHeight),
            "Body height must be positive.");

        sourceFootAnchor = default;
        targetFootAnchor = default;
        if (!IsValid
            || horizontalRadius > MaximumHorizontalRadius
            || bodyHeight > MaximumBodyHeight)
        {
            return false;
        }

        if (FaceKind == VoxelContactFaceKind.Vertical)
        {
            sourceFootAnchor = CanonicalFacePoint;
            targetFootAnchor = CanonicalFacePoint;
            return true;
        }

        if (!Fixed64.TrySubtract(CanonicalFacePoint.Y, bodyHeight, out Fixed64 lowerFootY))
            return false;

        Vector3d lowerFoot = new Vector3d(CanonicalFacePoint.X, lowerFootY, CanonicalFacePoint.Z);
        if (SourceToTarget.Y > Fixed64.Zero)
        {
            sourceFootAnchor = lowerFoot;
            targetFootAnchor = CanonicalFacePoint;
            return true;
        }

        if (SourceToTarget.Y < Fixed64.Zero)
        {
            sourceFootAnchor = CanonicalFacePoint;
            targetFootAnchor = lowerFoot;
            return true;
        }

        sourceFootAnchor = default;
        targetFootAnchor = default;
        return false;
    }
}
