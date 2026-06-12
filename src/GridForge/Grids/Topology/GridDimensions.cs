//=======================================================================
// GridDimensions.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
