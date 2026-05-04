# Diagnostics and Logging

This page covers the small but important diagnostics surface in GridForge.

The main idea is simple:

Use `GridForgeLogger` as the shared logging hook for the library and for debugging library behavior. Do not scatter ad hoc `Console.WriteLine(...)` calls through core code.

## What `GridForgeLogger` Is

`GridForgeLogger` is a static logging facade built on top of `SwiftCollections.Diagnostics.DiagnosticChannel`.

It gives the library one place to control:

- severity filtering
- message formatting
- output handling
- optional file logging
- thread-safe writes

Even though the surface is small, it matters because many subsystems use it as the fallback path when event subscribers or cleanup hooks throw.

## Default Behavior

Out of the box:

- `MinimumLevel` defaults to `Warning`
- `LogHandler` writes formatted messages to the console
- `CustomFormatter` produces UTC timestamped entries with level and source
- `LogFilePath` is `null`, so file logging is off by default

That means info-level messages are suppressed unless you opt in explicitly.

## Message Shape

The default formatter emits entries like:

```text
2026-04-06 14:32:10.123 [WARN] [GridOccupantManager.TryRemoveVoxelOccupant] ...
```

The three key parts are:

- timestamp in UTC
- severity tag
- derived source in `ClassName.MethodName` form

That source is generated automatically from caller information for the standard `Info(...)`, `Warn(...)`, and `Error(...)` helpers.

## Logging API

The public entry points are intentionally minimal:

- `GridForgeLogger.Channel.Info(...)`
- `GridForgeLogger.Channel.Warn(...)`
- `GridForgeLogger.Channel.Error(...)`
- `GridForgeLogger.Channel.Log(DiagnosticLevel, ...)`

`Info(...)`, `Warn(...)`, and `Error(...)` are fixed-level interpolated diagnostic helpers. `Log(DiagnosticLevel, ...)` is the matching generic primitive when the level is selected dynamically. These helpers are `DiagnosticChannel` extensions, so `GridForgeLogger.Channel` is the default GridForge channel, but the same no-work-when-disabled path can be reused with another diagnostic channel.

All logger entry points use the SwiftCollections diagnostic string handler, so formatted expressions are not evaluated when the requested level is disabled. This means call sites should pass interpolated strings, even for literal messages:

```csharp
GridForgeLogger.Channel.Warn($"Grid world not active. Cannot resolve grids.");
```

If you need exception details, include only the details you want in the interpolated message. They will still be skipped when the error level is disabled.

For most library work, that is enough. If you need more control, configure the handler or formatter rather than creating a second logging path.

## Configuration Surface

### `MinimumLevel`

Use this to control verbosity.

- `Warning` is the default
- `Info` is useful for local debugging
- `Error` keeps only failures
- `None` disables logging entirely

The tests confirm that `DiagnosticLevel.None` suppresses all logger output, even if a custom handler is installed.

### `LogHandler`

This is the output sink. By default it writes to the console.

You can replace it with a custom delegate to:

- route output into a test harness
- capture logs in a game server host
- forward diagnostics into your application's own logging system

Assigning `null` restores the default handler.

### `CustomFormatter`

This is the string formatter used before output is written.

Use it when you want:

- a different timestamp shape
- structured prefixes
- application-specific log layout

Assigning `null` restores the default formatter.

### `LogFilePath`

When set, the default handler appends each formatted entry to the given file path in addition to console output.

The implementation intentionally swallows file write failures and writes a fallback error to the console instead of throwing into calling code. That is important because diagnostics should not usually destabilize the library path that triggered them.

## Where Logs Show Up In Practice

You will mostly see `GridForgeLogger` used in places like:

- event subscriber notification loops
- partition attach or remove failures
- invalid occupant or partition removal attempts
- blocker notification failures
- cleanup or release paths that should not throw back into the main operation

This is a deliberate pattern across the codebase:

- keep the core operation running when a subscriber misbehaves
- emit a diagnostic instead of crashing the mutation flow

## Recommended Debugging Patterns

### During tests

Raise `MinimumLevel` only when the extra output is useful. The default warning level keeps test output quieter and usually matches what you want for routine validation.

### During focused local debugging

Temporarily set:

```csharp
using GridForge;
using SwiftCollections.Diagnostics;

GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;
```

This is a good fit when you are tracing registration, blocker lifecycle, or occupant add and remove behavior.

For fixed-level diagnostics, use the matching helper:

```csharp
GridForgeLogger.Channel.Warn(
    $"Occupant {occupant.GlobalId} is not registered to voxel {voxel.WorldIndex}.");
```

When `Warning` is disabled, the formatted values in that message are not evaluated and the final message string is not built.

When the level is dynamic, use `Log(...)`:

```csharp
GridForgeLogger.Channel.Log(level, $"Resolved {count} candidate voxels.");
```

### During longer-running tools or simulations

If you need a persistent record, set `LogFilePath` to a writable location and leave the standard logger surface in place.

### In library code

Prefer `GridForgeLogger` over direct console writes so behavior stays consistent and callers can override or suppress output centrally.

## A Small Configuration Example

```csharp
using GridForge;
using SwiftCollections.Diagnostics;

GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;
GridForgeLogger.LogFilePath = "gridforge.log";
GridForgeLogger.CustomFormatter = (level, message, source) =>
    $"{level} | {source} | {message}";
```

If you later want to restore defaults:

```csharp
GridForgeLogger.CustomFormatter = null;
GridForgeLogger.LogHandler = null;
GridForgeLogger.LogFilePath = null;
GridForgeLogger.MinimumLevel = DiagnosticLevel.Warning;
```

## Common Pitfalls

- Adding direct console output in core code instead of routing through `GridForgeLogger`
- Forgetting that info logs are filtered out by default
- Passing plain strings such as `GridForgeLogger.Channel.Warn("message")`; use interpolated strings such as `GridForgeLogger.Channel.Warn($"message")` so the handler overload is selected
- Treating logging as a control-flow mechanism instead of diagnostics
- Assuming a failed log-file write will throw back into the caller
- Forgetting to reset logger settings in tests after customizing them

## Read This Next

- [Common Workflows](Common-Workflows) for small examples that may benefit from temporary info-level logging
- [Testing and Benchmarking](Testing-and-Benchmarking) once that page exists, for validation-focused diagnostics habits
- [Home](Home) for the repo-wide invariant that logging should stay centralized
