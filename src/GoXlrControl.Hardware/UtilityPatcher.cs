using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GoXlrControl.Hardware;

/// <summary>
/// Installs GoXLR Utility via winget, falling back to the official GitHub NSIS installer.
/// </summary>
public sealed class UtilityPatcher
{
    public const string WingetPackageId = "GoXLR-on-Linux.GoXLR-Utility";
    public const string MinimumVersion = "1.2.4";
    public const string GitHubOwner = "GoXLR-on-Linux";
    public const string GitHubRepo = "goxlr-utility";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GoXlrControlStudio", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<UtilityPatchResult> EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (UtilityDaemonLifecycle.IsInstalled())
        {
            progress?.Report("GoXLR Utility ist bereits installiert.");
            return UtilityPatchResult.AlreadyInstalled;
        }

        if (UtilityDaemonLifecycle.IsOfficialAppRunning())
        {
            progress?.Report("Offizielle GoXLR App läuft — bitte beenden, dann erneut versuchen.");
            return UtilityPatchResult.OfficialAppConflict;
        }

        progress?.Report("Versuche Installation über winget…");
        var winget = await TryWingetInstallAsync(progress, cancellationToken).ConfigureAwait(false);
        if (winget == UtilityPatchResult.Installed || UtilityDaemonLifecycle.IsInstalled())
        {
            progress?.Report("GoXLR Utility über winget installiert.");
            return UtilityPatchResult.Installed;
        }

        progress?.Report("winget nicht verfügbar oder fehlgeschlagen — lade GitHub-Installer…");
        return await TryGitHubInstallAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UtilityPatchResult> EnsureReadyAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!UtilityDaemonLifecycle.IsInstalled())
        {
            var install = await EnsureInstalledAsync(progress, cancellationToken).ConfigureAwait(false);
            if (install is not (UtilityPatchResult.Installed or UtilityPatchResult.AlreadyInstalled))
                return install;
        }

        var start = await UtilityDaemonLifecycle.TryStartDaemonAsync(
            cancellationToken: cancellationToken,
            log: m => progress?.Report(m)).ConfigureAwait(false);

        return start switch
        {
            UtilityStartResult.AlreadyRunning or UtilityStartResult.Started => UtilityPatchResult.Ready,
            UtilityStartResult.OfficialAppConflict => UtilityPatchResult.OfficialAppConflict,
            UtilityStartResult.NotInstalled => UtilityPatchResult.NotInstalled,
            _ => UtilityPatchResult.StartFailed
        };
    }

    public static string? SelectWindowsInstallerUrl(string releaseJson)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        if (!doc.RootElement.TryGetProperty("assets", out var assets))
            return null;

        string? best = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Contains("macos", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!asset.TryGetProperty("browser_download_url", out var urlProp))
                continue;

            var url = urlProp.GetString();
            if (string.IsNullOrWhiteSpace(url))
                continue;

            // Prefer goxlr-utility-<version>.exe over other Windows exes.
            if (name.StartsWith("goxlr-utility-", StringComparison.OrdinalIgnoreCase))
                return url;
            best ??= url;
        }

        return best;
    }

    private async Task<UtilityPatchResult> TryWingetInstallAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var winget = FindWinget();
        if (winget is null)
        {
            progress?.Report("winget nicht gefunden.");
            return UtilityPatchResult.WingetUnavailable;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = winget,
                Arguments =
                    $"install --id {WingetPackageId} --exact --accept-package-agreements --accept-source-agreements --disable-interactivity",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return UtilityPatchResult.WingetUnavailable;

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(stdout))
                progress?.Report(Truncate(stdout));
            if (!string.IsNullOrWhiteSpace(stderr))
                progress?.Report(Truncate(stderr));

            // 0 = success, -1978335189 (0x8A15002B) often means already installed
            if (process.ExitCode == 0 || UtilityDaemonLifecycle.IsInstalled())
                return UtilityPatchResult.Installed;

            progress?.Report($"winget ExitCode {process.ExitCode}");
            return UtilityPatchResult.InstallFailed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report($"winget Fehler: {ex.Message}");
            return UtilityPatchResult.WingetUnavailable;
        }
    }

    private async Task<UtilityPatchResult> TryGitHubInstallAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            progress?.Report("GitHub Releases abfragen…");
            using var response = await Http.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var downloadUrl = SelectWindowsInstallerUrl(json);
            if (downloadUrl is null)
            {
                progress?.Report("Kein Windows-Installer in GitHub Releases gefunden.");
                return UtilityPatchResult.InstallFailed;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "GoXlrControlStudio");
            Directory.CreateDirectory(tempDir);
            var installerPath = Path.Combine(tempDir, "goxlr-utility-setup.exe");

            progress?.Report($"Lade {downloadUrl}…");
            await using (var remote = await Http.GetStreamAsync(downloadUrl, cancellationToken).ConfigureAwait(false))
            await using (var file = File.Create(installerPath))
                await remote.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

            progress?.Report("Installer starten (ggf. UAC bestätigen)…");
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/S",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process is null)
                return UtilityPatchResult.InstallFailed;

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // NSIS may finish before files are fully flushed; poll briefly.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline && !UtilityDaemonLifecycle.IsInstalled())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            if (UtilityDaemonLifecycle.IsInstalled())
            {
                progress?.Report("GoXLR Utility installiert.");
                return UtilityPatchResult.Installed;
            }

            progress?.Report($"Installer ExitCode {process.ExitCode} — Utility-Ordner nicht gefunden.");
            return UtilityPatchResult.InstallFailed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report($"GitHub-Installation fehlgeschlagen: {ex.Message}");
            return UtilityPatchResult.InstallFailed;
        }
    }

    private static string? FindWinget()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(local))
            return local;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "winget",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return File.Exists(line) ? line : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string text, int max = 400)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}

public enum UtilityPatchResult
{
    Ready,
    AlreadyInstalled,
    Installed,
    NotInstalled,
    OfficialAppConflict,
    WingetUnavailable,
    InstallFailed,
    StartFailed
}
