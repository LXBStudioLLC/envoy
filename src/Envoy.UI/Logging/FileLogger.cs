using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Envoy.UI.Logging;

/// <summary>
/// Minimal, dependency-free rolling file logger. Writes one file per day under
/// %LOCALAPPDATA%\Envoy\logs and prunes files older than the retention window.
/// Desktop log volume is low, so a single lock around the append is plenty and
/// keeps us off a third-party logging dependency. Logging must never throw into
/// the app, so every path is guarded.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDir;
    private readonly LogLevel _minLevel;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string logDir, LogLevel minLevel = LogLevel.Information, int retentionDays = 7)
    {
        _logDir = logDir;
        _minLevel = minLevel;
        try
        {
            Directory.CreateDirectory(_logDir);
            PruneOldLogs(retentionDays);
        }
        catch { /* if we can't set up the log dir, logging simply no-ops */ }
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _minLevel, Write));

    private void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                var path = Path.Combine(_logDir, $"envoy-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, line);
            }
            catch { /* never let logging crash the app */ }
        }
    }

    private void PruneOldLogs(int retentionDays)
    {
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var f in Directory.EnumerateFiles(_logDir, "envoy-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
            }
            catch { /* skip files we can't prune */ }
        }
    }

    public void Dispose() => _loggers.Clear();
}

internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly LogLevel _minLevel;
    private readonly Action<string> _write;

    public FileLogger(string category, LogLevel minLevel, Action<string> write)
    {
        _category = category;
        _minLevel = minLevel;
        _write = write;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null) return;

        var sb = new StringBuilder();
        sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
        sb.Append(ShortLevel(logLevel)).Append(' ');
        sb.Append(_category).Append(" - ").Append(message).Append('\n');
        if (exception is not null) sb.Append(exception).Append('\n');
        _write(sb.ToString());
    }

    private static string ShortLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        _ => "     "
    };

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        private NullScope() { }
        public void Dispose() { }
    }
}
