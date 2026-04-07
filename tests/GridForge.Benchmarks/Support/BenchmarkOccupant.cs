using FixedMathSharp;
using GridForge.Spatial;
using System;

namespace GridForge.Benchmarks;

internal sealed class BenchmarkOccupant : IVoxelOccupant
{
    public Guid GlobalId { get; } = Guid.NewGuid();

    public Vector3d Position { get; }

    public byte OccupantGroupId { get; }

    public BenchmarkOccupant(Vector3d position, byte occupantGroupId)
    {
        Position = position;
        OccupantGroupId = occupantGroupId;
    }
}
