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

                _voxels[i] = block.AddVoxel(grid, index);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetScanCellKey(int x, int y, int z)
    {
        int scanX = x / _scanCellSize;
        int scanY = y / _scanCellSize;
        int scanZ = z / _scanCellSize;

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
