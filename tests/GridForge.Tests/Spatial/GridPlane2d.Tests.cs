using FixedMathSharp;
using Xunit;

namespace GridForge.Spatial.Tests;

public class GridPlane2dTests
{
    [Fact]
    public void ToWorld_ShouldMapVector2dToWorldXzWithExplicitLayer()
    {
        Vector2d position = new(3, 5);
        Fixed64 layerY = (Fixed64)7;

        Vector3d result = GridPlane2d.ToWorld(position, layerY);

        Assert.Equal(new Vector3d(3, 7, 5), result);
    }

    [Fact]
    public void ToWorld_ShouldUseZeroLayerWhenLayerIsOmitted()
    {
        Vector2d position = new(3, 5);

        Vector3d result = GridPlane2d.ToWorld(position);

        Assert.Equal(new Vector3d(3, 0, 5), result);
    }

    [Fact]
    public void FromWorld_ShouldDropWorldYAndPreserveWorldXz()
    {
        Vector3d position = new(3, 7, 5);

        Vector2d result = GridPlane2d.FromWorld(position);

        Assert.Equal(new Vector2d(3, 5), result);
    }

    [Fact]
    public void ToWorldBounds_ShouldMapVector2dBoundsToLayerLockedWorldBounds()
    {
        Vector2d boundsMin = new(-2, -3);
        Vector2d boundsMax = new(4, 5);
        Fixed64 layerY = (Fixed64)9;

        (Vector3d min, Vector3d max) = GridPlane2d.ToWorldBounds(boundsMin, boundsMax, layerY);

        Assert.Equal(new Vector3d(-2, 9, -3), min);
        Assert.Equal(new Vector3d(4, 9, 5), max);
    }
}
