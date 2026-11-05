//=======================================================================
// GridNavigationBodyTraceCellRole.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids;

/// <summary>Describes how one published trace cell participates in coverage validation.</summary>
public enum GridNavigationBodyTraceCellRole : byte
{
    /// <summary>The cell identity contributes directly to the required physical coverage.</summary>
    RequiredCoverage,
    /// <summary>
    /// The missing cell is an OR alternative whose mutation invalidates the negative proof without
    /// making the cell an additional physical requirement.
    /// </summary>
    PhysicalAlternativeDependency
}
