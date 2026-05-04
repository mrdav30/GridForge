using SwiftCollections.Diagnostics;
using System;
using System.IO;

namespace GridForge;

/// <summary>
/// Provides a configurable logging system for GridForge with support for log levels, formatting, and file output.
/// </summary>
public static class GridForgeLogger
{
    #region Fields and Properties

    /// <summary>
    /// Delegate for handling log messages. Defaults to <see cref="DefaultLogHandler"/>.
    /// </summary>
    private static Action<DiagnosticLevel, string, string> _logHandler = DefaultLogHandler;

    private static readonly DiagnosticChannel _channel = CreateChannel();

    /// <summary>
    /// Gets the diagnostic channel used by GridForge logging.
    /// </summary>
    public static DiagnosticChannel Channel => _channel;

    /// <summary>
    /// Gets or sets the delegate used to write formatted log messages.
    /// Assigning <see langword="null"/> restores <see cref="DefaultLogHandler"/>.
    /// </summary>
    public static Action<DiagnosticLevel, string, string> LogHandler
    {
        get => _logHandler;
        set => _logHandler = value ?? DefaultLogHandler;
    }

    /// <summary>
    /// Delegate for custom log formatting. Defaults to <see cref="DefaultLogFormatter"/>.
    /// </summary>
    private static Func<DiagnosticLevel, string, string, string> _customFormatter = DefaultLogFormatter;

    /// <summary>
    /// Gets or sets the formatter used to transform log arguments into a final log entry.
    /// Assigning <see langword="null"/> restores <see cref="DefaultLogFormatter"/>.
    /// </summary>
    public static Func<DiagnosticLevel, string, string, string> CustomFormatter
    {
        get => _customFormatter;
        set => _customFormatter = value ?? DefaultLogFormatter;
    }

    /// <summary>
    /// Gets or sets the file path for logging. If null, file logging is disabled.
    /// </summary>
    public static string? LogFilePath { get; set; } = null;

    /// <summary>
    /// Gets or sets the minimum log level required for messages to be logged.
    /// </summary>
    public static DiagnosticLevel MinimumLevel
    {
        get => _channel.MinimumLevel;
        set => _channel.MinimumLevel = value;
    }

    /// <summary>
    /// Synchronization lock for thread-safe logging.
    /// </summary>
    private static readonly object _lock = new();

    #endregion

    #region Logging Methods

    /// <summary>
    /// Determines whether diagnostics at the specified level are currently enabled.
    /// </summary>
    /// <param name="level">The diagnostic level to evaluate.</param>
    /// <returns><see langword="true"/> when messages at <paramref name="level"/> will be emitted; otherwise, <see langword="false"/>.</returns>
    public static bool IsEnabled(DiagnosticLevel level)
    {
        return _channel.IsEnabled(level);
    }

    /// <summary>
    /// The default handler for logging messages, writing them to the console and optionally to a file.
    /// </summary>
    /// <param name="level">The severity level of the log message.</param>
    /// <param name="message">The log message.</param>
    /// <param name="source">The source of the log message (e.g., calling method).</param>
    private static void DefaultLogHandler(DiagnosticLevel level, string message, string source)
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

    /// <summary>
    /// The default log formatter that formats log messages with timestamp, log level, and source information.
    /// </summary>
    /// <param name="level">The severity level of the log message.</param>
    /// <param name="message">The log message.</param>
    /// <param name="source">The source of the log message (e.g., calling method).</param>
    /// <returns>A formatted log entry as a string.</returns>
    private static string DefaultLogFormatter(DiagnosticLevel level, string message, string source)
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

    private static DiagnosticChannel CreateChannel()
    {
        DiagnosticChannel channel = new("GridForge")
        {
            MinimumLevel = DiagnosticLevel.Warning,
            Sink = HandleDiagnosticEvent
        };
        return channel;
    }

    private static void HandleDiagnosticEvent(in DiagnosticEvent diagnostic)
    {
        _logHandler(diagnostic.Level, diagnostic.Message, diagnostic.Source);
    }

    #endregion
}
