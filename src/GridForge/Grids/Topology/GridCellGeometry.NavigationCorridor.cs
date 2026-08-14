//=======================================================================
// GridCellGeometry.NavigationCorridor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace GridForge.Grids.Topology;

public static partial class GridCellGeometry
{
    /// <summary>
    /// Validates a canonical, clearance-bearing corridor through an ordered chain of exact cell prisms.
    /// </summary>
    /// <remarks>
    /// Consecutive cells must share a positive-area face. Vertical faces contribute one portal-center
    /// waypoint. Horizontal faces contribute the last point wholly contained by the source cell and the
    /// first point wholly contained by the target cell. The resulting polyline and checked length are
    /// deterministic and independent of runtime grid identity.
    /// </remarks>
    /// <param name="orderedCells">Source cell, zero or more witness cells, and destination cell.</param>
    /// <param name="entryAnchor">Foot position inside the source cell.</param>
    /// <param name="exitAnchor">Foot position inside the destination cell.</param>
    /// <param name="radiusClearance">Required horizontal body radius.</param>
    /// <param name="heightClearance">Required positive body height.</param>
    /// <param name="portalWaypoints">Caller-owned storage with capacity for twice the portal count.</param>
    /// <param name="portalWaypointCount">The number of canonical portal waypoints written.</param>
    /// <param name="geometricCost">The checked fixed-point length of the canonical polyline.</param>
    public static bool TryValidateNavigationCorridor(
        ReadOnlySpan<GridCellPrism> orderedCells,
        Vector3d entryAnchor,
        Vector3d exitAnchor,
        Fixed64 radiusClearance,
        Fixed64 heightClearance,
        Span<Vector3d> portalWaypoints,
        out int portalWaypointCount,
        out Fixed64 geometricCost)
    {
        var cursor = new GridNavigationCorridorValidationCursor(
            orderedCells.Length,
            entryAnchor,
            exitAnchor,
            radiusClearance,
            heightClearance);
        while (cursor.Status == GridNavigationCorridorValidationStatus.InProgress)
            cursor.Advance(orderedCells, portalWaypoints, int.MaxValue);

        portalWaypointCount = cursor.PortalWaypointCount;
        geometricCost = cursor.GeometricCost;
        return cursor.Status == GridNavigationCorridorValidationStatus.Complete;
    }
}
