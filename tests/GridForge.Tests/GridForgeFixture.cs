using SwiftCollections.Diagnostics;
using Xunit;

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

[CollectionDefinition("GridForgeCollection")]
public class GridForgeCollection : ICollectionFixture<GridForgeFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
