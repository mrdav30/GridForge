using FixedMathSharp;
using GridForge.Configuration;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class GridWorldTests
{
    [Fact]
    public void TryAddGrid_ShouldNormalizeBoundsUsingOwningWorldVoxelSize()
    {
        GridConfiguration rawConfiguration = new(
            new Vector3d(-1.25, 0, -1.25),
            new Vector3d(1.25, 0, 1.25),
            scanCellSize: 4);

        Assert.Equal(new Vector3d(-1.25, 0, -1.25), rawConfiguration.BoundsMin);
        Assert.Equal(new Vector3d(1.25, 0, 1.25), rawConfiguration.BoundsMax);

        using GridWorld world = new((Fixed64)0.5, spatialGridCellSize: 32);

        Assert.True(world.TryAddGrid(rawConfiguration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.Equal(new Vector3d(-1, 0, -1), grid.BoundsMin);
        Assert.Equal(new Vector3d(1.5, 0, 1.5), grid.BoundsMax);
        Assert.Equal(rawConfiguration.ScanCellSize, grid.Configuration.ScanCellSize);
    }
}
