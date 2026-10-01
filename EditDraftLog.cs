using System;
using Microsoft.Extensions.Logging;

namespace Xaf.EditDraft.Core;

/// <summary>A sink for the engine's log lines. Every message arrives complete, prefix included (e.g. "[EditDraft] ...").</summary>
public interface IEditDraftLog
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}

/// <summary>
/// The engine's logging facade. The engine writes through <see cref="Info"/>, <see cref="Warning"/> and
/// <see cref="Error"/>; the host chooses the sink once at startup (<see cref="Sink"/>). The message text is
/// passed through unchanged, so a host sink writes the same lines as before the extraction.
///
/// Default: when the host chose no sink, <see cref="EditDraftCoreModule"/> uses the application's ILogger
/// (category "Xaf.EditDraft") if an ILoggerFactory is registered; before that, or without one,
/// System.Diagnostics.Trace. A failing sink never breaks the engine.
/// </summary>
public static class EditDraftLog
{
    private static volatile IEditDraftLog _sink;
    private static volatile bool _chosenByHost;

    /// <summary>The sink in use. Setting it is the host's choice (the module default no longer applies); null returns to Trace.</summary>
    public static IEditDraftLog Sink
    {
        get => _sink ?? TraceEditDraftLog.Instance;
        set { _sink = value; _chosenByHost = value != null; }
    }

    /// <summary>The module's default: the ILogger of <paramref name="services"/>, only when the host chose no sink.</summary>
    public static void UseLoggerIfNoHostSink(IServiceProvider services)
    {
        if (_chosenByHost) return;
        try
        {
            if (services?.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory)
                _sink = new LoggerEditDraftLog(factory.CreateLogger("Xaf.EditDraft"));
        }
        catch { /* keep the current sink */ }
    }

    public static void Info(string message) { try { Sink.Info(message); } catch { } }

    public static void Warning(string message) { try { Sink.Warning(message); } catch { } }

    public static void Error(string message) { try { Sink.Error(message); } catch { } }
}

/// <summary>The fallback sink: System.Diagnostics.Trace (BCL only).</summary>
public sealed class TraceEditDraftLog : IEditDraftLog
{
    public static readonly TraceEditDraftLog Instance = new();

    public void Info(string message) => System.Diagnostics.Trace.TraceInformation(message);
    public void Warning(string message) => System.Diagnostics.Trace.TraceWarning(message);
    public void Error(string message) => System.Diagnostics.Trace.TraceError(message);
}

/// <summary>An ILogger sink. The message is passed as-is (never as a message template, so braces in it are text).</summary>
public sealed class LoggerEditDraftLog : IEditDraftLog
{
    private readonly ILogger _logger;

    public LoggerEditDraftLog(ILogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void Info(string message) => Write(LogLevel.Information, message);
    public void Warning(string message) => Write(LogLevel.Warning, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    private void Write(LogLevel level, string message) => _logger.Log(level, default(EventId), message, null, (state, _) => state);
}
