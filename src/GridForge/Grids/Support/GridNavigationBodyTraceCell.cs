//=======================================================================
// GridNavigationBodyTraceCell.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;
using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>Identifies one topology cell and its physical generation evidence.</summary>
public readonly struct GridNavigationBodyTraceCell
{
    /// <summary>The exact world, grid-generation, and topology-local address.</summary>
    public WorldVoxelIndex Cell { get; }

    /// <summary>The normalized grid binding key, independent of its recyclable runtime slot.</summary>
    public GridConfigurationKey ConfigurationKey { get; }

    /// <summary>Whether physical storage contained this address during the trace.</summary>
    public bool IsPhysicallyPresent { get; }

    /// <summary>The last committed sequence applied to the owning grid generation.</summary>
    public ulong GridLastChangeSequence { get; }

    /// <summary>How this identity participates in the physical coverage proof.</summary>
    public GridNavigationBodyTraceCellRole Role { get; }

    internal GridNavigationBodyTraceCell(
        WorldVoxelIndex cell,
        GridConfigurationKey configurationKey,
        bool isPhysicallyPresent,
        ulong gridLastChangeSequence,
        GridNavigationBodyTraceCellRole role)
    {
        Cell = cell;
        ConfigurationKey = configurationKey;
        IsPhysicallyPresent = isPhysicallyPresent;
        GridLastChangeSequence = gridLastChangeSequence;
        Role = role;
    }
}
