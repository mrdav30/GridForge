//=======================================================================
// GridTraceSlab.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Topology;

namespace GridForge.Grids;

/// <summary>Describes one completed X slab of an original rectangular segment trace.</summary>
public readonly struct GridTraceSlab
{
    /// <summary>The topology-local X index.</summary>
    /// <value>The topology-local X index.</value>
    public int XIndex { get; }
    /// <summary>The first newly appended interval.</summary>
    /// <value>The first newly appended interval.</value>
    public int IntervalStart { get; }
    /// <summary>The number of newly appended intervals, possibly zero.</summary>
    /// <value>The number of newly appended intervals, possibly zero.</value>
    public int IntervalCount { get; }
    /// <summary>Whether the original endpoints strictly straddle the slab with a one-raw-parameter margin.</summary>
    /// <value>Whether the original endpoints strictly straddle the slab with a one-raw-parameter margin.</value>
    public bool SeparatesEndpoints { get; }
    /// <summary>The captured world allocation identity.</summary>
    /// <value>The captured world allocation identity.</value>
    public long WorldSpawnToken { get; }
    /// <summary>The original world change sequence, captured before generation qualification.</summary>
    /// <value>The original world change sequence, captured before generation qualification.</value>
    public ulong WorldChangeSequence { get; }
    /// <summary>The exact qualified grid generation.</summary>
    /// <value>The exact qualified grid generation.</value>
    public GridCoveredAddressGeneration Generation { get; }

    internal GridTraceSlab(int xIndex, int intervalStart, int intervalCount, bool separatesEndpoints,
        long worldSpawnToken, ulong worldChangeSequence, GridCoveredAddressGeneration generation)
    {
        XIndex = xIndex;
        IntervalStart = intervalStart;
        IntervalCount = intervalCount;
        SeparatesEndpoints = separatesEndpoints;
        WorldSpawnToken = worldSpawnToken;
        WorldChangeSequence = worldChangeSequence;
        Generation = generation;
    }
}
