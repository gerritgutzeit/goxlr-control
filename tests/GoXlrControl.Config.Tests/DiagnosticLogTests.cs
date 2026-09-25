using FluentAssertions;
using GoXlrControl.Diagnostics;
using Serilog.Events;

namespace GoXlrControl.Config.Tests;

public class DiagnosticLogTests
{
    [Fact]
    public void RedactSensitive_ReplacesWindowsAndUncPaths()
    {
        var input = @"Failed at C:\Users\gerri\AppData\Local\app.exe and \\server\share\file.json";
        var redacted = DiagnosticLog.RedactSensitive(input);
        redacted.Should().NotContain(@"C:\Users");
        redacted.Should().NotContain(@"\\server");
        redacted.Should().Contain("[path]");
    }

    [Fact]
    public void ExportReport_OmitsMachineName_AndRedactsPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "goxlr-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var log = new DiagnosticLog(dir);
            log.Info(@"Opened C:\Temp\secret\profile.json");
            var report = log.ExportReport(new Dictionary<string, string>
            {
                ["Device"] = @"D:\Devices\GoXLR"
            });

            report.Should().Contain("Machine: [redacted]");
            report.Should().NotContain(Environment.MachineName);
            report.Should().NotContain(@"C:\Temp");
            report.Should().NotContain(@"D:\Devices");
            report.Should().Contain("[path]");
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Theory]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("Warning", LogEventLevel.Warning)]
    [InlineData("nope", LogEventLevel.Information)]
    public void ParseLevel_MapsKnownValues(string name, LogEventLevel expected)
    {
        DiagnosticLog.ParseLevel(name).Should().Be(expected);
    }

    [Fact]
    public void SetMinimumLevel_FiltersRingBufferEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), "goxlr-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var log = new DiagnosticLog(dir);
            log.SetMinimumLevel("Warning");
            log.Info("should-not-appear");
            log.Warn("should-appear");

            var snapshot = log.Snapshot();
            snapshot.Should().ContainSingle(l => l.Contains("should-appear"));
            snapshot.Should().NotContain(l => l.Contains("should-not-appear"));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
            // Serilog may briefly retain the shared file handle on some hosts.
        }
    }
}
