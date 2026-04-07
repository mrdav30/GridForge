using SwiftCollections.Diagnostics;
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
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        Func<DiagnosticLevel, string, string, string> originalFormatter = GridForgeLogger.CustomFormatter;

        try
        {
            GridForgeLogger.LogHandler = (level, message, source) => { };
            GridForgeLogger.CustomFormatter = (level, message, source) => $"{level}:{source}:{message}";

            GridForgeLogger.LogHandler = null;
            GridForgeLogger.CustomFormatter = null;

            Assert.NotNull(GridForgeLogger.LogHandler);
            Assert.NotNull(GridForgeLogger.CustomFormatter);
            string formatted = GridForgeLogger.CustomFormatter((DiagnosticLevel)99, "message", "Source");

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
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        Func<DiagnosticLevel, string, string, string> originalFormatter = GridForgeLogger.CustomFormatter;
        string originalFilePath = GridForgeLogger.LogFilePath;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;

        try
        {
            GridForgeLogger.LogHandler = null;
            GridForgeLogger.CustomFormatter = null;
            GridForgeLogger.LogFilePath = Path.GetTempPath();
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;

            Exception exception = Record.Exception(() => GridForgeLogger.Info("write failure fallback"));

            Assert.Null(exception);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.CustomFormatter = originalFormatter;
            GridForgeLogger.LogFilePath = originalFilePath;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }

    [Fact]
    public void Info_ShouldWriteToConfiguredFile_WhenPathIsValid()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        Func<DiagnosticLevel, string, string, string> originalFormatter = GridForgeLogger.CustomFormatter;
        string originalFilePath = GridForgeLogger.LogFilePath;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;
        string tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.log");

        try
        {
            GridForgeLogger.LogHandler = null;
            GridForgeLogger.CustomFormatter = null;
            GridForgeLogger.LogFilePath = tempFilePath;
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;

            GridForgeLogger.Info("persist me", method: "WriteFile", filePath: "/tmp/FileLogger.cs");

            string logContents = File.ReadAllText(tempFilePath);

            Assert.Contains("[INFO]", logContents);
            Assert.Contains("[FileLogger.WriteFile]", logContents);
            Assert.Contains("persist me", logContents);
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);

            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.CustomFormatter = originalFormatter;
            GridForgeLogger.LogFilePath = originalFilePath;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }

    [Fact]
    public void Error_ShouldPassComputedSourceToHandler()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        string capturedSource = null;
        DiagnosticLevel? capturedLevel = null;
        string capturedMessage = null;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;
            GridForgeLogger.LogHandler = (level, message, source) =>
            {
                capturedLevel = level;
                capturedMessage = message;
                capturedSource = source;
            };

            GridForgeLogger.Error("boom", method: "FakeMethod", filePath: "/tmp/FakeClass.cs");

            Assert.Equal(DiagnosticLevel.Error, capturedLevel);
            Assert.Equal("boom", capturedMessage);
            Assert.Equal("FakeClass.FakeMethod", capturedSource);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }

    [Fact]
    public void Error_ShouldIncludeExceptionDetails_WhenExceptionProvided()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;
        string capturedMessage = null;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;
            GridForgeLogger.LogHandler = (level, message, source) =>
            {
                _ = level;
                _ = source;
                capturedMessage = message;
            };

            InvalidOperationException exception = null;

            try
            {
                throw new InvalidOperationException("exploded");
            }
            catch (InvalidOperationException ex)
            {
                exception = ex;
            }

            GridForgeLogger.Error("boom", exception, method: "ThrowingMethod", filePath: "/tmp/ErrorLogger.cs");

            Assert.NotNull(capturedMessage);
            Assert.Contains("boom", capturedMessage);
            Assert.Contains("Exception: System.InvalidOperationException: exploded", capturedMessage);
            Assert.Contains("StackTrace:", capturedMessage);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }

    [Fact]
    public void DefaultFormatter_ShouldSupportNoneAndWarningLevels()
    {
        Func<DiagnosticLevel, string, string, string> originalFormatter = GridForgeLogger.CustomFormatter;

        try
        {
            GridForgeLogger.CustomFormatter = null;

            string noneFormatted = GridForgeLogger.CustomFormatter(DiagnosticLevel.None, "quiet", "Logger.Source");
            string warningFormatted = GridForgeLogger.CustomFormatter(DiagnosticLevel.Warning, "heads up", "Logger.Source");

            Assert.Contains("[NONE]", noneFormatted);
            Assert.Contains("[WARN]", warningFormatted);
            Assert.Contains("[Logger.Source]", noneFormatted);
            Assert.Contains("heads up", warningFormatted);
        }
        finally
        {
            GridForgeLogger.CustomFormatter = originalFormatter;
        }
    }

    [Fact]
    public void CustomHandler_ShouldHonorMinimumLevelFiltering()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;
        int callCount = 0;
        DiagnosticLevel? capturedLevel = null;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Error;
            GridForgeLogger.LogHandler = (level, message, source) =>
            {
                _ = message;
                _ = source;
                callCount++;
                capturedLevel = level;
            };

            GridForgeLogger.Info("info");
            GridForgeLogger.Warn("warn");
            GridForgeLogger.Error("error");

            Assert.Equal(1, callCount);
            Assert.Equal(DiagnosticLevel.Error, capturedLevel);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }

    [Fact]
    public void MinimumLevelNone_ShouldDisableLogging()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;
        int callCount = 0;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.None;
            GridForgeLogger.LogHandler = (level, message, source) =>
            {
                _ = level;
                _ = message;
                _ = source;
                callCount++;
            };

            GridForgeLogger.Info("info");
            GridForgeLogger.Warn("warn");
            GridForgeLogger.Error("error");

            Assert.Equal(0, callCount);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }
}
