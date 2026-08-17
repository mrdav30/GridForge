//=======================================================================
// GridNavigationBodySegmentEndpointAllowance.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Topology;

/// <summary>Identifies one footprint boundary that a swept segment may cross.</summary>
public enum GridNavigationBodySegmentEndpointAllowance : byte
{
    /// <summary>Both segment endpoints must lie within the prism.</summary>
    None,
    /// <summary>The segment must enter through one exact footprint edge at its start.</summary>
    StartFootprintEdge,
    /// <summary>The segment must leave through one exact footprint edge at its end.</summary>
    EndFootprintEdge
}
