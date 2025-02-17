using System;
using Xunit;

namespace GridForge.Grids.Tests
{
    /// <summary>
    /// Class Fixture for all GridForge tests, ensuring proper setup and teardown.
    /// </summary>
    public class GridForgeFixture : IDisposable
    {
        public GridForgeFixture()
        {
            GridForgeLogger.Verbosity = GridForgeLogger.LogLevel.Error;
            GlobalGridManager.Setup();
        }

        public void Dispose()
        {
            GlobalGridManager.Reset();
        }
    }

    [CollectionDefinition("GridForgeCollection")]
    public class GridForgeCollection : ICollectionFixture<GridForgeFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
