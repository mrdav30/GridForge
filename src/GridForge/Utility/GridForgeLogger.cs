using System;
using System.IO;
using System.Runtime.CompilerServices;

/// <summary>
/// Provides a configurable logging system for GridForge with support for log levels, formatting, and file output.
/// </summary>
public static class GridForgeLogger
{
    /// <summary>
    /// Represents the severity level of a log message.
    /// </summary>
    public enum LogLevel
    {
#pragma warning disable 1591
        Info,
        Warning,
        Error
#pragma warning restore 1591
    }

    /// <summary>
    /// Delegate for handling log messages. Defaults to <see cref="DefaultLogHandler"/>.
    /// </summary>
    public static Action<LogLevel, string, string> LogHandler = DefaultLogHandler;

    /// <summary>
    /// Delegate for custom log formatting. Defaults to <see cref="DefaultLogFormatter"/>.
    /// </summary>
    public static Func<LogLevel, string, string, string> CustomFormatter = DefaultLogFormatter;

    /// <summary>
    /// Gets or sets the file path for logging. If null, file logging is disabled.
    /// </summary>
    public static string LogFilePath { get; set; } = null;

    /// <summary>
    /// Gets or sets the minimum log level required for messages to be logged.
    /// </summary>
    public static LogLevel Verbosity { get; set; } = LogLevel.Info;

    /// <summary>
    /// Synchronization lock for thread-safe logging.
    /// </summary>
    private static readonly object _lock = new object();

    /// <summary>
    /// The default handler for logging messages, writing them to the console and optionally to a file.
    /// </summary>
    /// <param name="level">The severity level of the log message.</param>
    /// <param name="message">The log message.</param>
    /// <param name="source">The source of the log message (e.g., calling method).</param>
    private static void DefaultLogHandler(LogLevel level, string message, string source)
    {
        if (level < Verbosity)
            return;

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
    private static string DefaultLogFormatter(LogLevel level, string message, string source)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelTag = level switch
        {
            LogLevel.Info => "[INFO]",
            LogLevel.Warning => "[WARN]",
            LogLevel.Error => "[ERROR]",
            _ => "[LOG]"
        };

        return $"{timestamp} {levelTag} [{source}] {message}";
    }

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Info(
        string message,
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        string className = Path.GetFileNameWithoutExtension(filePath); // Extract class name
        string source = $"{className}.{method}";
        LogHandler(LogLevel.Info, message, source);
    }

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    /// <param name="message">The warning message to log.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Warn(
        string message,
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        string className = Path.GetFileNameWithoutExtension(filePath); // Extract class name
        string source = $"{className}.{method}";
        LogHandler(LogLevel.Warning, message, source);
    }

    /// <summary>
    /// Logs an error message, optionally including an exception stack trace.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    /// <param name="ex">An optional exception whose details will be included in the log.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Error(
        string message,
        Exception ex = null,
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        string className = Path.GetFileNameWithoutExtension(filePath); // Extract class name
        string source = $"{className}.{method}";
        var errorMessage = ex == null
            ? message
            : $"{message}\nException: {ex.GetType()}: {ex.Message}\nStackTrace:\n{ex.StackTrace}";
        LogHandler(LogLevel.Error, errorMessage, method);
    }
}
