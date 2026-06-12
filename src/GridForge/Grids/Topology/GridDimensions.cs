namespace GridForge.Grids.Topology;

internal readonly struct GridDimensions
{
    public readonly int Width;
    public readonly int Height;
    public readonly int Length;

    public GridDimensions(int width, int height, int length)
    {
        Width = width;
        Height = height;
        Length = length;
    }
}
