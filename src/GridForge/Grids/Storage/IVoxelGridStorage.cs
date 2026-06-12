//=======================================================================
// IVoxelGridStorage.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;
using System.Collections.Generic;

namespace GridForge.Grids.Storage;

internal interface IVoxelGridStorage
{
    GridStorageKind Kind { get; }

    int ConfiguredVoxelCount { get; }

    SwiftSparseMap<ScanCell>? ScanCells { get; }

    void Reset(VoxelGrid grid);

    bool TryGetVoxel(int x, int y, int z, out Voxel? result);

    bool TryGetScanCell(int key, out ScanCell? result);

    IEnumerable<Voxel> EnumerateVoxels();

    void InvalidateBoundaryVoxels(
        int xStart,
        int xEnd,
        int yStart,
        int yEnd,
        int zStart,
        int zEnd);
}
