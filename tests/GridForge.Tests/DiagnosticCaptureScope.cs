using SwiftCollections.Diagnostics;
using System;
using System.Collections.Generic;

namespace GridForge.Grids.Tests;

internal sealed class DiagnosticCaptureScope : IDisposable
{
    private readonly Action<DiagnosticLevel, string, string> _originalHandler;
    private readonly DiagnosticLevel _originalMinimumLevel;

    public DiagnosticCaptureScope(DiagnosticLevel minimumLevel = DiagnosticLevel.Info)
    {
        Messages = new List<(DiagnosticLevel Level, string Message, string Source)>();
        _originalHandler = GridForgeLogger.LogHandler;
        _originalMinimumLevel = GridForgeLogger.MinimumLevel;

        GridForgeLogger.MinimumLevel = minimumLevel;
        GridForgeLogger.LogHandler = (level, message, source) =>
            Messages.Add((level, message, source));
    }

    public List<(DiagnosticLevel Level, string Message, string Source)> Messages { get; }

    public void Dispose()
    {
        GridForgeLogger.LogHandler = _originalHandler;
        GridForgeLogger.MinimumLevel = _originalMinimumLevel;
    }
}
