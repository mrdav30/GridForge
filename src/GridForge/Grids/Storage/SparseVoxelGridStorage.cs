//=======================================================================
// SparseVoxelGridStorage.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Storage;

internal sealed class SparseVoxelGridStorage : IVoxelGridStorage
{
    public GridStorageKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GridStorageKind.Sparse;
    }

    public int ConfiguredVoxelCount { get; private set; }

    public SwiftSparseMap<ScanCell>? ScanCells { get; private set; }

    private SwiftSparseMap<SparseVoxelBlock>? _blocks;
    private Voxel[]? _voxels;
    private int _scanCellSize;
    private int _scanWidth;
    private int _scanHeight;
    private int _scanLength;
    private int _scanLayerSize;

    public void Initialize(VoxelGrid grid, VoxelIndex[] configuredVoxels)
    {
        _scanCellSize = grid.ScanCellSize;
        _scanWidth = grid.ScanWidth;
        _scanHeight = grid.ScanHeight;
        _scanLength = grid.ScanLength;
        _scanLayerSize = grid.ScanWidth * grid.ScanHeight;

        ConfiguredVoxelCount = configuredVoxels.Length;
        if (ConfiguredVoxelCount == 0)
            return;

        SwiftDictionary<int, int> blockCapacities = Pools.SparseVoxelBlockCapacityPool.Rent();
        try
        {
            CountConfiguredVoxelsPerBlock(grid, configuredVoxels, blockCapacities);
            ScanCells = Pools.ScanCellMapPool.Rent();
            _blocks = Pools.SparseVoxelBlockMapPool.Rent();
            _voxels = ArrayPool<Voxel>.Shared.Rent(ConfiguredVoxelCount);

            for (int i = 0; i < configuredVoxels.Length; i++)
            {
                VoxelIndex index = configuredVoxels[i];
                int cellKey = grid.GetScanCellKey(index);

                if (!_blocks.TryGetValue(cellKey, out SparseVoxelBlock? block))
                {
                    block = Pools.SparseVoxelBlockPool.Rent();
                    block.Initialize(grid, cellKey, blockCapacities[cellKey]);
                    _blocks.Add(cellKey, block);
                    ScanCells.Add(cellKey, block.ScanCell!);
                }

                _voxels[i] = block.AddPreparedVoxel(grid, index);
            }
        }
        finally
        {
            Pools.SparseVoxelBlockCapacityPool.Release(blockCapacities);
        }
    }

    public void Reset(VoxelGrid grid)
    {
        ReleaseBlocks(grid);
        ReleaseScanCells();
        ReleaseVoxelCache();

        ConfiguredVoxelCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetVoxel(int x, int y, int z, out Voxel? result)
    {
        result = null;

        if (_blocks == null)
            return false;

        VoxelIndex index = new(x, y, z);
        int cellKey = GetScanCellKey(x, y, z);
        return cellKey >= 0
            && _blocks.TryGetValue(cellKey, out SparseVoxelBlock? block)
            && block.TryGetVoxel(index, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetScanCell(int key, out ScanCell? result)
    {
        result = null;
        return ScanCells?.TryGetValue(key, out result) == true;
    }

    public IEnumerable<Voxel> EnumerateVoxels()
    {
        if (_voxels == null)
            yield break;

        for (int i = 0; i < ConfiguredVoxelCount; i++)
            yield return _voxels[i];
    }

    public bool TryAddVoxel(VoxelGrid grid, VoxelIndex index, out Voxel? voxel)
    {
        voxel = null;
        int cellKey = grid.GetScanCellKey(index);
        if (cellKey < 0)
            return false;

        EnsureStorageMaps();

        bool createdBlock = false;
        if (!_blocks!.TryGetValue(cellKey, out SparseVoxelBlock? block))
        {
            block = Pools.SparseVoxelBlockPool.Rent();
            block.Initialize(grid, cellKey, capacity: 1);
            _blocks.Add(cellKey, block);
            ScanCells!.Add(cellKey, block.ScanCell!);
            createdBlock = true;
        }

        if (!block!.TryAddVoxel(grid, index, out voxel))
        {
            if (createdBlock)
                ReleaseBlock(grid, cellKey, block);
            return false;
        }

        AddVoxelToCache(voxel!);
        ConfiguredVoxelCount++;
        return true;
    }

    public bool TryRemoveVoxel(VoxelGrid grid, VoxelIndex index, out Voxel? voxel)
    {
        voxel = null;
        if (_blocks == null)
            return false;

        int cellKey = grid.GetScanCellKey(index);
        if (cellKey < 0
            || !_blocks.TryGetValue(cellKey, out SparseVoxelBlock? block)
            || !block!.TryRemoveVoxel(grid, index, out voxel))
        {
            return false;
        }

        RemoveVoxelFromCache(index);
        ConfiguredVoxelCount--;

        if (block.Count == 0)
            ReleaseBlock(grid, cellKey, block);

        ReleaseEmptyStorageMapsIfNeeded();
        return true;
    }

    public void AddVoxelsInIndexRange(
        VoxelIndex min,
        VoxelIndex max,
        SwiftList<Voxel> results,
        SwiftHashSet<Voxel> redundancy)
    {
        if (_blocks == null || _scanCellSize <= 0)
            return;

        int scanXMin = min.x / _scanCellSize;
        int scanYMin = min.y / _scanCellSize;
        int scanZMin = min.z / _scanCellSize;
        int scanXMax = max.x / _scanCellSize;
        int scanYMax = max.y / _scanCellSize;
        int scanZMax = max.z / _scanCellSize;

        for (int scanX = scanXMin; scanX <= scanXMax; scanX++)
        {
            for (int scanY = scanYMin; scanY <= scanYMax; scanY++)
            {
                for (int scanZ = scanZMin; scanZ <= scanZMax; scanZ++)
                {
                    int cellKey = GetScanCellKeyFromScanCoordinates(scanX, scanY, scanZ);
                    if (cellKey >= 0 && _blocks.TryGetValue(cellKey, out SparseVoxelBlock? block))
                        block.AddVoxelsInIndexRange(min, max, results, redundancy);
                }
            }
        }
    }

    public void AddScanCellsInRange(
        VoxelGrid _,
        int xMin,
        int yMin,
        int zMin,
        int xMax,
        int yMax,
        int zMax,
        SwiftList<ScanCell> results,
        SwiftHashSet<ScanCell> redundancy)
    {
        if (_blocks == null)
            return;

        for (int x = xMin; x <= xMax; x++)
        {
            for (int y = yMin; y <= yMax; y++)
            {
                for (int z = zMin; z <= zMax; z++)
                {
                    int cellKey = GetScanCellKeyFromScanCoordinates(x, y, z);
                    if (cellKey >= 0
                        && _blocks.TryGetValue(cellKey, out SparseVoxelBlock? block)
                        && block.ScanCell != null
                        && redundancy.Add(block.ScanCell))
                    {
                        results.Add(block.ScanCell);
                    }
                }
            }
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

        for (int i = 0; i < ConfiguredVoxelCount; i++)
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

    private static void CountConfiguredVoxelsPerBlock(
        VoxelGrid grid,
        VoxelIndex[] configuredVoxels,
        SwiftDictionary<int, int> result)
    {
        result.EnsureCapacity(configuredVoxels.Length);
        for (int i = 0; i < configuredVoxels.Length; i++)
        {
            int key = grid.GetScanCellKey(configuredVoxels[i]);
            if (result.TryGetValue(key, out int count))
                result[key] = count + 1;
            else
                result.Add(key, 1);
        }
    }

    private void EnsureStorageMaps()
    {
        ScanCells ??= Pools.ScanCellMapPool.Rent();
        _blocks ??= Pools.SparseVoxelBlockMapPool.Rent();
    }

    private void AddVoxelToCache(Voxel voxel)
    {
        EnsureVoxelCacheCapacity(ConfiguredVoxelCount + 1);
        TryFindVoxelCacheIndex(voxel.Index, out int insertIndex);

        if (insertIndex < ConfiguredVoxelCount)
            Array.Copy(_voxels!, insertIndex, _voxels!, insertIndex + 1, ConfiguredVoxelCount - insertIndex);

        _voxels![insertIndex] = voxel;
    }

    private void RemoveVoxelFromCache(VoxelIndex index)
    {
        if (_voxels == null || !TryFindVoxelCacheIndex(index, out int voxelArrayIndex))
            return;

        int moveCount = ConfiguredVoxelCount - voxelArrayIndex - 1;
        if (moveCount > 0)
            Array.Copy(_voxels, voxelArrayIndex + 1, _voxels, voxelArrayIndex, moveCount);

        _voxels[ConfiguredVoxelCount - 1] = null!;
        if (ConfiguredVoxelCount == 1)
        {
            ArrayPool<Voxel>.Shared.Return(_voxels, clearArray: true);
            _voxels = null;
        }
    }

    private void EnsureVoxelCacheCapacity(int minCapacity)
    {
        if (_voxels != null && _voxels.Length >= minCapacity)
            return;

        int capacity = _voxels == null
            ? minCapacity
            : Math.Max(minCapacity, _voxels.Length << 1);
        Voxel[] replacement = ArrayPool<Voxel>.Shared.Rent(capacity);

        if (_voxels != null)
        {
            Array.Copy(_voxels, replacement, ConfiguredVoxelCount);
            ArrayPool<Voxel>.Shared.Return(_voxels, clearArray: true);
        }

        _voxels = replacement;
    }

    private bool TryFindVoxelCacheIndex(VoxelIndex index, out int voxelArrayIndex)
    {
        voxelArrayIndex = 0;
        if (_voxels == null)
            return false;

        int min = 0;
        int max = ConfiguredVoxelCount - 1;
        while (min <= max)
        {
            int mid = min + ((max - min) >> 1);
            int compare = CompareVoxelIndices(_voxels[mid].Index, index);
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

    private void ReleaseBlock(VoxelGrid grid, int cellKey, SparseVoxelBlock block)
    {
        ScanCells?.Remove(cellKey);
        _blocks?.Remove(cellKey);
        block.Reset(grid);
        Pools.SparseVoxelBlockPool.Release(block);
    }

    private void ReleaseEmptyStorageMapsIfNeeded()
    {
        if (ConfiguredVoxelCount != 0)
            return;

        if (_blocks != null)
        {
            Pools.SparseVoxelBlockMapPool.Release(_blocks);
            _blocks = null;
        }

        if (ScanCells != null)
        {
            Pools.ScanCellMapPool.Release(ScanCells);
            ScanCells = null;
        }
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
    private int GetScanCellKey(int x, int y, int z)
    {
        int scanX = x / _scanCellSize;
        int scanY = y / _scanCellSize;
        int scanZ = z / _scanCellSize;

        return GetScanCellKeyFromScanCoordinates(scanX, scanY, scanZ);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetScanCellKeyFromScanCoordinates(int scanX, int scanY, int scanZ)
    {
        if ((uint)scanX >= (uint)_scanWidth
            || (uint)scanY >= (uint)_scanHeight
            || (uint)scanZ >= (uint)_scanLength)
        {
            return -1;
        }

        return scanX + scanY * _scanWidth + scanZ * _scanLayerSize;
    }

    private void ReleaseBlocks(VoxelGrid grid)
    {
        if (_blocks == null)
            return;

        Span<SparseVoxelBlock> blocks = _blocks.Values;
        for (int i = 0; i < blocks.Length; i++)
        {
            SparseVoxelBlock block = blocks[i];
            block.Reset(grid);
            Pools.SparseVoxelBlockPool.Release(block);
        }

        Pools.SparseVoxelBlockMapPool.Release(_blocks);
        _blocks = null;
    }

    private void ReleaseScanCells()
    {
        if (ScanCells == null)
            return;

        Pools.ScanCellMapPool.Release(ScanCells);
        ScanCells = null;
    }

    private void ReleaseVoxelCache()
    {
        if (_voxels != null)
            ArrayPool<Voxel>.Shared.Return(_voxels, clearArray: true);

        _voxels = null;
        _scanCellSize = 0;
        _scanWidth = 0;
        _scanHeight = 0;
        _scanLength = 0;
        _scanLayerSize = 0;
    }
}
