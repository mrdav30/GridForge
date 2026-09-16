//=======================================================================
// GridTraceInterval.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Describes the exact closed parameter interval where a segment intersects one grid address.
/// </summary>
public readonly struct GridTraceInterval
{
    /// <summary>The exact world, grid-generation, and topology-local address.</summary>
    public WorldVoxelIndex Cell { get; }

    /// <summary>The normalized grid binding key, independent of the recyclable runtime slot.</summary>
    public GridConfigurationKey ConfigurationKey { get; }

    /// <summary>Whether physical storage currently contains the addressed voxel.</summary>
    public bool IsPhysicallyPresent { get; }

    /// <summary>The last committed sequence applied to the traced grid generation.</summary>
    public ulong GridLastChangeSequence { get; }

    /// <summary>The first inclusive segment parameter in the cell prism.</summary>
    public Fixed64 TEnter { get; }

    /// <summary>The last inclusive segment parameter in the cell prism.</summary>
    public Fixed64 TExit { get; }

    /// <summary>
    /// Stable group for peers whose interval interiors overlap, or point peers at one exact parameter.
    /// </summary>
    /// <remarks>
    /// Closed intervals that merely hand off at one endpoint remain successive groups. Group membership
    /// expresses simultaneous geometric coverage only; it does not imply voxel adjacency.
    /// </remarks>
    public int TieGroupId { get; }

    /// <summary>The canonical identity order within <see cref="TieGroupId"/>.</summary>
    public int TieOrder { get; }

    internal GridTraceInterval(
        WorldVoxelIndex cell,
        GridConfigurationKey configurationKey,
        bool isPhysicallyPresent,
        ulong gridLastChangeSequence,
        Fixed64 tEnter,
        Fixed64 tExit,
        int tieGroupId = -1,
        int tieOrder = -1)
    {
        Cell = cell;
        ConfigurationKey = configurationKey;
        IsPhysicallyPresent = isPhysicallyPresent;
        GridLastChangeSequence = gridLastChangeSequence;
        TEnter = tEnter;
        TExit = tExit;
        TieGroupId = tieGroupId;
        TieOrder = tieOrder;
    }

    internal GridTraceInterval WithTie(int tieGroupId, int tieOrder) =>
        new(
            Cell,
            ConfigurationKey,
            IsPhysicallyPresent,
            GridLastChangeSequence,
            TEnter,
            TExit,
            tieGroupId,
            tieOrder);
}
