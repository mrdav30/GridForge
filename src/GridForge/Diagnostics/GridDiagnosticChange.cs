//=======================================================================
// GridDiagnosticChange.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections.Utility;
using System;

namespace GridForge.Diagnostics;

/// <summary>
/// Immutable descriptor for one diagnostic dirty change captured by a session.
/// </summary>
public readonly struct GridDiagnosticChange :
    IEquatable<GridDiagnosticChange>,
    IComparable<GridDiagnosticChange>
{
    /// <summary>
    /// Kind flags coalesced for this dirty change.
    /// </summary>
    public readonly GridDiagnosticChangeKind Kind;

    /// <summary>
    /// Runtime token of the world that produced the change.
    /// </summary>
    public readonly int WorldSpawnToken;

    /// <summary>
    /// World-local grid slot affected by the change, or
    /// <see cref="ushort.MaxValue"/> for world-scoped changes.
    /// </summary>
    public readonly ushort GridIndex;

    /// <summary>
    /// Runtime token of the grid instance affected by the change.
    /// </summary>
    public readonly int GridSpawnToken;

    /// <summary>
    /// World-scoped voxel identity for cell-scoped changes.
    /// </summary>
    public readonly WorldVoxelIndex WorldIndex;

    /// <summary>
    /// Topology-local voxel index for cell or sparse address-range changes.
    /// </summary>
    public readonly VoxelIndex VoxelIndex;

    /// <summary>
    /// Minimum world-space bounds affected by grid or range-scoped changes.
    /// </summary>
    public readonly Vector3d BoundsMin;

    /// <summary>
    /// Maximum world-space bounds affected by grid or range-scoped changes.
    /// </summary>
    public readonly Vector3d BoundsMax;

    /// <summary>
    /// Initializes a diagnostic dirty-change descriptor.
    /// </summary>
    public GridDiagnosticChange(
        GridDiagnosticChangeKind kind,
        int worldSpawnToken,
        ushort gridIndex,
        int gridSpawnToken,
        WorldVoxelIndex worldIndex,
        VoxelIndex voxelIndex,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        Kind = kind;
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        WorldIndex = worldIndex;
        VoxelIndex = voxelIndex;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    internal GridDiagnosticChange WithKind(GridDiagnosticChangeKind kind) =>
        new(
            kind,
            WorldSpawnToken,
            GridIndex,
            GridSpawnToken,
            WorldIndex,
            VoxelIndex,
            BoundsMin,
            BoundsMax);

    /// <inheritdoc/>
    public int CompareTo(GridDiagnosticChange other)
    {
        int result = WorldSpawnToken.CompareTo(other.WorldSpawnToken);
        if (result != 0)
            return result;

        result = GetScopeOrder(this).CompareTo(GetScopeOrder(other));
        if (result != 0)
            return result;

        result = GridIndex.CompareTo(other.GridIndex);
        if (result != 0)
            return result;

        result = GridSpawnToken.CompareTo(other.GridSpawnToken);
        if (result != 0)
            return result;

        result = VoxelIndex.CompareTo(other.VoxelIndex);
        if (result != 0)
            return result;

        result = CompareVector(BoundsMin, other.BoundsMin);
        if (result != 0)
            return result;

        result = CompareVector(BoundsMax, other.BoundsMax);
        if (result != 0)
            return result;

        return ((int)Kind).CompareTo((int)other.Kind);
    }

    /// <inheritdoc/>
    public bool Equals(GridDiagnosticChange other) =>
        Kind == other.Kind
        && WorldSpawnToken == other.WorldSpawnToken
        && GridIndex == other.GridIndex
        && GridSpawnToken == other.GridSpawnToken
        && WorldIndex.Equals(other.WorldIndex)
        && VoxelIndex.Equals(other.VoxelIndex)
        && BoundsMin.Equals(other.BoundsMin)
        && BoundsMax.Equals(other.BoundsMax);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GridDiagnosticChange other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes((int)Kind, WorldSpawnToken);
        hash = SwiftHashTools.CombineHashCodes(hash, GridIndex);
        hash = SwiftHashTools.CombineHashCodes(hash, GridSpawnToken);
        hash = SwiftHashTools.CombineHashCodes(hash, WorldIndex.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, VoxelIndex.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, BoundsMin.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, BoundsMax.GetHashCode());
    }

    private static int GetScopeOrder(GridDiagnosticChange change)
    {
        if ((change.Kind & GridDiagnosticChangeKind.WorldReset) != 0)
            return 0;

        if (change.WorldIndex.WorldSpawnToken != 0 || change.WorldIndex.VoxelIndex.IsAllocated)
            return 2;

        if ((change.Kind & GridDiagnosticChangeKind.SparseAddressChanged) != 0)
            return 3;

        return 1;
    }

    private static int CompareVector(Vector3d left, Vector3d right)
    {
        int result = CompareFixed(left.X, right.X);
        if (result != 0)
            return result;

        result = CompareFixed(left.Y, right.Y);
        return result != 0 ? result : CompareFixed(left.Z, right.Z);
    }

    private static int CompareFixed(Fixed64 left, Fixed64 right)
    {
        if (left < right)
            return -1;

        return left > right ? 1 : 0;
    }
}
