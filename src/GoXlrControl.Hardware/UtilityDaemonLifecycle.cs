using System.Diagnostics;

namespace GoXlrControl.Hardware;

/// <summary>
/// Locates and starts the GoXLR Utility daemon without requiring the Utility UI.
/// </summary>
public static class UtilityDaemonLifecycle
{
    public static readonly string[] DaemonProcessNames = ["goxlr-daemon", "goxlr-utility"];
    public static readonly string[] OfficialAppProcessNames = ["GoXLR App", "GoXLR Beta App"];

    public static IEnumerable<string> CandidateDaemonPaths()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            yield return Path.Combine(root, "GoXLR Utility", "goxlr-daemon.exe");
            yield return Path.Combine(root, "GoXLR Utility", "goxlr-daemon");
        }
    }

    public static string? FindDaemonExecutable() =>
        CandidateDaemonPaths().FirstOrDefault(File.Exists);

    public static bool IsInstalled() => FindDaemonExecutable() is not null
        || Directory.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "GoXLR Utility"));

    public static bool IsDaemonRunning() =>
        DaemonProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);

    public static bool IsOfficialAppRunning() =>
        OfficialAppProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);

    /// <summary>
    /// Starts goxlr-daemon if installed and not already running.
    /// Does not stop the daemon on app exit — other tools may rely on it.
    /// </summary>
    public static async Task<UtilityStartResult> TryStartDaemonAsync(
        TimeSpan? waitForReady = null,
        CancellationToken cancellationToken = default,
        Action<string>? log = null)
    {
        if (IsOfficialAppRunning())
            return UtilityStartResult.OfficialAppConflict;

        if (IsDaemonRunning())
            return UtilityStartResult.AlreadyRunning;

        var exe = FindDaemonExecutable();
        if (exe is null)
            return UtilityStartResult.NotInstalled;

        try
        {
            log?.Invoke($"Starte Utility-Daemon: {exe}");
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
                return UtilityStartResult.StartFailed;

            var deadline = DateTime.UtcNow + (waitForReady ?? TimeSpan.FromSeconds(15));
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDaemonRunning())
                {
                    // Give the named pipe a moment to come up after process start.
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    log?.Invoke("Utility-Daemon läuft.");
                    return UtilityStartResult.Started;
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            log?.Invoke("Utility-Daemon gestartet, aber nicht rechtzeitig bereit.");
            return UtilityStartResult.StartFailed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Daemon-Start fehlgeschlagen: {ex.Message}");
            return UtilityStartResult.StartFailed;
        }
    }
}

public enum UtilityStartResult
{
    AlreadyRunning,
    Started,
    NotInstalled,
    OfficialAppConflict,
    StartFailed
}
