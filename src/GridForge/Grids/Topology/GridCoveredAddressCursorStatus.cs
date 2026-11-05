//=======================================================================
// GridCoveredAddressCursorStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Topology;

/// <summary>Describes the state of a resumable covered-address query.</summary>
public enum GridCoveredAddressCursorStatus : byte
{
    /// <summary>The bound revision or an eligible grid generation changed; discard the run.</summary>
    Stale = 0,

    /// <summary>More generation-input, lookup, address, or output work is required.</summary>
    More = 1,

    /// <summary>Every eligible covered topology address was examined.</summary>
    Complete = 2
}
