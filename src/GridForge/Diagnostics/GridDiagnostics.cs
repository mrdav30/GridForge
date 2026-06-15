//=======================================================================
// GridDiagnostics.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Utility;
using System.Runtime.CompilerServices;

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

        ResultListVisitor visitor = new(results);
        return VisitCellsCore(world, query, ref visitor, scratch);
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
        return VisitCellsCore(world, query, ref visitor, scratch);
    }

    private static GridDiagnosticQueryResult VisitCellsCore<TVisitor>(
        GridWorld world,
        in GridDiagnosticQuery query,
        ref TVisitor visitor,
        GridDiagnosticScratch? scratch)
        where TVisitor : struct, IGridDiagnosticCellVisitor
    {
        scratch?.Clear();
        if (world == null || !world.IsActive)
            return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.InactiveWorld, 0, 0);

        if (query.GridIndex.HasValue)
        {
            if (!world.TryGetGrid(query.GridIndex.Value, out VoxelGrid? requestedGrid)
                || requestedGrid == null
                || !requestedGrid.IsActive)
            {
                return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.InvalidGrid, 0, 0);
            }

            return VisitGridCells(world, requestedGrid, query, ref visitor);
        }

        int cellCount = 0;
        int skippedCellCount = 0;
        bool hasBounds = TryGetQueryBounds(query, out TopologyVoxelAabb queryBounds);

        foreach (VoxelGrid grid in world.ActiveGrids)
        {
            if (!ShouldVisitGrid(grid, query))
                continue;

            GridDiagnosticQueryStatus status = VisitPhysicalCells(
                world,
                grid,
                query,
                hasBounds,
                queryBounds,
                ref visitor,
                ref cellCount,
                ref skippedCellCount);

            if (status != GridDiagnosticQueryStatus.Completed)
                return new GridDiagnosticQueryResult(status, cellCount, skippedCellCount);
        }

        return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.Completed, cellCount, skippedCellCount);
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

    private static GridDiagnosticQueryResult VisitGridCells<TVisitor>(
        GridWorld world,
        VoxelGrid grid,
        in GridDiagnosticQuery query,
        ref TVisitor visitor)
        where TVisitor : struct, IGridDiagnosticCellVisitor
    {
        int cellCount = 0;
        int skippedCellCount = 0;
        bool hasBounds = TryGetQueryBounds(query, out TopologyVoxelAabb queryBounds);

        if (!ShouldVisitGrid(grid, query))
            return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.Completed, 0, 0);

        GridDiagnosticQueryStatus status = VisitPhysicalCells(
            world,
            grid,
            query,
            hasBounds,
            queryBounds,
            ref visitor,
            ref cellCount,
            ref skippedCellCount);

        return new GridDiagnosticQueryResult(status, cellCount, skippedCellCount);
    }

    private static GridDiagnosticQueryStatus VisitPhysicalCells<TVisitor>(
        GridWorld world,
        VoxelGrid grid,
        in GridDiagnosticQuery query,
        bool hasBounds,
        TopologyVoxelAabb queryBounds,
        ref TVisitor visitor,
        ref int cellCount,
        ref int skippedCellCount)
        where TVisitor : struct, IGridDiagnosticCellVisitor
    {
        if (query.AddressMode == GridDiagnosticAddressMode.MissingOnly)
            return GridDiagnosticQueryStatus.Completed;

        VoxelIndex minIndex = default;
        VoxelIndex maxIndex = default;
        if (hasBounds
            && !TopologyVoxelRangeUtility.TryGetCandidateRange(grid, queryBounds, out minIndex, out maxIndex))
        {
            return GridDiagnosticQueryStatus.Completed;
        }

        foreach (Voxel voxel in grid.EnumerateVoxels())
        {
            if (hasBounds && !IsVoxelInQueryBounds(grid, voxel, queryBounds, minIndex, maxIndex))
                continue;

            GridDiagnosticCellState state = GetCellState(voxel);
            if (!MatchesStateFilters(state, query.RequiredStates, query.ExcludedStates))
                continue;

            if (cellCount >= query.MaxCells)
            {
                skippedCellCount++;
                return GridDiagnosticQueryStatus.MaxCellsExceeded;
            }

            GridDiagnosticCell cell = CreatePhysicalCell(world, grid, voxel, state);
            cellCount++;
            if (!visitor.Visit(in cell))
            {
                skippedCellCount++;
                return GridDiagnosticQueryStatus.Truncated;
            }
        }

        return GridDiagnosticQueryStatus.Completed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldVisitGrid(
        VoxelGrid grid,
        in GridDiagnosticQuery query)
    {
        if (!grid.IsActive)
            return false;

        if (query.TopologyKind.HasValue && grid.Configuration.TopologyKind != query.TopologyKind.Value)
            return false;

        return !query.StorageKind.HasValue || grid.StorageKind == query.StorageKind.Value;
    }

    private static bool TryGetQueryBounds(
        in GridDiagnosticQuery query,
        out TopologyVoxelAabb bounds)
    {
        bounds = default;
        if (!query.BoundsMin.HasValue || !query.BoundsMax.HasValue)
            return false;

        Vector3d min = query.BoundsMin.Value;
        Vector3d max = query.BoundsMax.Value;
        bounds = new TopologyVoxelAabb(
            new Vector3d(
                FixedMath.Min(min.X, max.X),
                FixedMath.Min(min.Y, max.Y),
                FixedMath.Min(min.Z, max.Z)),
            new Vector3d(
                FixedMath.Max(min.X, max.X),
                FixedMath.Max(min.Y, max.Y),
                FixedMath.Max(min.Z, max.Z)));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsVoxelInQueryBounds(
        VoxelGrid grid,
        Voxel voxel,
        TopologyVoxelAabb queryBounds,
        VoxelIndex minIndex,
        VoxelIndex maxIndex)
    {
        VoxelIndex index = voxel.Index;
        if (index.x < minIndex.x
            || index.x > maxIndex.x
            || index.y < minIndex.y
            || index.y > maxIndex.y
            || index.z < minIndex.z
            || index.z > maxIndex.z)
        {
            return false;
        }

        TopologyVoxelAabb voxelBounds = TopologyVoxelAabb.FromVoxel(grid, voxel);
        return voxelBounds.Overlaps(queryBounds, Fixed64.Zero);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static GridDiagnosticCellState GetCellState(Voxel voxel)
    {
        GridDiagnosticCellState state = GridDiagnosticCellState.None;
        if (!voxel.IsOccupied && !voxel.IsBlocked)
            state |= GridDiagnosticCellState.Empty;

        if (voxel.IsOccupied)
            state |= GridDiagnosticCellState.Occupied;

        if (voxel.IsBlocked)
            state |= GridDiagnosticCellState.Blocked;

        if (voxel.IsBoundaryVoxel)
            state |= GridDiagnosticCellState.Boundary;

        if (voxel.IsPartioned)
            state |= GridDiagnosticCellState.Partitioned;

        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesStateFilters(
        GridDiagnosticCellState state,
        GridDiagnosticCellState requiredStates,
        GridDiagnosticCellState excludedStates)
    {
        if (requiredStates != GridDiagnosticCellState.None && (state & requiredStates) != requiredStates)
            return false;

        return excludedStates == GridDiagnosticCellState.None || (state & excludedStates) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static GridDiagnosticCell CreatePhysicalCell(
        GridWorld world,
        VoxelGrid grid,
        Voxel voxel,
        GridDiagnosticCellState state) =>
        new(
            GridDiagnosticCellKind.Physical,
            world.SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            voxel.Index,
            voxel.WorldPosition,
            grid.Configuration.TopologyKind,
            grid.StorageKind,
            grid.Configuration.TopologyMetrics,
            state,
            voxel.WorldIndex);

    private struct ResultListVisitor : IGridDiagnosticCellVisitor
    {
        private readonly SwiftList<GridDiagnosticCell> _results;

        public ResultListVisitor(SwiftList<GridDiagnosticCell> results)
        {
            _results = results;
        }

        public bool Visit(in GridDiagnosticCell cell)
        {
            _results.Add(cell);
            return true;
        }
    }
}
