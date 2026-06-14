//=======================================================================
// SparseVoxelBlock.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Buffers;
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

    public Voxel AddPreparedVoxel(VoxelGrid grid, VoxelIndex index)
    {
        // GridWorld validates prepared sparse input as sorted and deduplicated before storage initialization.
        EnsureCapacity(_count + 1);
        Voxel voxel = CreateVoxel(grid, index);
        _voxels![_count++] = voxel;
        return voxel;
    }

    public bool TryAddVoxel(VoxelGrid grid, VoxelIndex index, out Voxel? voxel)
    {
        voxel = null;
        if (TryFindVoxelArrayIndex(index, out int insertIndex))
            return false;

        EnsureCapacity(_count + 1);
        voxel = CreateVoxel(grid, index);

        if (insertIndex < _count)
            Array.Copy(_voxels!, insertIndex, _voxels!, insertIndex + 1, _count - insertIndex);

        _voxels![insertIndex] = voxel;
        _count++;
        return true;
    }

    public bool TryRemoveVoxel(VoxelGrid grid, VoxelIndex index, out Voxel? voxel)
    {
        voxel = null;
        if (!TryFindVoxelArrayIndex(index, out int voxelArrayIndex))
            return false;

        voxel = _voxels![voxelArrayIndex];
        int moveCount = _count - voxelArrayIndex - 1;
        if (moveCount > 0)
            Array.Copy(_voxels, voxelArrayIndex + 1, _voxels, voxelArrayIndex, moveCount);

        _voxels[--_count] = null!;
        voxel.Reset(grid);
        Pools.VoxelPool.Release(voxel);
        return true;
    }

    public bool TryGetVoxel(VoxelIndex index, out Voxel? result)
    {
        result = null;
        if (!TryFindVoxelArrayIndex(index, out int voxelArrayIndex))
            return false;

        result = _voxels![voxelArrayIndex];
        return result.IsAllocated;
    }

    private bool TryFindVoxelArrayIndex(VoxelIndex index, out int voxelArrayIndex)
    {
        voxelArrayIndex = 0;
        if (_voxels == null)
            return false;

        int min = 0;
        int max = _count - 1;
        while (min <= max)
        {
            int mid = min + ((max - min) >> 1);
            Voxel voxel = _voxels[mid];
            int compare = voxel.Index.CompareTo(index);

            if (compare == 0)
            {
                voxelArrayIndex = mid;
                return true;
            }

            if (compare < 0)
                min = mid + 1;
            else
                max = mid - 1;
        }

        voxelArrayIndex = min;
        return false;
    }

    private void EnsureCapacity(int minCapacity)
    {
        if (_voxels != null && _voxels.Length >= minCapacity)
            return;

        int capacity = _voxels == null
            ? minCapacity
            : Math.Max(minCapacity, _voxels.Length << 1);
        Voxel[] replacement = ArrayPool<Voxel>.Shared.Rent(capacity);

        if (_voxels != null)
        {
            Array.Copy(_voxels, replacement, _count);
            ArrayPool<Voxel>.Shared.Return(_voxels, clearArray: true);
        }

        _voxels = replacement;
    }

    private Voxel CreateVoxel(VoxelGrid grid, VoxelIndex index)
    {
        Voxel voxel = Pools.VoxelPool.Rent();
        voxel.Initialize(
            new WorldVoxelIndex(grid.World!.SpawnToken, grid.GridIndex, grid.SpawnToken, index),
            grid.GetWorldPosition(index),
            CellKey,
            grid.IsOnBoundary(index),
            grid.Version);

        return voxel;
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
    private static bool IsIndexInRange(VoxelIndex index, VoxelIndex min, VoxelIndex max) =>
        index.x >= min.x && index.x <= max.x
        && index.y >= min.y && index.y <= max.y
        && index.z >= min.z && index.z <= max.z;
}
