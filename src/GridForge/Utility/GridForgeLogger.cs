//=======================================================================
// GridForgeLogger.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

global using SwiftCollections.Diagnostics;

using System;
using System.IO;

namespace GridForge;

/// <summary>
/// Provides a configurable logging system for GridForge with support for log levels, formatting, and file output.
/// </summary>
public static class GridForgeLogger
{
    private static readonly GridForgeDiagnosticLogger _logger = new();

    /// <summary>
    /// Gets the diagnostic channel used by GridForge logging.
    /// </summary>
    public static DiagnosticChannel Channel => _logger.Channel;

    /// <summary>
    /// Gets the diagnostic channel used for verbose debug diagnostics.
    /// </summary>
    public static DiagnosticChannel DebugChannel => _logger.DebugChannel;

    /// <summary>
    /// Gets or sets a value indicating whether verbose debug diagnostics should be emitted.
    /// </summary>
    public static bool EnableDebugLogging
    {
        get => _logger.EnableDebugLogging;
        set => _logger.EnableDebugLogging = value;
    }

    /// <summary>
    /// Gets or sets the delegate used to write formatted log messages.
    /// Assigning <see langword="null"/> restores <see cref="DefaultLogHandler"/>.
    /// </summary>
    public static Action<DiagnosticLevel, string, string> LogHandler
    {
        get => _logger.LogHandler;
        set => _logger.LogHandler = value;
    }

    /// <summary>
    /// Gets or sets the formatter used to transform log arguments into a final log entry.
    /// Assigning <see langword="null"/> restores <see cref="DefaultLogFormatter"/>.
    /// </summary>
    public static Func<DiagnosticLevel, string, string, string> CustomFormatter
    {
        get => _logger.CustomFormatter;
        set => _logger.CustomFormatter = value;
    }

    /// <summary>
    /// Gets or sets the file path for logging. If null, file logging is disabled.
    /// </summary>
    public static string? LogFilePath
    {
        get => _logger.LogFilePath;
        set => _logger.LogFilePath = value;
    }

    /// <summary>
    /// Gets or sets the minimum log level required for messages to be logged.
    /// </summary>
    public static DiagnosticLevel MinimumLevel
    {
        get => _logger.MinimumLevel;
        set => _logger.MinimumLevel = value;
    }

    /// <summary>
    /// Determines whether diagnostics at the specified level are currently enabled.
    /// </summary>
    /// <param name="level">The diagnostic level to evaluate.</param>
    /// <returns><see langword="true"/> when messages at <paramref name="level"/> will be emitted; otherwise, <see langword="false"/>.</returns>
    public static bool IsEnabled(DiagnosticLevel level)
    {
        return _logger.IsEnabled(level);
    }

    /// <summary>
    /// The default handler for logging messages, writing them to the console and optionally to a file.
    /// </summary>
    /// <param name="level">The severity level of the log message.</param>
    /// <param name="message">The log message.</param>
    /// <param name="source">The source of the log message.</param>
    public static void DefaultLogHandler(DiagnosticLevel level, string message, string source)
    {
        _logger.DefaultLogHandler(level, message, source);
    }

    /// <summary>
    /// The default log formatter that formats log messages with timestamp, log level, and source information.
    /// </summary>
    /// <param name="level">The severity level of the log message.</param>
    /// <param name="message">The log message.</param>
    /// <param name="source">The source of the log message.</param>
    /// <returns>A formatted log entry as a string.</returns>
    public static string DefaultLogFormatter(DiagnosticLevel level, string message, string source)
    {
        return _logger.DefaultLogFormatter(level, message, source);
    }

    private sealed class GridForgeDiagnosticLogger : DiagnosticLogger
    {
        private readonly object _lock = new();

        public GridForgeDiagnosticLogger() : base("GridForge") { }

        public string? LogFilePath { get; set; }

        public override void DefaultLogHandler(DiagnosticLevel level, string message, string source)
        {
            lock (_lock)
            {
                string logEntry = CustomFormatter(level, message, source);
                Console.WriteLine(logEntry);

                if (!string.IsNullOrEmpty(LogFilePath))
                {
                    try
                    {
                        File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Failed to write to log file: {ex.Message}");
                    }
                }
            }
        }

        public override string DefaultLogFormatter(DiagnosticLevel level, string message, string source)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelTag = level switch
            {
                DiagnosticLevel.None => "[NONE]",
                DiagnosticLevel.Info => "[INFO]",
                DiagnosticLevel.Warning => "[WARN]",
                DiagnosticLevel.Error => "[ERROR]",
                _ => "[LOG]"
            };

            return $"{timestamp} {levelTag} [{source}] {message}";
        }
    }
}
