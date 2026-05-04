using SwiftCollections.Diagnostics;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace GridForge;

/// <summary>
/// Provides interpolated diagnostic logging helpers for <see cref="DiagnosticChannel"/>.
/// </summary>
public static class DiagnosticChannelLoggingExtensions
{
    /// <summary>
    /// Writes an info diagnostic message without evaluating formatted expressions when info diagnostics are disabled.
    /// </summary>
    /// <param name="channel">The diagnostic channel that receives the message.</param>
    /// <param name="message">The interpolated diagnostic message.</param>
    /// <param name="source">An optional source identifier. When omitted, caller information is used.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Info(
        this DiagnosticChannel channel,
        [InterpolatedStringHandlerArgument("channel")] DiagnosticMessageHandler<InfoDiagnosticLevel> message,
        string source = "",
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!message.IsEnabled)
            return;

        WriteCore(channel, DiagnosticLevel.Info, message.Message, ResolveSource(source, method, filePath));
    }

    /// <summary>
    /// Writes a warning diagnostic message without evaluating formatted expressions when warning diagnostics are disabled.
    /// </summary>
    /// <param name="channel">The diagnostic channel that receives the message.</param>
    /// <param name="message">The interpolated diagnostic message.</param>
    /// <param name="source">An optional source identifier. When omitted, caller information is used.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Warn(
        this DiagnosticChannel channel,
        [InterpolatedStringHandlerArgument("channel")] DiagnosticMessageHandler<WarningDiagnosticLevel> message,
        string source = "",
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!message.IsEnabled)
            return;

        WriteCore(channel, DiagnosticLevel.Warning, message.Message, ResolveSource(source, method, filePath));
    }

    /// <summary>
    /// Writes an error diagnostic message without evaluating formatted expressions when error diagnostics are disabled.
    /// </summary>
    /// <param name="channel">The diagnostic channel that receives the message.</param>
    /// <param name="message">The interpolated diagnostic message.</param>
    /// <param name="source">An optional source identifier. When omitted, caller information is used.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Error(
        this DiagnosticChannel channel,
        [InterpolatedStringHandlerArgument("channel")] DiagnosticMessageHandler<ErrorDiagnosticLevel> message,
        string source = "",
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!message.IsEnabled)
            return;

        WriteCore(channel, DiagnosticLevel.Error, message.Message, ResolveSource(source, method, filePath));
    }

    /// <summary>
    /// Writes a dynamic-level diagnostic message without evaluating formatted expressions when the level is disabled.
    /// </summary>
    /// <param name="channel">The diagnostic channel that receives the message.</param>
    /// <param name="level">The severity level of the diagnostic event.</param>
    /// <param name="message">The interpolated diagnostic message.</param>
    /// <param name="source">An optional source identifier. When omitted, caller information is used.</param>
    /// <param name="method">The calling method name, automatically captured.</param>
    /// <param name="filePath">The calling file name, automatically captured to get the class name.</param>
    public static void Log(
        this DiagnosticChannel channel,
        DiagnosticLevel level,
        [InterpolatedStringHandlerArgument("channel", "level")] DiagnosticMessageHandler<DynamicDiagnosticLevel> message,
        string source = "",
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!message.IsEnabled)
            return;

        WriteCore(channel, level, message.Message, ResolveSource(source, method, filePath));
    }

    private static void WriteCore(
        DiagnosticChannel channel,
        DiagnosticLevel level,
        DiagnosticInterpolatedStringHandler message,
        string source)
    {
        if (channel == null)
            throw new ArgumentNullException(nameof(channel));

        channel.Write(level, message, source);
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
}
