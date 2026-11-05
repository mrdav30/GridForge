//=======================================================================
// GridNavigationCorridorValidationStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Topology;

/// <summary>
/// Describes the state of a resumable navigation-corridor validation.
/// </summary>
public enum GridNavigationCorridorValidationStatus : byte
{
    /// <summary>The input or corridor geometry is invalid.</summary>
    Invalid = 0,

    /// <summary>More bounded work is required.</summary>
    InProgress = 1,

    /// <summary>The corridor certificate is complete.</summary>
    Complete = 2,

    /// <summary>The canonical geometric cost is not representable.</summary>
    CostOverflow = 3
}
