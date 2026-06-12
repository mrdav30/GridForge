using GridForge.Configuration;

namespace GridForge.Grids.Topology;

internal static class GridTopologyFactory
{
    public static bool TryCreate(GridConfiguration configuration, out IGridTopology? topology)
    {
        topology = null;

        if (configuration.TopologyKind == GridTopologyKind.RectangularPrism)
        {
            topology = new RectangularPrismTopology(configuration.TopologyMetrics);
            return true;
        }

        GridForgeLogger.Channel.Warn($"Grid topology '{configuration.TopologyKind}' is not implemented yet.");
        return false;
    }
}
