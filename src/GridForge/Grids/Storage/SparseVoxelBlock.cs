//=======================================================================
// SparseVoxelBlock.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;
using SwiftCollections;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Storage;

internal sealed class SparseVoxelBlock
{
    public int CellKey { get; private set; } = -1;

    public ScanCell? ScanCell { get; private set; }

    public int Count => _count;

    private Voxel[]? _voxels;
    private int _count;

    public void Initialize(VoxelGrid grid, int cellKey, int capacity)
    {
        CellKey = cellKey;
        ScanCell = Pools.ScanCellPool.Rent();
        ScanCell.Initialize(grid.World!, grid.GridIndex, cellKey);
        _voxels = capacity > 0
            ? ArrayPool<Voxel>.Shared.Rent(capacity)
            : null;
        _count = 0;
    }

    public Voxel AddVoxel(VoxelGrid grid, VoxelIndex index)
    {
        Voxel voxel = Pools.VoxelPool.Rent();
        voxel.Initialize(
            new WorldVoxelIndex(grid.World!.SpawnToken, grid.GridIndex, grid.SpawnToken, index),
            grid.GetWorldPosition(index),
            CellKey,
            grid.IsOnBoundary(index),
            grid.Version);

        _voxels![_count++] = voxel;
        return voxel;
    }

    public bool TryGetVoxel(VoxelIndex index, out Voxel? result)
    {
        result = null;
        if (_voxels == null)
            return false;

        int min = 0;
        int max = _count - 1;
        while (min <= max)
        {
            int mid = min + ((max - min) >> 1);
            Voxel voxel = _voxels[mid];
            int compare = CompareVoxelIndices(voxel.Index, index);

            if (compare == 0)
            {
                result = voxel;
                return voxel.IsAllocated;
            }

            if (compare < 0)
                min = mid + 1;
            else
                max = mid - 1;
        }

        return false;
    }

    public IEnumerable<Voxel> EnumerateVoxels()
    {
        if (_voxels == null)
            yield break;

        for (int i = 0; i < _count; i++)
            yield return _voxels[i];
    }

    public void AddVoxelsInIndexRange(
        VoxelIndex min,
        VoxelIndex max,
        SwiftList<Voxel> results,
        SwiftHashSet<Voxel> redundancy)
    {
        if (_voxels == null)
            return;

        for (int i = 0; i < _count; i++)
        {
            Voxel voxel = _voxels[i];
            VoxelIndex index = voxel.Index;
            if (IsIndexInRange(index, min, max) && redundancy.Add(voxel))
                results.Add(voxel);
        }
    }

    public void InvalidateBoundaryVoxels(
        int xStart,
        int xEnd,
        int yStart,
        int yEnd,
        int zStart,
        int zEnd)
    {
        if (_voxels == null)
            return;

        for (int i = 0; i < _count; i++)
        {
            Voxel voxel = _voxels[i];
            VoxelIndex index = voxel.Index;
            if (index.x >= xStart && index.x <= xEnd
                && index.y >= yStart && index.y <= yEnd
                && index.z >= zStart && index.z <= zEnd)
            {
                voxel.InvalidateNeighborCache();
            }
        }
    }

    public void Reset(VoxelGrid grid)
    {
        if (_voxels != null)
        {
            for (int i = 0; i < _count; i++)
            {
                Voxel voxel = _voxels[i];
                voxel.Reset(grid);
                Pools.VoxelPool.Release(voxel);
            }

            ArrayPool<Voxel>.Shared.Return(_voxels, clearArray: true);
        }

        if (ScanCell != null)
            Pools.ScanCellPool.Release(ScanCell);

        Clear();
    }

    public void Clear()
    {
        CellKey = -1;
        ScanCell = null;
        _voxels = null;
        _count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CompareVoxelIndices(VoxelIndex left, VoxelIndex right)
    {
        int result = left.x.CompareTo(right.x);
        if (result != 0)
            return result;

        result = left.y.CompareTo(right.y);
        return result != 0 ? result : left.z.CompareTo(right.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIndexInRange(VoxelIndex index, VoxelIndex min, VoxelIndex max) =>
        index.x >= min.x && index.x <= max.x
        && index.y >= min.y && index.y <= max.y
        && index.z >= min.z && index.z <= max.z;
}
