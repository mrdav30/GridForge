//=======================================================================
// DenseVoxelGridStorage.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Dimensions;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Storage;

internal sealed class DenseVoxelGridStorage : IVoxelGridStorage
{
    public GridStorageKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GridStorageKind.Dense;
    }

    public int ConfiguredVoxelCount { get; private set; }

    public SwiftSparseMap<ScanCell>? ScanCells { get; private set; }

    internal SwiftArray3D<Voxel>? Voxels { get; private set; }

    private int _width;
    private int _height;
    private int _length;

    public void Initialize(VoxelGrid grid)
    {
        _width = grid.Width;
        _height = grid.Height;
        _length = grid.Length;
        ConfiguredVoxelCount = grid.Size;

        GenerateScanCells(grid);
        GenerateVoxels(grid);
    }

    public void Reset(VoxelGrid grid)
    {
        ReleaseVoxels(grid);
        ReleaseScanCells();

        ConfiguredVoxelCount = 0;
        _width = 0;
        _height = 0;
        _length = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetVoxel(int x, int y, int z, out Voxel? result)
    {
        result = Voxels![x, y, z];
        return result!.IsAllocated;
    }

    public bool TryGetClosestVoxel(
        VoxelGrid grid,
        VoxelIndex closestIndex,
        Vector3d position,
        out Voxel? result,
        out Fixed64 distanceSquared)
    {
        result = Voxels![closestIndex.x, closestIndex.y, closestIndex.z];
        distanceSquared = (result!.WorldPosition - position).MagnitudeSquared;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetScanCell(int key, out ScanCell? result)
    {
        result = null;
        return ScanCells?.TryGetValue(key, out result) == true;
    }

    public IEnumerable<Voxel> EnumerateVoxels()
    {
        if (Voxels == null)
            yield break;

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                for (int z = 0; z < _length; z++)
                {
                    yield return Voxels[x, y, z];
                }
            }
        }
    }

    public void AddVoxelsInIndexRange(
        VoxelIndex min,
        VoxelIndex max,
        SwiftList<Voxel> results,
        SwiftHashSet<Voxel> redundancy)
    {
        if (Voxels == null)
            return;

        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                for (int z = min.z; z <= max.z; z++)
                {
                    Voxel voxel = Voxels[x, y, z];
                    if (redundancy.Add(voxel))
                        results.Add(voxel);
                }
            }
        }
    }

    public void AddScanCellsInRange(
        VoxelGrid grid,
        int xMin,
        int yMin,
        int zMin,
        int xMax,
        int yMax,
        int zMax,
        SwiftList<ScanCell> results,
        SwiftHashSet<ScanCell> redundancy)
    {
        if (ScanCells == null)
            return;

        for (int x = xMin; x <= xMax; x++)
        {
            for (int y = yMin; y <= yMax; y++)
            {
                for (int z = zMin; z <= zMax; z++)
                {
                    int scanCellKey = grid.GetScanCellKey(x, y, z);
                    if (scanCellKey >= 0
                        && ScanCells.TryGetValue(scanCellKey, out ScanCell? scanCell)
                        && redundancy.Add(scanCell))
                    {
                        results.Add(scanCell!);
                    }
                }
            }
        }
    }

    private void GenerateScanCells(VoxelGrid grid)
    {
        ScanCells = Pools.ScanCellMapPool.Rent();

        for (int x = 0; x < grid.ScanWidth; x++)
        {
            for (int y = 0; y < grid.ScanHeight; y++)
            {
                for (int z = 0; z < grid.ScanLength; z++)
                {
                    int cellKey = grid.GetScanCellKey(x, y, z);

                    ScanCell scanCell = Pools.ScanCellPool.Rent();
                    scanCell.Initialize(grid.World!, grid.GridIndex, cellKey);
                    ScanCells.Add(cellKey, scanCell);
                }
            }
        }
    }

    private void GenerateVoxels(VoxelGrid grid)
    {
        Voxels = new SwiftArray3D<Voxel>(_width, _height, _length);

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                for (int z = 0; z < _length; z++)
                {
                    VoxelIndex index = new(x, y, z);
                    Vector3d position = grid.GetWorldPosition(index);
                    Voxel voxel = Pools.VoxelPool.Rent();

                    voxel.Initialize(
                        new WorldVoxelIndex(grid.World!.SpawnToken, grid.GridIndex, grid.SpawnToken, index),
                        position,
                        grid.GetScanCellKey(index),
                        grid.IsOnBoundary(index),
                        grid.Version);

                    Voxels[x, y, z] = voxel;
                }
            }
        }
    }

    private void ReleaseVoxels(VoxelGrid grid)
    {
        if (Voxels == null)
            return;

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                for (int z = 0; z < _length; z++)
                {
                    Voxel voxel = Voxels[x, y, z];
                    voxel.Reset(grid);
                    Pools.VoxelPool.Release(voxel);
                }
            }
        }

        Voxels = null;
    }

    private void ReleaseScanCells()
    {
        if (ScanCells == null)
            return;

        foreach (ScanCell cell in ScanCells.Values)
            Pools.ScanCellPool.Release(cell);

        Pools.ScanCellMapPool.Release(ScanCells);
        ScanCells = null;
    }
}
