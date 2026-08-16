//=======================================================================
// GridNavigationCorridorValidationCursor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace GridForge.Grids.Topology;

/// <summary>
/// Describes the state of a resumable navigation-corridor validation.
/// </summary>
public enum GridNavigationCorridorValidationStatus : byte
{
    /// <summary>The input or corridor geometry is invalid.</summary>
    Invalid = 0,

    /// <summary>More bounded work is required.</summary>
    InProgress = 1,

    /// <summary>The corridor certificate is complete.</summary>
    Complete = 2,

    /// <summary>The canonical geometric cost is not representable.</summary>
    CostOverflow = 3
}

/// <summary>
/// Resumably validates one deterministic navigation corridor into caller-owned storage.
/// </summary>
/// <remarks>
/// The ordered cell and waypoint spans are not retained. Their lengths and contents must remain
/// stable between calls. A successful corridor of N cells consumes exactly 2N+1 work units.
/// </remarks>
public struct GridNavigationCorridorValidationCursor
{
    private enum ValidationStage : byte
    {
        Cells,
        EntryAnchor,
        Portals,
        ExitAnchor
    }

    private readonly int _cellCount;
    private readonly Vector3d _entryAnchor;
    private readonly Vector3d _exitAnchor;
    private readonly Fixed64 _radiusClearance;
    private readonly Fixed64 _heightClearance;
    private GridNavigationCorridorValidationStatus _status;
    private ValidationStage _stage;
    private int _cellIndex;
    private int _portalIndex;
    private int _portalWaypointCount;
    private Vector3d _previousPoint;
    private Fixed64 _geometricCost;
    private GridNavigationPortal _selectedPortal;

    /// <summary>The current validation state.</summary>
    public readonly GridNavigationCorridorValidationStatus Status => _status;

    /// <summary>The number of canonical waypoints written, or zero after failure.</summary>
    public readonly int PortalWaypointCount => _portalWaypointCount;

    /// <summary>The checked canonical polyline length accumulated so far.</summary>
    public readonly Fixed64 GeometricCost => _geometricCost;

    /// <summary>
    /// Creates a cursor for one ordered source, witness, and destination cell chain.
    /// </summary>
    /// <param name="cellCount">The stable number of ordered cells supplied to every advance.</param>
    /// <param name="entryAnchor">The source-cell foot anchor.</param>
    /// <param name="exitAnchor">The destination-cell foot anchor.</param>
    /// <param name="radiusClearance">The required nonnegative horizontal body radius.</param>
    /// <param name="heightClearance">The required positive body height.</param>
    public GridNavigationCorridorValidationCursor(
        int cellCount,
        Vector3d entryAnchor,
        Vector3d exitAnchor,
        Fixed64 radiusClearance,
        Fixed64 heightClearance)
    {
        _cellCount = cellCount;
        _entryAnchor = entryAnchor;
        _exitAnchor = exitAnchor;
        _radiusClearance = radiusClearance;
        _heightClearance = heightClearance;
        _status = cellCount >= 2
            && radiusClearance >= Fixed64.Zero
            && heightClearance > Fixed64.Zero
                ? GridNavigationCorridorValidationStatus.InProgress
                : GridNavigationCorridorValidationStatus.Invalid;
        _stage = ValidationStage.Cells;
        _cellIndex = 0;
        _portalIndex = 0;
        _portalWaypointCount = 0;
        _previousPoint = entryAnchor;
        _geometricCost = default;
        _selectedPortal = default;
    }

    /// <summary>
    /// Performs at most <paramref name="maxWork"/> bounded validation units.
    /// </summary>
    /// <param name="orderedCells">The unchanged ordered cells supplied for this cursor.</param>
    /// <param name="portalWaypoints">Caller-owned storage for twice the portal count.</param>
    /// <param name="maxWork">The nonnegative maximum number of work units to perform.</param>
    /// <returns>The resulting validation state.</returns>
    public GridNavigationCorridorValidationStatus Advance(
        ReadOnlySpan<GridCellPrism> orderedCells,
        Span<Vector3d> portalWaypoints,
        int maxWork)
    {
        if (_status != GridNavigationCorridorValidationStatus.InProgress)
            return _status;

        if (orderedCells.Length != _cellCount
            || _cellCount - 1 > portalWaypoints.Length / 2)
        {
            return Fail(GridNavigationCorridorValidationStatus.Invalid);
        }

        while (maxWork-- > 0 && _status == GridNavigationCorridorValidationStatus.InProgress)
            PerformNext(orderedCells, portalWaypoints);

        return _status;
    }

