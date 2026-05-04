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

            Exception exception = Record.Exception(() => GridForgeLogger.Channel.Info($"write failure fallback"));

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

            GridForgeLogger.Channel.Info($"persist me", method: "WriteFile", filePath: "/tmp/FileLogger.cs");

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

            GridForgeLogger.Channel.Error($"boom", method: "FakeMethod", filePath: "/tmp/FakeClass.cs");

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
    public void ErrorInterpolated_ShouldEmitExpectedDiagnostic()
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

            GridForgeLogger.Channel.Error(
                $"boom with detail {"exploded"}",
                method: "ThrowingMethod",
                filePath: "/tmp/ErrorLogger.cs");

            Assert.NotNull(capturedMessage);
            Assert.Equal("boom with detail exploded", capturedMessage);
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
    public void LogInterpolated_ShouldNotEvaluateFormattedExpressions_WhenLevelIsDisabled()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;
        int evaluationCount = 0;
        int callCount = 0;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Error;
            GridForgeLogger.LogHandler = (level, message, source) =>
            {
                _ = level;
                _ = message;
                _ = source;
                callCount++;
            };

            GridForgeLogger.Channel.Log(DiagnosticLevel.Info, $"expensive {Evaluate()}");

            Assert.Equal(0, evaluationCount);
            Assert.Equal(0, callCount);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }

        string Evaluate()
        {
            evaluationCount++;
            return "value";
        }
    }

    [Fact]
    public void InfoInterpolated_ShouldNotEvaluateFormattedExpressions_WhenLevelIsDisabled()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;
        int evaluationCount = 0;
        int callCount = 0;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Warning;
            GridForgeLogger.LogHandler = (level, message, source) =>
            {
                _ = level;
                _ = message;
                _ = source;
                callCount++;
            };

            GridForgeLogger.Channel.Info($"expensive {Evaluate()}");

            Assert.Equal(0, evaluationCount);
            Assert.Equal(0, callCount);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }

        string Evaluate()
        {
            evaluationCount++;
            return "value";
        }
    }

    [Fact]
    public void DiagnosticChannelInfo_ShouldUseProvidedChannelForEnablement()
    {
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;

            DiagnosticChannel channel = new("Standalone")
            {
                MinimumLevel = DiagnosticLevel.Warning,
                Sink = (in DiagnosticEvent _) => throw new InvalidOperationException("Disabled diagnostics should not emit.")
            };

            int evaluationCount = 0;

            channel.Info($"standalone {Evaluate()}");

            Assert.Equal(0, evaluationCount);

            string Evaluate()
            {
                evaluationCount++;
                return "value";
            }
        }
        finally
        {
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }

    [Fact]
    public void DiagnosticChannelWarn_ShouldEmitThroughProvidedChannel()
    {
        DiagnosticLevel? capturedLevel = null;
        string capturedMessage = null;
        string capturedSource = null;

        DiagnosticChannel channel = new("Standalone")
        {
            MinimumLevel = DiagnosticLevel.Info,
            Sink = (in DiagnosticEvent diagnostic) =>
            {
                capturedLevel = diagnostic.Level;
                capturedMessage = diagnostic.Message;
                capturedSource = diagnostic.Source;
            }
        };

        channel.Warn(
            $"standalone value {42}",
            method: "StandaloneMethod",
            filePath: "/tmp/StandaloneLogger.cs");

        Assert.Equal(DiagnosticLevel.Warning, capturedLevel);
        Assert.Equal("standalone value 42", capturedMessage);
        Assert.Equal("StandaloneLogger.StandaloneMethod", capturedSource);
    }

    [Fact]
    public void LogInterpolated_ShouldEmitExpectedDiagnostic_WhenLevelIsEnabled()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;
        DiagnosticLevel? capturedLevel = null;
        string capturedMessage = null;
        string capturedSource = null;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;
            GridForgeLogger.LogHandler = (level, message, source) =>
            {
                capturedLevel = level;
                capturedMessage = message;
                capturedSource = source;
            };

            GridForgeLogger.Channel.Log(
                DiagnosticLevel.Warning,
                $"enabled value {42}",
                method: "InterpolatedMethod",
                filePath: "/tmp/InterpolatedLogger.cs");

            Assert.Equal(DiagnosticLevel.Warning, capturedLevel);
            Assert.Equal("enabled value 42", capturedMessage);
            Assert.Equal("InterpolatedLogger.InterpolatedMethod", capturedSource);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }

    [Fact]
    public void WarnInterpolated_ShouldEmitExpectedDiagnostic()
    {
        Action<DiagnosticLevel, string, string> originalHandler = GridForgeLogger.LogHandler;
        DiagnosticLevel originalMinimumLevel = GridForgeLogger.MinimumLevel;
        DiagnosticLevel? capturedLevel = null;
        string capturedMessage = null;
        string capturedSource = null;

        try
        {
            GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;
            GridForgeLogger.LogHandler = (level, message, source) =>
            {
                capturedLevel = level;
                capturedMessage = message;
                capturedSource = source;
            };

            GridForgeLogger.Channel.Warn($"plain warning", method: "StringMethod", filePath: "/tmp/StringLogger.cs");

            Assert.Equal(DiagnosticLevel.Warning, capturedLevel);
            Assert.Equal("plain warning", capturedMessage);
            Assert.Equal("StringLogger.StringMethod", capturedSource);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
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

            GridForgeLogger.Channel.Info($"info");
            GridForgeLogger.Channel.Warn($"warn");
            GridForgeLogger.Channel.Error($"error");

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

            GridForgeLogger.Channel.Info($"info");
            GridForgeLogger.Channel.Warn($"warn");
            GridForgeLogger.Channel.Error($"error");

            Assert.Equal(0, callCount);
        }
        finally
        {
            GridForgeLogger.LogHandler = originalHandler;
            GridForgeLogger.MinimumLevel = originalMinimumLevel;
        }
    }
}
