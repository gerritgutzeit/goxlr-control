using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GoXlrControl.Diagnostics;

public sealed class DiagnosticLog : IDisposable
{
    private static readonly Regex WindowsPathRegex = new(
        @"[A-Za-z]:\\(?:[^\s""']+)",
        RegexOptions.Compiled);
    private static readonly Regex UncPathRegex = new(
        @"\\\\[^\s""']+",
        RegexOptions.Compiled);

    private readonly object _gate = new();
    private readonly Queue<string> _ring = new();
    private readonly int _capacity;
    private readonly LoggingLevelSwitch _levelSwitch;
    private readonly ILogger _logger;
    private bool _disposed;

    public DiagnosticLog(string? logDirectory = null, int capacity = 500)
    {
        _capacity = capacity;
        _levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
        var dir = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GoXlrControlStudio", "logs");
        Directory.CreateDirectory(dir);
        _logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(_levelSwitch)
            .WriteTo.File(Path.Combine(dir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateLogger();
    }

    public event EventHandler<string>? EntryAdded;

    public void SetMinimumLevel(string levelName)
    {
        _levelSwitch.MinimumLevel = ParseLevel(levelName);
    }

    public void Info(string message) => Write(LogEventLevel.Information, message);
    public void Warn(string message) => Write(LogEventLevel.Warning, message);
    public void Error(string message) => Write(LogEventLevel.Error, message);

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate) return _ring.ToList();
    }

    public string ExportReport(IDictionary<string, string>? extras = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GoXLR Control Studio — Diagnostic Report");
        sb.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
        sb.AppendLine("Machine: [redacted]");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        if (extras is not null)
        {
            foreach (var (k, v) in extras)
                sb.AppendLine($"{k}: {RedactSensitive(v)}");
        }

        sb.AppendLine("--- Log ---");
        foreach (var line in Snapshot())
            sb.AppendLine(RedactSensitive(line));
        return sb.ToString();
    }

    public static string RedactSensitive(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var redacted = WindowsPathRegex.Replace(text, "[path]");
        redacted = UncPathRegex.Replace(redacted, "[path]");
        return redacted;
    }

    public static LogEventLevel ParseLevel(string? levelName) =>
        Enum.TryParse<LogEventLevel>(levelName, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_logger as IDisposable)?.Dispose();
    }

    private void Write(LogEventLevel level, string message)
    {
        if (level < _levelSwitch.MinimumLevel)
            return;

        var line = $"{DateTimeOffset.Now:HH:mm:ss.fff} [{level}] {message}";
        lock (_gate)
        {
            _ring.Enqueue(line);
            while (_ring.Count > _capacity)
                _ring.Dequeue();
        }

        _logger.Write(level, message);
        EntryAdded?.Invoke(this, line);
    }
}
