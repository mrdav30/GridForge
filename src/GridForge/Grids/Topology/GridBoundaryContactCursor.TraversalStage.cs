//=======================================================================
// GridBoundaryContactCursor.TraversalStage.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Topology;

public sealed partial class GridBoundaryContactCursor
{
    internal enum TraversalStage : byte
    {
        Pair,
        Source,
        Target
    }
}
