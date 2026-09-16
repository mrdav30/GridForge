//=======================================================================
// GridTraceAddressCandidate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace GridForge.Grids;

internal readonly struct GridTraceAddressCandidate
{
    public readonly VoxelGrid Grid;
    public readonly VoxelIndex Index;
    public readonly bool IsPhysicallyPresent;

    public GridTraceAddressCandidate(VoxelGrid grid, VoxelIndex index, bool isPhysicallyPresent)
    {
        Grid = grid;
        Index = index;
        IsPhysicallyPresent = isPhysicallyPresent;
    }

    public GridTraceAddressCandidate WithPhysicalPresence(bool isPhysicallyPresent) =>
        new GridTraceAddressCandidate(Grid, Index, isPhysicallyPresent);
}
