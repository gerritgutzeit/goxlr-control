using System.Text;
using Serilog;
using Serilog.Events;

namespace GoXlrControl.Diagnostics;

public sealed class DiagnosticLog
{
    private readonly object _gate = new();
    private readonly Queue<string> _ring = new();
    private readonly int _capacity;
    private readonly ILogger _logger;

    public DiagnosticLog(string? logDirectory = null, int capacity = 500)
    {
        _capacity = capacity;
        var dir = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GoXlrControlStudio", "logs");
        Directory.CreateDirectory(dir);
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(dir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }

    public event EventHandler<string>? EntryAdded;

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
        sb.AppendLine($"Machine: {Environment.MachineName}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        if (extras is not null)
        {
            foreach (var (k, v) in extras)
                sb.AppendLine($"{k}: {v}");
        }

        sb.AppendLine("--- Log ---");
        foreach (var line in Snapshot())
            sb.AppendLine(line);
        return sb.ToString();
    }

    private void Write(LogEventLevel level, string message)
    {
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
