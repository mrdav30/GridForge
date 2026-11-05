using System;
using FixedMathSharp;
using GridForge.Spatial;

namespace GridForge.Grids.Tests;

public class TestOccupant : IVoxelOccupant
{
    public Guid GlobalId { get; private set; } = Guid.NewGuid();

    public byte OccupantGroupId { get; private set; }

    public Vector3d Position { get; set; }

    public TestOccupant(Vector3d position, byte clusterKey = byte.MaxValue)
    {
        Position = position;
        OccupantGroupId = clusterKey;
    }
}
