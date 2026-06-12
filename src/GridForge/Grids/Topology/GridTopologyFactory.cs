//=======================================================================
// GridTopologyFactory.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;

namespace GridForge.Grids.Topology;

internal static class GridTopologyFactory
{
    public static bool TryCreate(GridConfiguration configuration, out IGridTopology? topology)
    {
        topology = null;

        switch (configuration.TopologyKind)
        {
            case GridTopologyKind.RectangularPrism:
                {
                    topology = new RectangularPrismTopology(configuration.TopologyMetrics);
                    return true;
                }
            case GridTopologyKind.HexPrism:
                {
                    if (!GridTopologyMetrics.IsValid(configuration.TopologyKind, configuration.TopologyMetrics))
                    {
                        GridForgeLogger.Channel.Warn($"Hex-prism topology requires positive cell radius and layer height.");
                        return false;
                    }

                    topology = new HexPrismTopology(configuration.TopologyMetrics);
                    return true;
                }
            default:
                GridForgeLogger.Channel.Warn($"Grid topology '{configuration.TopologyKind}' is not implemented.");
                return false;
        }
    }
}
