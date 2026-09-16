//=======================================================================
// GridTraceIntervalStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids;

/// <summary>
/// Reports completion, a deterministic trace ceiling, geometry failure, or an explicit partial stop.
/// </summary>
public enum GridTraceIntervalStatus : byte
{
    /// <summary>The complete trace was written.</summary>
    Complete,
    /// <summary>The candidate-address ceiling was exhausted.</summary>
    AddressCandidateLimitExceeded,
    /// <summary>The output interval ceiling was exceeded.</summary>
    OutputLimitExceeded,
    /// <summary>A candidate grid cell could not be represented exactly.</summary>
    UnrepresentableGeometry,
    /// <summary>The candidate-grid ceiling was exhausted.</summary>
    GridCandidateLimitExceeded,
    /// <summary>The combined candidate-grid and candidate-address work ceiling was exhausted.</summary>
    CandidateWorkLimitExceeded,
    /// <summary>The caller stopped after a complete slab; retained intervals are not canonical or tied.</summary>
    StoppedAfterCompleteSlab
}
