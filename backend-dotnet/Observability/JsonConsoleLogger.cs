using System.Collections.Concurrent;
using System.Text.Json;

namespace Opstrax.Api.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// JsonConsoleLoggerProvider — structured (JSON) logging for the whole app.
//
// Every log line is a single JSON object written to stdout (the format Render,
// Loki, Datadog, CloudWatch, etc. ingest natively). Each entry is automatically
// enriched from the ambient TelemetryContext so operators can pivot any log to
// its trace:
//   timestamp, level(severity), message, category(module),
//   correlation_id, trace_id, span_id, tenant_id, user_id, role,
//   endpoint, method, deployment_version, environment,
//   error_code, exception (redacted), event_id.
//
// The message + exception text are passed through LogRedactor so no secret/PII
// leaks even from a raw framework exception.
//
// Enable by setting Logging:Json = true (default in Production).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class JsonConsoleLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, JsonConsoleLogger> _loggers = new();
    private static readonly TextWriter Out = Console.Out;
    private static readonly object WriteLock = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new JsonConsoleLogger(name, WriteEntry));

    private static void WriteEntry(string json)
    {
        // Single lock keeps each JSON object on its own line (log collectors are
        // line-delimited). Cheap relative to the DB/HTTP work each request does.
        lock (WriteLock) { Out.WriteLine(json); }
    }

    public void Dispose() => _loggers.Clear();
}

public sealed class JsonConsoleLogger(string category, Action<string> sink) : ILogger
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    IDisposable? ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var ctx = TelemetryContext.Current;
        var message = LogRedactor.Scrub(formatter(state, exception));

        // error_code: use the EventId name if present, else the exception type.
        string? errorCode = null;
        if (!string.IsNullOrWhiteSpace(eventId.Name)) errorCode = eventId.Name;
        else if (exception is not null) errorCode = exception.GetType().Name;

        var entry = new Dictionary<string, object?>
        {
            ["timestamp"]          = DateTime.UtcNow.ToString("o"),
            ["severity"]           = SeverityOf(logLevel),
            ["level"]              = logLevel.ToString(),
            ["module"]             = category,
            ["message"]            = message,
            ["correlation_id"]     = ctx?.CorrelationId,
            ["trace_id"]           = ctx?.TraceId,
            ["span_id"]            = ctx?.SpanId,
            ["tenant_id"]          = ctx?.TenantId,
            ["user_id"]            = ctx?.UserId,
            ["role"]               = ctx?.Role,
            ["endpoint"]           = ctx?.Endpoint,
            ["method"]             = ctx?.Method,
            ["deployment_version"] = ctx?.DeploymentVersion ?? BuildInfo.Version,
            ["environment"]        = BuildInfo.Environment,
        };

        if (eventId.Id != 0) entry["event_id"] = eventId.Id;
        if (errorCode is not null) entry["error_code"] = errorCode;

        if (exception is not null)
        {
            entry["exception_type"]  = exception.GetType().FullName;
            entry["exception"]       = LogRedactor.Scrub(exception.Message);
            // stack reference only — a compact hash + top frame, never full PII-bearing args.
            entry["stack_reference"] = StackReference(exception);
        }

        try { sink(JsonSerializer.Serialize(entry, Opts)); }
        catch { /* logging must never throw into the request path */ }
    }

    // "severity" mirrors the syslog/Google Cloud vocabulary used by log platforms.
    private static string SeverityOf(LogLevel level) => level switch
    {
        LogLevel.Trace       => "DEBUG",
        LogLevel.Debug       => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning     => "WARNING",
        LogLevel.Error       => "ERROR",
        LogLevel.Critical    => "CRITICAL",
        _                    => "DEFAULT",
    };

    private static string StackReference(Exception ex)
    {
        var top = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "no-frame";
        // Stable short id so repeated occurrences of the same fault correlate in logs.
        var hash = (uint)(ex.GetType().FullName + "|" + top).GetHashCode();
        return $"{ex.GetType().Name}#{hash:x8}";
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
