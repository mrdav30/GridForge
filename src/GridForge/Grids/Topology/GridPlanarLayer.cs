//=======================================================================
// GridPlanarLayer.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace GridForge.Grids.Topology;

/// <summary>Identifies one selected planar layer in an exact grid generation.</summary>
public readonly struct GridPlanarLayer
{
    /// <summary>The exact eligible generation and committed revision.</summary>
    public GridCoveredAddressGeneration Generation { get; }

    /// <summary>The nonnegative topology-local layer index.</summary>
    public int LayerIndex { get; }

    /// <summary>Creates one explicit layer selection.</summary>
    public GridPlanarLayer(GridCoveredAddressGeneration generation, int layerIndex)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(generation.GridSpawnToken <= 0,
            actualValue: null, paramName: nameof(generation));
        SwiftThrowHelper.ThrowIfNegative(layerIndex, nameof(layerIndex));
        Generation = generation;
        LayerIndex = layerIndex;
    }
}
