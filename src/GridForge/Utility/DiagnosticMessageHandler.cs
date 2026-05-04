using SwiftCollections.Diagnostics;
using System;
using System.Runtime.CompilerServices;

namespace GridForge;

/// <summary>
/// Describes a fixed diagnostic level for an interpolated diagnostic message handler.
/// </summary>
public interface IDiagnosticLevelProvider
{
    /// <summary>
    /// Gets the diagnostic level for this provider.
    /// </summary>
    DiagnosticLevel Level { get; }
}

/// <summary>
/// Provides the info diagnostic level for fixed-level interpolated logging.
/// </summary>
public readonly struct InfoDiagnosticLevel : IDiagnosticLevelProvider
{
    /// <inheritdoc />
    public DiagnosticLevel Level => DiagnosticLevel.Info;
}

/// <summary>
/// Provides the warning diagnostic level for fixed-level interpolated logging.
/// </summary>
public readonly struct WarningDiagnosticLevel : IDiagnosticLevelProvider
{
    /// <inheritdoc />
    public DiagnosticLevel Level => DiagnosticLevel.Warning;
}

/// <summary>
/// Provides the error diagnostic level for fixed-level interpolated logging.
/// </summary>
public readonly struct ErrorDiagnosticLevel : IDiagnosticLevelProvider
{
    /// <inheritdoc />
    public DiagnosticLevel Level => DiagnosticLevel.Error;
}

/// <summary>
/// Provides a placeholder level for dynamic-level interpolated logging.
/// </summary>
public readonly struct DynamicDiagnosticLevel : IDiagnosticLevelProvider
{
    /// <inheritdoc />
    public DiagnosticLevel Level => DiagnosticLevel.None;
}

/// <summary>
/// Builds interpolated diagnostic messages only when the requested diagnostic level is enabled.
/// </summary>
/// <typeparam name="TLevel">The fixed diagnostic level provider used by fixed-level helpers.</typeparam>
[InterpolatedStringHandler]
public ref struct DiagnosticMessageHandler<TLevel>
    where TLevel : struct, IDiagnosticLevelProvider
{
    private DiagnosticInterpolatedStringHandler _message;

    /// <summary>
    /// Initializes a new handler for a fixed-level diagnostic message.
    /// </summary>
    /// <param name="literalLength">The combined length of literal portions in the interpolated string.</param>
    /// <param name="formattedCount">The number of formatted expressions in the interpolated string.</param>
    /// <param name="channel">The diagnostic channel that receives the message.</param>
    /// <param name="isEnabled">Set to <see langword="true"/> when formatted expressions should be evaluated.</param>
    public DiagnosticMessageHandler(
        int literalLength,
        int formattedCount,
        DiagnosticChannel channel,
        out bool isEnabled)
        : this(literalLength, formattedCount, channel, default(TLevel).Level, out isEnabled)
    {
    }

    /// <summary>
    /// Initializes a new handler for a dynamic-level diagnostic message.
    /// </summary>
    /// <param name="literalLength">The combined length of literal portions in the interpolated string.</param>
    /// <param name="formattedCount">The number of formatted expressions in the interpolated string.</param>
    /// <param name="channel">The diagnostic channel that receives the message.</param>
    /// <param name="level">The diagnostic level being evaluated.</param>
    /// <param name="isEnabled">Set to <see langword="true"/> when formatted expressions should be evaluated.</param>
    public DiagnosticMessageHandler(
        int literalLength,
        int formattedCount,
        DiagnosticChannel channel,
        DiagnosticLevel level,
        out bool isEnabled)
    {
        if (channel == null)
            throw new ArgumentNullException(nameof(channel));

        _message = new DiagnosticInterpolatedStringHandler(literalLength, formattedCount, channel, level, out isEnabled);
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
