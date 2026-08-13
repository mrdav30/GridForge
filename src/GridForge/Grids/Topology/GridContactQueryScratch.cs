//=======================================================================
// GridContactQueryScratch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace GridForge.Grids.Topology;

/// <summary>
/// Owns reusable temporary storage for allocation-free warmed exact-contact queries.
/// </summary>
/// <remarks>
/// Instances are caller-owned, retain capacity between calls, and are not thread-safe.
/// </remarks>
public sealed class GridContactQueryScratch
{
    internal SwiftList<Voxel> SourceVoxels { get; }

    internal SwiftList<Voxel> CandidateVoxels { get; }

    internal SwiftHashSet<Voxel> ProcessedVoxels { get; }

    /// <summary>
    /// Initializes scratch storage with optional expected source and per-source candidate counts.
    /// </summary>
    public GridContactQueryScratch(int sourceCapacity = 0, int candidateCapacity = 0)
    {
        SwiftThrowHelper.ThrowIfNegative(sourceCapacity, nameof(sourceCapacity));
        SwiftThrowHelper.ThrowIfNegative(candidateCapacity, nameof(candidateCapacity));

        SourceVoxels = new SwiftList<Voxel>(sourceCapacity);
        CandidateVoxels = new SwiftList<Voxel>(candidateCapacity);
        ProcessedVoxels = new SwiftHashSet<Voxel>(candidateCapacity);
    }

    /// <summary>
    /// Clears retained references without releasing reusable capacity.
    /// </summary>
    public void Clear()
    {
        SourceVoxels.Clear();
        CandidateVoxels.Clear();
        ProcessedVoxels.Clear();
    }
}
