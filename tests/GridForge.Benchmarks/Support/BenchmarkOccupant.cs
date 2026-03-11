using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using System;

namespace GridForge.Benchmarks;

internal sealed class BenchmarkOccupant : IVoxelOccupant
{
    public Guid GlobalId { get; } = Guid.NewGuid();

    public Vector3d Position { get; }

    public byte OccupantGroupId { get; }

    public SwiftDictionary<GlobalVoxelIndex, int> OccupyingIndexMap { get; } =
        new SwiftDictionary<GlobalVoxelIndex, int>();

    public BenchmarkOccupant(Vector3d position, byte occupantGroupId)
    {
        Position = position;
        OccupantGroupId = occupantGroupId;
    }

    public void SetOccupancy(GlobalVoxelIndex occupyingIndex, int ticket)
    {
        OccupyingIndexMap[occupyingIndex] = ticket;
    }

    public void RemoveOccupancy(GlobalVoxelIndex occupyingIndex)
    {
        OccupyingIndexMap.Remove(occupyingIndex);
    }
}
