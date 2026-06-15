//=======================================================================
// GridDiagnosticCell.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace GridForge.Diagnostics;

/// <summary>
/// Immutable descriptor for a physical voxel or sparse address-space cell.
/// </summary>
public readonly struct GridDiagnosticCell
{
    /// <summary>
    /// Identifies whether this descriptor represents a physical voxel or a
    /// missing sparse address-space cell.
    /// </summary>
    public readonly GridDiagnosticCellKind Kind;

    /// <summary>
    /// Runtime token of the owning world when the descriptor was created.
    /// </summary>
    public readonly int WorldSpawnToken;

    /// <summary>
    /// World-local grid slot that owns this cell.
    /// </summary>
    public readonly ushort GridIndex;

    /// <summary>
    /// Runtime token of the grid instance when the descriptor was created.
    /// </summary>
    public readonly int GridSpawnToken;

    /// <summary>
    /// Topology-local cell index.
    /// </summary>
    public readonly VoxelIndex Index;

    /// <summary>
    /// Topology-projected world-space center for the cell.
    /// </summary>
    public readonly Vector3d WorldPosition;

    /// <summary>
    /// Topology kind used to interpret the local index and geometry.
    /// </summary>
    public readonly GridTopologyKind TopologyKind;

    /// <summary>
    /// Storage kind of the grid that owns this descriptor.
    /// </summary>
    public readonly GridStorageKind StorageKind;

    /// <summary>
    /// Topology metrics used to derive diagnostic geometry.
    /// </summary>
    public readonly GridTopologyMetrics TopologyMetrics;

    /// <summary>
    /// Diagnostic state flags for filtering and adapter overlays.
    /// </summary>
    public readonly GridDiagnosticCellState State;

    /// <summary>
    /// World-scoped identity for the cell. Missing sparse address cells use a
    /// potential identity and do not resolve to a physical voxel.
    /// </summary>
    public readonly WorldVoxelIndex WorldIndex;

    /// <summary>
    /// Initializes a diagnostic cell descriptor.
    /// </summary>
    public GridDiagnosticCell(
        GridDiagnosticCellKind kind,
        int worldSpawnToken,
        ushort gridIndex,
        int gridSpawnToken,
        VoxelIndex index,
        Vector3d worldPosition,
        GridTopologyKind topologyKind,
        GridStorageKind storageKind,
        GridTopologyMetrics topologyMetrics,
        GridDiagnosticCellState state,
        WorldVoxelIndex worldIndex)
    {
        Kind = kind;
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        Index = index;
        WorldPosition = worldPosition;
        TopologyKind = topologyKind;
        StorageKind = storageKind;
        TopologyMetrics = topologyMetrics;
        State = state;
        WorldIndex = worldIndex;
    }
}
