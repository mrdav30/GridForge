//=======================================================================
// GridBoundaryContactCursorStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Topology;

/// <summary>
/// Describes the state of a resumable exact boundary-contact query.
/// </summary>
public enum GridBoundaryContactCursorStatus : byte
{
    /// <summary>The bound world or grid generation changed and all prior output must be discarded.</summary>
    Stale = 0,

    /// <summary>More bounded candidate work is required.</summary>
    More = 1,

    /// <summary>All bound spatial pairs and topology addresses were examined.</summary>
    Complete = 2
}
