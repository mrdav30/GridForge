//=======================================================================
// NormalizedGridConfiguration.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace GridForge.Configuration;

/// <summary>
/// Describes a validated grid configuration after topology-specific bounds
/// normalization, without registering a live grid.
/// </summary>
public readonly struct NormalizedGridConfiguration
{
    internal IGridTopology? Topology { get; }

    internal GridDimensions Dimensions { get; }

    /// <summary>
    /// The normalized configuration. Its bounds are topology-aligned, while
    /// scan-cell and storage settings retain the caller's requested values.
    /// </summary>
    public GridConfiguration Configuration { get; }

    /// <summary>
    /// The exact normalized bounds-and-topology key used to bind equivalent grids.
    /// Storage kind and scan-cell size are deliberately excluded from this identity.
    /// </summary>
    public GridConfigurationKey Key { get; }

    /// <summary>
    /// The number of valid topology-local X or axial-Q addresses.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// The number of valid topology-local vertical layers.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// The number of valid topology-local Z or axial-R addresses.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// The total number of topology-local addresses in the normalized grid.
    /// Sparse storage may materialize only a subset of these addresses.
    /// </summary>
    public int AddressCount { get; }

    /// <summary>
    /// Indicates whether this descriptor contains a valid normalized address space.
    /// </summary>
    public bool IsValid => Width > 0 && Height > 0 && Length > 0;

    internal NormalizedGridConfiguration(
        GridConfiguration configuration,
        IGridTopology topology,
        GridDimensions dimensions)
    {
        Topology = topology;
        Dimensions = dimensions;
        Configuration = configuration;
        Key = configuration.ToGridKey();
        Width = dimensions.Width;
        Height = dimensions.Height;
        Length = dimensions.Length;
        AddressCount = checked(dimensions.Width * dimensions.Height * dimensions.Length);
    }

    /// <summary>
    /// Determines whether an index belongs to this topology-local address space.
    /// Validation is independent of sparse physical-voxel presence.
    /// </summary>
    /// <remarks>
    /// Validation uses coordinate values only; <see cref="VoxelIndex.IsAllocated"/>
    /// does not change whether the address is in range.
    /// </remarks>
    /// <param name="index">The topology-local index to validate.</param>
    /// <returns>True when each coordinate is inside the normalized dimensions.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValidIndex(VoxelIndex index) =>
        (uint)index.x < (uint)Width
        && (uint)index.y < (uint)Height
        && (uint)index.z < (uint)Length;
}
