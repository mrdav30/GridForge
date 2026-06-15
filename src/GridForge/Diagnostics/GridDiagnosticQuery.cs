//=======================================================================
// GridDiagnosticQuery.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;

namespace GridForge.Diagnostics;

/// <summary>
/// Immutable filter and budget settings for a diagnostic cell query.
/// </summary>
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
    public static GridDiagnosticQuery AllPhysical() => new GridDiagnosticQuery(maxCells: DefaultMaxCells);

    /// <summary>
    /// Creates a physical-cell query for one world-local grid slot.
    /// </summary>
    public static GridDiagnosticQuery ForGrid(ushort gridIndex) => new(gridIndex: gridIndex);

    /// <summary>
    /// Creates a physical-cell query clipped to world-space bounds.
    /// </summary>
    public static GridDiagnosticQuery ForBounds(Vector3d min, Vector3d max) => new(
        boundsMin: min,
        boundsMax: max);
}
