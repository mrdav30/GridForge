//=======================================================================
// GridPlanarLayer.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids.Topology;

/// <summary>Identifies one selected planar layer in an exact grid generation.</summary>
public readonly struct GridPlanarLayer
{
    /// <summary>The exact eligible generation and committed revision.</summary>
    /// <value>The exact eligible generation and committed revision.</value>
    public GridCoveredAddressGeneration Generation { get; }

    /// <summary>The nonnegative topology-local layer index.</summary>
    /// <value>The nonnegative topology-local layer index.</value>
    public int LayerIndex { get; }

    /// <summary>Creates one explicit layer selection.</summary>
    /// <param name="generation">The exact eligible generation and committed revision.</param>
    /// <param name="layerIndex">The nonnegative topology-local layer index.</param>
    /// <exception cref="System.ArgumentOutOfRangeException">The generation has a nonpositive grid allocation token, or the layer index is negative. The constructor does not verify the upper layer bound.</exception>
    public GridPlanarLayer(GridCoveredAddressGeneration generation, int layerIndex)
    {
        ChronicleGuard.ThrowIfArgumentOutOfRange(generation.GridSpawnToken <= 0,
            actualValue: null, paramName: nameof(generation));
        ChronicleGuard.ThrowIfNegative(layerIndex, nameof(layerIndex));
        Generation = generation;
        LayerIndex = layerIndex;
    }
}
