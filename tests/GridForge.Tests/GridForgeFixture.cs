using Chronicler.Diagnostics;

namespace GridForge.Grids.Tests;

/// <summary>
/// Class fixture for all GridForge tests that configures shared logger output.
/// </summary>
public class GridForgeFixture
{
    public GridForgeFixture()
    {
        GridForgeLogger.MinimumLevel = DiagnosticLevel.Error;
    }
}