    private void PerformNext(
        ReadOnlySpan<GridCellPrism> orderedCells,
        Span<Vector3d> portalWaypoints)
    {
        switch (_stage)
        {
            case ValidationStage.Cells:
                GridCellPrism cell = orderedCells[_cellIndex++];
                if (cell.FootprintVertexCount is not 4 and not 6
                    || cell.VerticalMax < cell.VerticalMin)
                {
                    Fail(GridNavigationCorridorValidationStatus.Invalid);
                    return;
                }

                if (_cellIndex == _cellCount)
                    _stage = ValidationStage.EntryAnchor;
                return;

            case ValidationStage.EntryAnchor:
                GridCellGeometry.TryCreateNavigationPortal(
                    orderedCells[0],
                    orderedCells[1],
                    out _selectedPortal);
                if (!GridCellGeometry.IsNavigationBodyAnchorValid(
                        orderedCells[0],
                        _entryAnchor,
                        _radiusClearance,
                        _heightClearance,
                        _selectedPortal))
                {
                    Fail(GridNavigationCorridorValidationStatus.Invalid);
                    return;
                }

                _stage = ValidationStage.Portals;
                return;

            case ValidationStage.Portals:
                ValidateNextPortal(orderedCells, portalWaypoints);
                return;

            default:
                ValidateExitAnchor(orderedCells);
                return;
        }
    }

    private void ValidateNextPortal(
        ReadOnlySpan<GridCellPrism> orderedCells,
        Span<Vector3d> portalWaypoints)
    {
        GridCellPrism source = orderedCells[_portalIndex];
        GridCellPrism target = orderedCells[_portalIndex + 1];
        if ((_portalIndex > 0
                && !GridCellGeometry.TryCreateNavigationPortal(source, target, out _selectedPortal))
            || !_selectedPortal.IsValid
            || !_selectedPortal.TryResolveProfile(
                _radiusClearance,
                _heightClearance,
                out Vector3d sourcePoint,
                out Vector3d targetPoint))
        {
            Fail(GridNavigationCorridorValidationStatus.Invalid);
            return;
        }

        portalWaypoints[_portalWaypointCount++] = sourcePoint;
        if (!TryAccumulateDistance(_previousPoint, sourcePoint))
        {
            Fail(GridNavigationCorridorValidationStatus.CostOverflow);
            return;
        }

        _previousPoint = sourcePoint;
        if (_selectedPortal.FaceKind == VoxelContactFaceKind.Horizontal)
        {
            portalWaypoints[_portalWaypointCount++] = targetPoint;
            if (!TryAccumulateDistance(_previousPoint, targetPoint))
            {
                Fail(GridNavigationCorridorValidationStatus.CostOverflow);
                return;
            }

            _previousPoint = targetPoint;
        }

        if (!GridCellGeometry.IsNavigationBodyAnchorValid(
                source,
                sourcePoint,
                _radiusClearance,
                _heightClearance,
                _selectedPortal)
            || !GridCellGeometry.IsNavigationBodyAnchorValid(
                target,
                targetPoint,
                _radiusClearance,
                _heightClearance,
                _selectedPortal))
        {
            Fail(GridNavigationCorridorValidationStatus.Invalid);
            return;
        }

        _portalIndex++;
        if (_portalIndex == _cellCount - 1)
            _stage = ValidationStage.ExitAnchor;
    }

    private void ValidateExitAnchor(ReadOnlySpan<GridCellPrism> orderedCells)
    {
        if (!TryAccumulateDistance(_previousPoint, _exitAnchor))
        {
            Fail(GridNavigationCorridorValidationStatus.CostOverflow);
            return;
        }

        if (!GridCellGeometry.IsNavigationBodyAnchorValid(
                orderedCells[_cellCount - 1],
                _exitAnchor,
                _radiusClearance,
                _heightClearance,
                _selectedPortal))
        {
            Fail(GridNavigationCorridorValidationStatus.Invalid);
            return;
        }

        _status = GridNavigationCorridorValidationStatus.Complete;
    }

    private GridNavigationCorridorValidationStatus Fail(
        GridNavigationCorridorValidationStatus status)
    {
        _portalWaypointCount = 0;
        _geometricCost = default;
        _status = status;
        return status;
    }

    private bool TryAccumulateDistance(Vector3d start, Vector3d end)
    {
        return Vector3d.TryGetDistance(start, end, out Fixed64 distance)
            && Fixed64.TryAdd(_geometricCost, distance, out _geometricCost);
    }

}
