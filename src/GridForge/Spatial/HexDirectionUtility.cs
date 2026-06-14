//=======================================================================
// HexDirectionUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;

namespace GridForge.Spatial;

/// <summary>
/// Provides hex-prism neighbor direction utilities.
/// </summary>
public static class HexDirectionUtility
{
    /// <summary>
    /// Hex-prism neighbor offsets in deterministic direction order.
    /// </summary>
    public static readonly VoxelIndex[] Offsets =
    {
        new(1, 0, 0),
        new(1, 0, -1),
        new(0, 0, -1),
        new(-1, 0, 0),
        new(-1, 0, 1),
        new(0, 0, 1),
        new(0, -1, 0),
        new(1, -1, 0),
        new(1, -1, -1),
        new(0, -1, -1),
        new(-1, -1, 0),
        new(-1, -1, 1),
        new(0, -1, 1),
        new(0, 1, 0),
        new(1, 1, 0),
        new(1, 1, -1),
        new(0, 1, -1),
        new(-1, 1, 0),
        new(-1, 1, 1),
        new(0, 1, 1)
    };

    /// <summary>
    /// All 20 hex-prism neighbor directions excluding None.
    /// </summary>
    public static readonly HexDirection[] All =
    {
        HexDirection.QPositive,
        HexDirection.QPositiveRNegative,
        HexDirection.RNegative,
        HexDirection.QNegative,
        HexDirection.QNegativeRPositive,
        HexDirection.RPositive,
        HexDirection.Below,
        HexDirection.BelowQPositive,
        HexDirection.BelowQPositiveRNegative,
        HexDirection.BelowRNegative,
        HexDirection.BelowQNegative,
        HexDirection.BelowQNegativeRPositive,
        HexDirection.BelowRPositive,
        HexDirection.Above,
        HexDirection.AboveQPositive,
        HexDirection.AboveQPositiveRNegative,
        HexDirection.AboveRNegative,
        HexDirection.AboveQNegative,
        HexDirection.AboveQNegativeRPositive,
        HexDirection.AboveRPositive
    };

    /// <summary>
    /// The 8 face-adjacent hex-prism directions.
    /// </summary>
    public static readonly HexDirection[] Primary =
    {
        HexDirection.QPositive,
        HexDirection.QPositiveRNegative,
        HexDirection.RNegative,
        HexDirection.QNegative,
        HexDirection.QNegativeRPositive,
        HexDirection.RPositive,
        HexDirection.Below,
        HexDirection.Above
    };

    /// <summary>
    /// All 6 planar axial hex directions.
    /// </summary>
    public static readonly HexDirection[] Planar =
    {
        HexDirection.QPositive,
        HexDirection.QPositiveRNegative,
        HexDirection.RNegative,
        HexDirection.QNegative,
        HexDirection.QNegativeRPositive,
        HexDirection.RPositive
    };

    /// <summary>
    /// All 2 vertical hex-prism directions.
    /// </summary>
    public static readonly HexDirection[] Vertical =
    {
        HexDirection.Below,
        HexDirection.Above
    };

    /// <summary>
    /// All 7 neighbor directions on the layer below.
    /// </summary>
    public static readonly HexDirection[] BelowLayer =
    {
        HexDirection.Below,
        HexDirection.BelowQPositive,
        HexDirection.BelowQPositiveRNegative,
        HexDirection.BelowRNegative,
        HexDirection.BelowQNegative,
        HexDirection.BelowQNegativeRPositive,
        HexDirection.BelowRPositive
    };

    /// <summary>
    /// All 7 neighbor directions on the layer above.
    /// </summary>
    public static readonly HexDirection[] AboveLayer =
    {
        HexDirection.Above,
        HexDirection.AboveQPositive,
        HexDirection.AboveQPositiveRNegative,
        HexDirection.AboveRNegative,
        HexDirection.AboveQNegative,
        HexDirection.AboveQNegativeRPositive,
        HexDirection.AboveRPositive
    };

    /// <summary>
    /// All 12 non-vertical directions on the layers above and below.
    /// </summary>
    public static readonly HexDirection[] VerticalDiagonal =
    {
        HexDirection.BelowQPositive,
        HexDirection.BelowQPositiveRNegative,
        HexDirection.BelowRNegative,
        HexDirection.BelowQNegative,
        HexDirection.BelowQNegativeRPositive,
        HexDirection.BelowRPositive,
        HexDirection.AboveQPositive,
        HexDirection.AboveQPositiveRNegative,
        HexDirection.AboveRNegative,
        HexDirection.AboveQNegative,
        HexDirection.AboveQNegativeRPositive,
        HexDirection.AboveRPositive
    };

    /// <summary>
    /// Gets the topology-local index offset for a hex direction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoxelIndex GetOffset(HexDirection direction) => Offsets[(int)direction];

    /// <summary>
    /// True for planar axial directions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPlanar(HexDirection direction) =>
        (uint)direction <= (uint)HexDirection.RPositive;

    /// <summary>
    /// True for vertical layer directions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsVertical(HexDirection direction) =>
        direction == HexDirection.Below || direction == HexDirection.Above;
}
