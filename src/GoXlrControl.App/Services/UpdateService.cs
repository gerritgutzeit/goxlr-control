using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;

namespace GoXlrControl.App.Services;

/// <summary>
/// Checks GitHub Releases via Velopack and applies updates on demand.
/// </summary>
public sealed partial class UpdateService : ObservableObject
{
    public const string DefaultGitHubRepoUrl = "https://github.com/gerritgutzeit/goxlr-control";
    public const string VelopackToolVersion = "1.2.158";

    private readonly DiagnosticLogAdapter _log;
    private UpdateManager? _manager;
    private UpdateInfo? _pending;

    public UpdateService(Action<string>? log = null)
    {
        _log = new DiagnosticLogAdapter(log);
    }

    [ObservableProperty] private bool updateAvailable;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? availableVersion;
    [ObservableProperty] private string? currentVersion;
    [ObservableProperty] private bool isInstalledRelease;

    public string GitHubRepoUrl { get; } = ResolveRepoUrl();

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var mgr = CreateManager();
            _manager = mgr;
            CurrentVersion = mgr.CurrentVersion?.ToString() ?? GetAssemblyVersion();
            IsInstalledRelease = mgr.IsInstalled;

            if (!mgr.IsInstalled)
            {
                _log.Info("Update-Check übersprungen (kein Velopack-Install — z. B. Debug/dotnet run).");
                return;
            }

            _log.Info($"Prüfe Updates ({GitHubRepoUrl})…");
            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (info is null)
            {
                UpdateAvailable = false;
                AvailableVersion = null;
                _pending = null;
                _log.Info("Keine neue Version verfügbar.");
                return;
            }

            _pending = info;
            AvailableVersion = info.TargetFullRelease.Version.ToString();
            UpdateAvailable = true;
            _log.Info($"Update verfügbar: {AvailableVersion}");
        }
        catch (Exception ex)
        {
            _log.Warn($"Update-Check fehlgeschlagen: {ex.Message}");
            UpdateAvailable = false;
            _pending = null;
        }
    }

    public async Task ApplyUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || _pending is null || !UpdateAvailable)
            return;

        IsBusy = true;
        try
        {
            _log.Info($"Lade Update {_pending.TargetFullRelease.Version}…");
            await _manager.DownloadUpdatesAsync(_pending, progress: null).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _log.Info("Update installieren und neu starten…");
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch (Exception ex)
        {
            _log.Warn($"Update fehlgeschlagen: {ex.Message}");
            IsBusy = false;
        }
    }

    private UpdateManager CreateManager()
    {
        var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
        return new UpdateManager(source);
    }

    private static string ResolveRepoUrl()
    {
        var env = Environment.GetEnvironmentVariable("GOXLR_CONTROL_GITHUB_REPO");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim().TrimEnd('/');

        var asm = Assembly.GetExecutingAssembly();
        var meta = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
        if (!string.IsNullOrWhiteSpace(meta))
            return meta.Trim().TrimEnd('/');

        return DefaultGitHubRepoUrl;
    }

    private static string GetAssemblyVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private sealed class DiagnosticLogAdapter(Action<string>? sink)
    {
        public void Info(string message) => sink?.Invoke(message);
        public void Warn(string message) => sink?.Invoke("WARN " + message);
    }
}
