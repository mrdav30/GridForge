//=======================================================================
// GridNavigationBodyTraceStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids;

/// <summary>Reports the outcome of a bounded swept navigation-body trace.</summary>
public enum GridNavigationBodyTraceStatus : byte
{
    /// <summary>The complete physical prism union was written.</summary>
    Complete,
    /// <summary>The complete canonical set was written, but at least one required cell is physically absent.</summary>
    IncompletePhysicalCoverage,
    /// <summary>The inputs or an exact topology prism could not be represented.</summary>
    InvalidOrUnrepresentableGeometry,
    /// <summary>The candidate-address ceiling was exhausted.</summary>
    AddressLimitExceeded,
    /// <summary>The complete required and dependency-evidence output ceiling was exhausted.</summary>
    OutputLimitExceeded,
    /// <summary>The combined grid and address work ceiling was exhausted.</summary>
    CandidateWorkLimitExceeded,
    /// <summary>The candidate-grid ceiling was exhausted.</summary>
    GridCandidateLimitExceeded,
    /// <summary>Checked body-top or query-bounds arithmetic overflowed.</summary>
    ArithmeticOverflow
}
