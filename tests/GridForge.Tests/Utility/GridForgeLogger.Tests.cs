using System;
using System.IO;
using Xunit;

namespace GridForge.Utility.Tests;

[Collection("GridForgeCollection")]
public class GridForgeLoggerTests
{
    [Fact]
    public void LogHandlerProperties_ShouldRestoreDefaultsWhenAssignedNull()
    {
        Action<GridForgeLogger.LogLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        Func<GridForgeLogger.LogLevel, string, string, string> originalFormatter = GridForgeLogger.CustomFormatter;

        try
        {
            GridForgeLogger.LogHandler = (level, message, source) => { };
            GridForgeLogger.CustomFormatter = (level, message, source) => $"{level}:{source}:{message}";

            GridForgeLogger.LogHandler = null;
            GridForgeLogger.CustomFormatter = null;

            Assert.NotNull(GridForgeLogger.LogHandler);
            Assert.NotNull(GridForgeLogger.CustomFormatter);
            string formatted = GridForgeLogger.CustomFormatter((GridForgeLogger.LogLevel)99, "message", "Source");

            Assert.Contains("[LOG]", formatted);
            Assert.Contains("[Source]", formatted);
            Assert.Contains("message", formatted);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.CustomFormatter = originalFormatter;
        }
    }

    [Fact]
    public void Info_ShouldNotThrowWhenLogFileWriteFails()
    {
        Action<GridForgeLogger.LogLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        Func<GridForgeLogger.LogLevel, string, string, string> originalFormatter = GridForgeLogger.CustomFormatter;
        string originalFilePath = GridForgeLogger.LogFilePath;
        GridForgeLogger.LogLevel originalVerbosity = GridForgeLogger.Verbosity;

        try
        {
            GridForgeLogger.LogHandler = null;
            GridForgeLogger.CustomFormatter = null;
            GridForgeLogger.LogFilePath = Path.GetTempPath();
            GridForgeLogger.Verbosity = GridForgeLogger.LogLevel.Info;

            Exception exception = Record.Exception(() => GridForgeLogger.Info("write failure fallback"));

            Assert.Null(exception);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.CustomFormatter = originalFormatter;
            GridForgeLogger.LogFilePath = originalFilePath;
            GridForgeLogger.Verbosity = originalVerbosity;
        }
    }
}
