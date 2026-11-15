//=======================================================================
// GridBoundedVoxelStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids;

/// <summary>Completion status of a bounded physical voxel operation.</summary>
public enum GridBoundedVoxelStatus : byte
{
    /// <summary>A stable world or partition view could not be acquired immediately.</summary>
    WorldNotReady,
    /// <summary>The operation completed, including a possible empty result.</summary>
    Completed,
    /// <summary>The next inspection or its required cleanup exceeded the allowance.</summary>
    WorkBudgetExceeded,
    /// <summary>There was no room to retain another result.</summary>
    CapacityExceeded,
    /// <summary>The query could encounter a grid outside the supported storage or topology.</summary>
    UnsupportedGrid,
    /// <summary>The padded bounds could not be represented.</summary>
    Unrepresentable,
    /// <summary>The previously captured world sequence is no longer current.</summary>
    Stale,
}
