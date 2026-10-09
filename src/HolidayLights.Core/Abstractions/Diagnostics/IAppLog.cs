namespace HolidayLights.Core.Abstractions;

/// <summary>Severity of a log entry.</summary>
public enum AppLogLevel
{
    /// <summary>Diagnostic detail.</summary>
    Debug,

    /// <summary>Normal events (start, imports, rebuilds).</summary>
    Info,

    /// <summary>Recoverable problems (fallbacks, retries, unreadable files).</summary>
    Warning,

    /// <summary>Unexpected errors.</summary>
    Error,
}

/// <summary>
/// The application log (PRODUCT-SPEC 6.6.5): <c>%LOCALAPPDATA%\Holiday Lights\Logs\</c>, 3 files of 1 MB, rotating.
/// Implemented by app-shell; every component receives it by constructor. Thread-safe; never throws.
/// </summary>
public interface IAppLog
{
    /// <summary>Writes one entry.</summary>
    /// <param name="level">Severity.</param>
    /// <param name="source">The component, e.g. "Rendering" or "Audio.Midi".</param>
    /// <param name="message">What happened (no personal data).</param>
    /// <param name="exception">The exception, if any.</param>
    void Write(AppLogLevel level, string source, string message, Exception? exception = null);
}

/// <summary>A log that discards everything (tests, tools).</summary>
public sealed class NullAppLog : IAppLog
{
    /// <summary>The shared instance.</summary>
    public static NullAppLog Instance { get; } = new();

    /// <inheritdoc />
    public void Write(AppLogLevel level, string source, string message, Exception? exception = null)
    {
    }
}

/// <summary>Shorthands for <see cref="IAppLog"/>.</summary>
public static class AppLogExtensions
{
    /// <summary>Writes an <see cref="AppLogLevel.Info"/> entry.</summary>
    /// <param name="log">The log.</param>
    /// <param name="source">The component.</param>
    /// <param name="message">What happened.</param>
    public static void Info(this IAppLog log, string source, string message) => log.Write(AppLogLevel.Info, source, message);

    /// <summary>Writes a <see cref="AppLogLevel.Warning"/> entry.</summary>
    /// <param name="log">The log.</param>
    /// <param name="source">The component.</param>
    /// <param name="message">What happened.</param>
    /// <param name="exception">The exception, if any.</param>
    public static void Warn(this IAppLog log, string source, string message, Exception? exception = null) =>
        log.Write(AppLogLevel.Warning, source, message, exception);

    /// <summary>Writes an <see cref="AppLogLevel.Error"/> entry.</summary>
    /// <param name="log">The log.</param>
    /// <param name="source">The component.</param>
    /// <param name="message">What happened.</param>
    /// <param name="exception">The exception, if any.</param>
    public static void Error(this IAppLog log, string source, string message, Exception? exception = null) =>
        log.Write(AppLogLevel.Error, source, message, exception);
}
