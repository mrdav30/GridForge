//=======================================================================
// GridTraceScratch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace GridForge.Grids;

/// <summary>
/// Reusable temporary storage for allocation-sensitive grid tracing and coverage operations.
/// </summary>
public sealed class GridTraceScratch
{
    internal SwiftHashSet<ushort> ProcessedGrids { get; } = new();

    internal SwiftHashSet<Voxel> VoxelRedundancy { get; } = new();

    /// <summary>
    /// Clears all temporary trace state while retaining allocated backing storage.
    /// </summary>
    public void Clear()
    {
        ProcessedGrids.Clear();
        VoxelRedundancy.Clear();
    }
}
