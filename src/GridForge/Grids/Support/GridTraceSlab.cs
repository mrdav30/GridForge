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
    public int XIndex { get; }
    /// <summary>The first newly appended interval.</summary>
    public int IntervalStart { get; }
    /// <summary>The number of newly appended intervals, possibly zero.</summary>
    public int IntervalCount { get; }
    /// <summary>Whether the original endpoints strictly straddle the slab with a one-raw-parameter margin.</summary>
    public bool SeparatesEndpoints { get; }
    /// <summary>The captured world allocation identity.</summary>
    public long WorldSpawnToken { get; }
    /// <summary>The original world change sequence, captured before generation qualification.</summary>
    public ulong WorldChangeSequence { get; }
    /// <summary>The exact qualified grid generation.</summary>
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
