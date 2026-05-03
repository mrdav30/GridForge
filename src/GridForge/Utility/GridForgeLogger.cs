using SwiftCollections.Diagnostics;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace GridForge;

/// <summary>
/// Provides a configurable logging system for GridForge with support for log levels, formatting, and file output.
/// </summary>
public static class GridForgeLogger
{
    /// <summary>
    /// Delegate for handling log messages. Defaults to <see cref="DefaultLogHandler"/>.
    /// </summary>
    private static Action<DiagnosticLevel, string, string> _logHandler = DefaultLogHandler;

    private static readonly DiagnosticChannel _channel = CreateChannel();

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
    /// Determines whether diagnostics at the specified level are currently enabled.
    /// </summary>
    /// <param name="level">The diagnostic level to evaluate.</param>
    /// <returns><see langword="true"/> when messages at <paramref name="level"/> will be emitted; otherwise, <see langword="false"/>.</returns>
    public static bool IsEnabled(DiagnosticLevel level)
    {
        return _channel.IsEnabled(level);
    }

    /// <summary>
    /// Synchronization lock for thread-safe logging.
    /// </summary>
    private static readonly object _lock = new();

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

    /// <summary>
    /// Logs an informational interpolated diagnostic message without evaluating formatted expressions when info diagnostics are disabled.
    /// </summary>
    /// <param name="message">The interpolated diagnostic message.</param>
    /// <param name="source">An optional source identifier. When omitted, caller information is used.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Info(
        GridForgeInfoDiagnosticInterpolatedStringHandler message,
        string source = "",
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!message.IsEnabled)
            return;

        WriteCore(DiagnosticLevel.Info, message.Message, ResolveSource(source, method, filePath));
    }

    /// <summary>
    /// Logs a warning interpolated diagnostic message without evaluating formatted expressions when warning diagnostics are disabled.
    /// </summary>
    /// <param name="message">The interpolated diagnostic message.</param>
    /// <param name="source">An optional source identifier. When omitted, caller information is used.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Warn(
        GridForgeWarningDiagnosticInterpolatedStringHandler message,
        string source = "",
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!message.IsEnabled)
            return;

        WriteCore(DiagnosticLevel.Warning, message.Message, ResolveSource(source, method, filePath));
    }

    /// <summary>
    /// Logs an error interpolated diagnostic message without evaluating formatted expressions when error diagnostics are disabled.
    /// </summary>
    /// <param name="message">The interpolated diagnostic message.</param>
    /// <param name="source">An optional source identifier. When omitted, caller information is used.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Error(
        GridForgeErrorDiagnosticInterpolatedStringHandler message,
        string source = "",
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!message.IsEnabled)
            return;

        WriteCore(DiagnosticLevel.Error, message.Message, ResolveSource(source, method, filePath));
    }

    /// <summary>
    /// Writes an interpolated diagnostic message at the specified level without evaluating formatted expressions when the level is disabled.
    /// </summary>
    /// <param name="level">The severity level of the diagnostic event.</param>
    /// <param name="message">The interpolated diagnostic message.</param>
    /// <param name="source">An optional source identifier. When omitted, caller information is used.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Write(
        DiagnosticLevel level,
        [InterpolatedStringHandlerArgument("level")] GridForgeDiagnosticInterpolatedStringHandler message,
        string source = "",
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!message.IsEnabled)
            return;

        WriteCore(level, message.Message, ResolveSource(source, method, filePath));
    }

    /// <summary>
    /// Builds interpolated diagnostic messages only when the requested diagnostic level is enabled.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct GridForgeDiagnosticInterpolatedStringHandler
    {
        private DiagnosticInterpolatedStringHandler _message;

        /// <summary>
        /// Initializes a new handler for a GridForge diagnostic message.
        /// </summary>
        /// <param name="literalLength">The combined length of literal portions in the interpolated string.</param>
        /// <param name="formattedCount">The number of formatted expressions in the interpolated string.</param>
        /// <param name="level">The diagnostic level being evaluated.</param>
        /// <param name="isEnabled">Set to <see langword="true"/> when formatted expressions should be evaluated.</param>
        public GridForgeDiagnosticInterpolatedStringHandler(
            int literalLength,
            int formattedCount,
            DiagnosticLevel level,
            out bool isEnabled)
        {
            _message = new DiagnosticInterpolatedStringHandler(literalLength, formattedCount, _channel, level, out isEnabled);
        }

        /// <summary>
        /// Gets whether the handler is actively building a diagnostic message.
        /// </summary>
        public bool IsEnabled => _message.IsEnabled;

        internal DiagnosticInterpolatedStringHandler Message => _message;

        /// <summary>
        /// Appends a literal string segment.
        /// </summary>
        /// <param name="value">The literal string segment.</param>
        public void AppendLiteral(string value)
        {
            _message.AppendLiteral(value);
        }

        /// <summary>
        /// Appends a formatted value.
        /// </summary>
        /// <typeparam name="T">The type of value to append.</typeparam>
        /// <param name="value">The value to append.</param>
        public void AppendFormatted<T>(T value)
        {
            _message.AppendFormatted(value);
        }

        /// <summary>
        /// Appends a formatted value using the specified format string.
        /// </summary>
        /// <typeparam name="T">The type of value to append.</typeparam>
        /// <param name="value">The value to append.</param>
        /// <param name="format">The format string to apply.</param>
        public void AppendFormatted<T>(T value, string format)
        {
            _message.AppendFormatted(value, format);
        }

        /// <summary>
        /// Appends a formatted value with the specified alignment.
        /// </summary>
        /// <typeparam name="T">The type of value to append.</typeparam>
        /// <param name="value">The value to append.</param>
        /// <param name="alignment">The minimum width for the formatted value.</param>
        public void AppendFormatted<T>(T value, int alignment)
        {
            _message.AppendFormatted(value, alignment);
        }

        /// <summary>
        /// Appends a formatted value with the specified alignment and format string.
        /// </summary>
        /// <typeparam name="T">The type of value to append.</typeparam>
        /// <param name="value">The value to append.</param>
        /// <param name="alignment">The minimum width for the formatted value.</param>
        /// <param name="format">The format string to apply.</param>
        public void AppendFormatted<T>(T value, int alignment, string format)
        {
            _message.AppendFormatted(value, alignment, format);
        }

        /// <summary>
        /// Appends a string value.
        /// </summary>
        /// <param name="value">The value to append.</param>
        public void AppendFormatted(string? value)
        {
            _message.AppendFormatted(value ?? string.Empty);
        }

        /// <summary>
        /// Appends a string value with the specified alignment.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="alignment">The minimum width for the formatted value.</param>
        public void AppendFormatted(string? value, int alignment)
        {
            _message.AppendFormatted(value ?? string.Empty, alignment);
        }

        /// <summary>
        /// Appends a string value with the specified alignment and format string.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="alignment">The minimum width for the formatted value.</param>
        /// <param name="format">The format string to apply.</param>
        public void AppendFormatted(string? value, int alignment, string format)
        {
            _message.AppendFormatted(value ?? string.Empty, alignment, format);
        }

        /// <summary>
        /// Appends a character span.
        /// </summary>
        /// <param name="value">The span to append.</param>
        public void AppendFormatted(ReadOnlySpan<char> value)
        {
            _message.AppendFormatted(value);
        }

        /// <summary>
        /// Appends a character span with the specified alignment.
        /// </summary>
        /// <param name="value">The span to append.</param>
        /// <param name="alignment">The minimum width for the formatted value.</param>
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment)
        {
            _message.AppendFormatted(value, alignment);
        }

        /// <summary>
        /// Appends a character span with the specified alignment and format string.
        /// </summary>
        /// <param name="value">The span to append.</param>
        /// <param name="alignment">The minimum width for the formatted value.</param>
        /// <param name="format">The format string to apply.</param>
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment, string format)
        {
            _message.AppendFormatted(value, alignment, format);
        }
    }

    /// <summary>
    /// Builds info diagnostic messages only when info diagnostics are enabled.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct GridForgeInfoDiagnosticInterpolatedStringHandler
    {
        private GridForgeDiagnosticInterpolatedStringHandler _message;

        /// <summary>
        /// Initializes a new handler for an info diagnostic message.
        /// </summary>
        /// <param name="literalLength">The combined length of literal portions in the interpolated string.</param>
        /// <param name="formattedCount">The number of formatted expressions in the interpolated string.</param>
        /// <param name="isEnabled">Set to <see langword="true"/> when formatted expressions should be evaluated.</param>
        public GridForgeInfoDiagnosticInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
        {
            _message = new GridForgeDiagnosticInterpolatedStringHandler(
                literalLength,
                formattedCount,
                DiagnosticLevel.Info,
                out isEnabled);
        }

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.IsEnabled" />
        public bool IsEnabled => _message.IsEnabled;

        internal DiagnosticInterpolatedStringHandler Message => _message.Message;

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendLiteral(string)" />
        public void AppendLiteral(string value) => _message.AppendLiteral(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T)" />
        public void AppendFormatted<T>(T value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, string)" />
        public void AppendFormatted<T>(T value, string format) => _message.AppendFormatted(value, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, int)" />
        public void AppendFormatted<T>(T value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, int, string)" />
        public void AppendFormatted<T>(T value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string)" />
        public void AppendFormatted(string? value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string, int)" />
        public void AppendFormatted(string? value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string, int, string)" />
        public void AppendFormatted(string? value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char})" />
        public void AppendFormatted(ReadOnlySpan<char> value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int)" />
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int, string)" />
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);
    }

    /// <summary>
    /// Builds warning diagnostic messages only when warning diagnostics are enabled.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct GridForgeWarningDiagnosticInterpolatedStringHandler
    {
        private GridForgeDiagnosticInterpolatedStringHandler _message;

        /// <summary>
        /// Initializes a new handler for a warning diagnostic message.
        /// </summary>
        /// <param name="literalLength">The combined length of literal portions in the interpolated string.</param>
        /// <param name="formattedCount">The number of formatted expressions in the interpolated string.</param>
        /// <param name="isEnabled">Set to <see langword="true"/> when formatted expressions should be evaluated.</param>
        public GridForgeWarningDiagnosticInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
        {
            _message = new GridForgeDiagnosticInterpolatedStringHandler(
                literalLength,
                formattedCount,
                DiagnosticLevel.Warning,
                out isEnabled);
        }

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.IsEnabled" />
        public bool IsEnabled => _message.IsEnabled;

        internal DiagnosticInterpolatedStringHandler Message => _message.Message;

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendLiteral(string)" />
        public void AppendLiteral(string value) => _message.AppendLiteral(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T)" />
        public void AppendFormatted<T>(T value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, string)" />
        public void AppendFormatted<T>(T value, string format) => _message.AppendFormatted(value, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, int)" />
        public void AppendFormatted<T>(T value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, int, string)" />
        public void AppendFormatted<T>(T value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string)" />
        public void AppendFormatted(string? value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string, int)" />
        public void AppendFormatted(string? value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string, int, string)" />
        public void AppendFormatted(string? value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char})" />
        public void AppendFormatted(ReadOnlySpan<char> value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int)" />
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int, string)" />
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);
    }

    /// <summary>
    /// Builds error diagnostic messages only when error diagnostics are enabled.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct GridForgeErrorDiagnosticInterpolatedStringHandler
    {
        private GridForgeDiagnosticInterpolatedStringHandler _message;

        /// <summary>
        /// Initializes a new handler for an error diagnostic message.
        /// </summary>
        /// <param name="literalLength">The combined length of literal portions in the interpolated string.</param>
        /// <param name="formattedCount">The number of formatted expressions in the interpolated string.</param>
        /// <param name="isEnabled">Set to <see langword="true"/> when formatted expressions should be evaluated.</param>
        public GridForgeErrorDiagnosticInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
        {
            _message = new GridForgeDiagnosticInterpolatedStringHandler(
                literalLength,
                formattedCount,
                DiagnosticLevel.Error,
                out isEnabled);
        }

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.IsEnabled" />
        public bool IsEnabled => _message.IsEnabled;

        internal DiagnosticInterpolatedStringHandler Message => _message.Message;

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendLiteral(string)" />
        public void AppendLiteral(string value) => _message.AppendLiteral(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T)" />
        public void AppendFormatted<T>(T value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, string)" />
        public void AppendFormatted<T>(T value, string format) => _message.AppendFormatted(value, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, int)" />
        public void AppendFormatted<T>(T value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted{T}(T, int, string)" />
        public void AppendFormatted<T>(T value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string)" />
        public void AppendFormatted(string? value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string, int)" />
        public void AppendFormatted(string? value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(string, int, string)" />
        public void AppendFormatted(string? value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char})" />
        public void AppendFormatted(ReadOnlySpan<char> value) => _message.AppendFormatted(value);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int)" />
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment) => _message.AppendFormatted(value, alignment);

        /// <inheritdoc cref="GridForgeDiagnosticInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int, string)" />
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment, string format) => _message.AppendFormatted(value, alignment, format);
    }

    private static void WriteCore(DiagnosticLevel level, DiagnosticInterpolatedStringHandler message, string source)
    {
        _channel.Write(level, message, source);
    }

    private static string ResolveSource(string source, string method, string filePath)
    {
        if (!string.IsNullOrEmpty(source))
            return source;

        string className = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(className))
            return method;

        if (string.IsNullOrEmpty(method))
            return className;

        return $"{className}.{method}";
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
}
