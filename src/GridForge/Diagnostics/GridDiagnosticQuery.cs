//=======================================================================
// GridDiagnosticQuery.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;

namespace GridForge.Diagnostics;

/// <summary>
/// Immutable filter and budget settings for a diagnostic cell query.
/// </summary>
/// <remarks><c>default(GridDiagnosticQuery)</c> and parameterless struct construction have a zero cell budget and can immediately report MaxCellsExceeded in a nonempty query. Use <see cref="AllPhysical"/> or an explicit constructor argument to obtain the default 65536-cell budget. A supplied nonpositive maxCells constructor argument is replaced with that budget.</remarks>
public readonly struct GridDiagnosticQuery
{
    /// <summary>
    /// Default maximum number of diagnostic cells returned by a single query.
    /// </summary>
    public const int DefaultMaxCells = 65536;

    /// <summary>
    /// Optional world-local grid slot filter.
    /// </summary>
    public readonly ushort? GridIndex;

    /// <summary>
    /// Optional topology filter.
    /// </summary>
    public readonly GridTopologyKind? TopologyKind;

    /// <summary>
    /// Optional storage-kind filter.
    /// </summary>
    public readonly GridStorageKind? StorageKind;

    /// <summary>
    /// Selects whether the query should return physical cells, missing sparse
    /// address cells, or both.
    /// </summary>
    public readonly GridDiagnosticAddressMode AddressMode;

    /// <summary>
    /// State flags that a returned cell must contain.
    /// </summary>
    public readonly GridDiagnosticCellState RequiredStates;

    /// <summary>
    /// State flags that exclude a cell from the result.
    /// </summary>
    public readonly GridDiagnosticCellState ExcludedStates;

    /// <summary>
    /// Optional world-space minimum bounds for the query.
    /// </summary>
    public readonly Vector3d? BoundsMin;

    /// <summary>
    /// Optional world-space maximum bounds for the query.
    /// </summary>
    public readonly Vector3d? BoundsMax;

    /// <summary>
    /// Maximum number of cells this query may return before it is stopped.
    /// </summary>
    public readonly int MaxCells;

    /// <summary>
    /// Allows missing sparse address-space queries to scan the whole grid
    /// address space when no bounds are supplied.
    /// </summary>
    public readonly bool AllowFullAddressSpaceScan;

    /// <summary>
    /// Initializes a diagnostic query.
    /// </summary>
    /// <param name="gridIndex">Optional world-local grid slot filter.</param>
    /// <param name="topologyKind">Optional topology filter.</param>
    /// <param name="storageKind">Optional storage-kind filter.</param>
    /// <param name="addressMode">Selects whether the query should return physical cells, missing sparse address cells, or both.</param>
    /// <param name="requiredStates">State flags that a returned cell must contain.</param>
    /// <param name="excludedStates">State flags that exclude a cell from the result.</param>
    /// <param name="boundsMin">Optional minimum world-space query corner. Both corners must be supplied for bounds filtering.</param>
    /// <param name="boundsMax">Optional maximum world-space query corner. Both corners must be supplied for bounds filtering.</param>
    /// <param name="maxCells">Maximum emitted cells; nonpositive values become DefaultMaxCells.</param>
    /// <param name="allowFullAddressSpaceScan">True permits unbounded-by-region missing sparse address scans. Defaults to false.</param>
    public GridDiagnosticQuery(
        ushort? gridIndex = null,
        GridTopologyKind? topologyKind = null,
        GridStorageKind? storageKind = null,
        GridDiagnosticAddressMode addressMode = GridDiagnosticAddressMode.PhysicalOnly,
        GridDiagnosticCellState requiredStates = GridDiagnosticCellState.None,
        GridDiagnosticCellState excludedStates = GridDiagnosticCellState.None,
        Vector3d? boundsMin = null,
        Vector3d? boundsMax = null,
        int maxCells = DefaultMaxCells,
        bool allowFullAddressSpaceScan = false)
    {
        GridIndex = gridIndex;
        TopologyKind = topologyKind;
        StorageKind = storageKind;
        AddressMode = addressMode;
        RequiredStates = requiredStates;
        ExcludedStates = excludedStates;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        MaxCells = maxCells > 0 ? maxCells : DefaultMaxCells;
        AllowFullAddressSpaceScan = allowFullAddressSpaceScan;
    }

    /// <summary>
    /// Creates a query that returns physical cells from all active grids.
    /// </summary>
    /// <returns>A physical-cell query with no grid or bounds filters and the default cell budget.</returns>
    public static GridDiagnosticQuery AllPhysical() => new GridDiagnosticQuery(maxCells: DefaultMaxCells);

    /// <summary>
    /// Creates a physical-cell query for one world-local grid slot.
    /// </summary>
    /// <returns>A physical-cell query for the supplied slot with the default cell budget.</returns>
    /// <param name="gridIndex">The reusable world-local grid slot to filter; this is not a durable generation identity.</param>
    public static GridDiagnosticQuery ForGrid(ushort gridIndex) => new(gridIndex: gridIndex);

    /// <summary>
    /// Creates a physical-cell query clipped to world-space bounds.
    /// </summary>
    /// <param name="min">The minimum world-space corner, in fixed-point world units.</param>
    /// <param name="max">The maximum world-space corner, in fixed-point world units.</param>
    /// <returns>A physical-cell query with the supplied world bounds and the default cell budget.</returns>
    public static GridDiagnosticQuery ForBounds(Vector3d min, Vector3d max) => new(
        boundsMin: min,
        boundsMax: max);
}
