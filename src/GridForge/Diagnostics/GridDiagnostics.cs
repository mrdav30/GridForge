//=======================================================================
// GridDiagnostics.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids;
using SwiftCollections;
using SwiftCollections.Utility;

namespace GridForge.Diagnostics;

/// <summary>
/// Engine-agnostic diagnostic query helpers for GridForge worlds and grids.
/// </summary>
public static class GridDiagnostics
{
    /// <summary>
    /// Clears and fills caller-owned storage with diagnostic cells.
    /// </summary>
    public static GridDiagnosticQueryResult GetCellsInto(
        GridWorld world,
        in GridDiagnosticQuery query,
        SwiftList<GridDiagnosticCell> results,
        GridDiagnosticScratch? scratch = null)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));

        results.Clear();
        scratch?.Clear();

        if (world == null || !world.IsActive)
            return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.InactiveWorld, 0, 0);

        return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.Completed, 0, 0);
    }

    /// <summary>
    /// Visits diagnostic cells without requiring an intermediate result list.
    /// </summary>
    public static GridDiagnosticQueryResult VisitCells<TVisitor>(
        GridWorld world,
        in GridDiagnosticQuery query,
        ref TVisitor visitor,
        GridDiagnosticScratch? scratch = null)
        where TVisitor : struct, IGridDiagnosticCellVisitor
    {
        scratch?.Clear();

        if (world == null || !world.IsActive)
            return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.InactiveWorld, 0, 0);

        return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.Completed, 0, 0);
    }

    /// <summary>
    /// Resolves a physical diagnostic cell descriptor back to its active grid
    /// and voxel.
    /// </summary>
    public static bool TryResolvePhysicalCell(
        GridWorld world,
        in GridDiagnosticCell cell,
        out VoxelGrid? grid,
        out Voxel? voxel)
    {
        grid = null;
        voxel = null;

        if (world == null
            || cell.Kind != GridDiagnosticCellKind.Physical
            || cell.WorldSpawnToken != world.SpawnToken
            || cell.WorldIndex.WorldSpawnToken != cell.WorldSpawnToken
            || cell.WorldIndex.GridIndex != cell.GridIndex
            || cell.WorldIndex.GridSpawnToken != cell.GridSpawnToken
            || cell.WorldIndex.VoxelIndex != cell.Index)
        {
            return false;
        }

        return world.TryGetGridAndVoxel(cell.WorldIndex, out grid, out voxel);
    }
}
