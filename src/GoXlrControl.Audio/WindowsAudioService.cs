using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GoXlrControl.Abstractions;
using GoXlrControl.Config;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace GoXlrControl.Audio;

public sealed class WindowsAudioService : IVolumeSink, IDisposable
{
    public static readonly Guid AppEventContext = new("A7C0F8D2-4E91-4B6A-9C3E-1D2F3A4B5C6D");
    private static readonly TimeSpan ProcessCacheTtl = TimeSpan.FromSeconds(5);

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly ILogger<WindowsAudioService>? _logger;
    private readonly ConcurrentDictionary<int, CachedProcessInfo> _processCache = new();
    private bool _followDefault;
    private string? _explicitDeviceId;

    public WindowsAudioService(bool followDefault = true, string? explicitDeviceId = null, ILogger<WindowsAudioService>? logger = null)
    {
        _followDefault = followDefault;
        _explicitDeviceId = explicitDeviceId;
        _logger = logger;
    }

    public void Configure(bool followDefault, string? explicitDeviceId)
    {
        _followDefault = followDefault;
        _explicitDeviceId = explicitDeviceId;
    }

    public IReadOnlyList<AudioEndpointInfo> GetPlaybackEndpoints()
    {
        var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        return devices.Select(d => new AudioEndpointInfo(d.ID, d.FriendlyName, IsGoXlr(d.FriendlyName))).ToList();
    }

    public AudioEndpointInfo? GetDefaultPlayback()
    {
        try
        {
            var d = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new AudioEndpointInfo(d.ID, d.FriendlyName, IsGoXlr(d.FriendlyName));
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<AudioSessionInfo> GetSessions()
    {
        var result = new List<AudioSessionInfo>();
        MMDevice device;
        try
        {
            device = ResolveDevice();
        }
        catch
        {
            return result;
        }

        var sessions = device.AudioSessionManager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            try
            {
                if (s.IsSystemSoundsSession) continue;
                var pid = (int)s.GetProcessID;
                string? exe = null;
                string display = s.DisplayName;
                if (pid > 0 && TryGetProcessInfo(pid) is { } info)
                {
                    exe = info.Path ?? info.ProcessName;
                    if (string.IsNullOrWhiteSpace(display))
                        display = info.ProcessName;
                }

                result.Add(new AudioSessionInfo(
                    s.GetSessionIdentifier,
                    pid,
                    exe,
                    string.IsNullOrWhiteSpace(display) ? (exe ?? "Unknown") : display,
                    s.SimpleAudioVolume.Volume,
                    s.SimpleAudioVolume.Mute));
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Session skip");
            }
        }

        return result;
    }

    public Task SetMasterVolumeAsync(double normalized, CancellationToken ct = default)
    {
        var device = ResolveDevice();
        device.AudioEndpointVolume.NotificationGuid = AppEventContext;
        device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(normalized, 0, 1);
        return Task.CompletedTask;
    }

    public Task ToggleMasterMuteAsync(CancellationToken ct = default)
    {
        var device = ResolveDevice();
        device.AudioEndpointVolume.NotificationGuid = AppEventContext;
        device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
        return Task.CompletedTask;
    }

    public Task SetEndpointVolumeAsync(string deviceId, double normalized, CancellationToken ct = default)
    {
        using var device = _enumerator.GetDevice(deviceId);
        device.AudioEndpointVolume.NotificationGuid = AppEventContext;
        device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(normalized, 0, 1);
        return Task.CompletedTask;
    }

    public Task ToggleEndpointMuteAsync(string deviceId, CancellationToken ct = default)
    {
        using var device = _enumerator.GetDevice(deviceId);
        device.AudioEndpointVolume.NotificationGuid = AppEventContext;
        device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
        return Task.CompletedTask;
    }

    public Task SetApplicationVolumeAsync(AppIdentity identity, double normalized, bool aggregate, CancellationToken ct = default)
    {
        var volume = (float)Math.Clamp(normalized, 0, 1);
        foreach (var session in MatchSessions(identity))
        {
            try
            {
                session.SimpleAudioVolume.Volume = volume;
                if (!aggregate) break;
            }
            catch (COMException ex)
            {
                _logger?.LogDebug(ex, "Set session volume failed");
            }
        }

        return Task.CompletedTask;
    }

    public Task ToggleApplicationMuteAsync(AppIdentity identity, CancellationToken ct = default)
    {
        var matched = MatchSessions(identity).ToList();
        if (matched.Count == 0) return Task.CompletedTask;
        var mute = !matched[0].SimpleAudioVolume.Mute;
        foreach (var session in matched)
        {
            try { session.SimpleAudioVolume.Mute = mute; }
            catch (COMException) { /* ignore */ }
        }

        return Task.CompletedTask;
    }

    public Task<double> GetMasterVolumeAsync(CancellationToken ct = default)
    {
        var device = ResolveDevice();
        return Task.FromResult((double)device.AudioEndpointVolume.MasterVolumeLevelScalar);
    }

    public Task<double?> GetApplicationVolumeAsync(AppIdentity identity, CancellationToken ct = default)
    {
        var session = MatchSessions(identity).FirstOrDefault();
        return Task.FromResult(session is null ? (double?)null : session.SimpleAudioVolume.Volume);
    }

    public Task<bool> GetMasterMuteAsync(CancellationToken ct = default)
    {
        var device = ResolveDevice();
        return Task.FromResult(device.AudioEndpointVolume.Mute);
    }

    public Task<bool?> GetApplicationMuteAsync(AppIdentity identity, CancellationToken ct = default)
    {
        var session = MatchSessions(identity).FirstOrDefault();
        return Task.FromResult(session is null ? (bool?)null : session.SimpleAudioVolume.Mute);
    }

    public Task<double> GetMasterPeakAsync(CancellationToken ct = default)
    {
        try
        {
            var device = ResolveDevice();
            return Task.FromResult(Math.Clamp((double)device.AudioMeterInformation.MasterPeakValue, 0, 1));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Endpoint peak failed");
            return Task.FromResult(0.0);
        }
    }

    public Task<double> GetApplicationPeakAsync(AppIdentity identity, CancellationToken ct = default)
    {
        double peak = 0;
        foreach (var session in MatchSessions(identity))
        {
            try
            {
                peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Session peak failed");
            }
        }

        return Task.FromResult(Math.Clamp(peak, 0, 1));
    }

    private IEnumerable<AudioSessionControl> MatchSessions(AppIdentity identity)
    {
        MMDevice device;
        try { device = ResolveDevice(); }
        catch { yield break; }

        var sessions = device.AudioSessionManager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.IsSystemSoundsSession) continue;
            if (Matches(s, identity))
                yield return s;
        }
    }

    private bool Matches(AudioSessionControl session, AppIdentity identity)
    {
        try
        {
            return identity.Kind switch
            {
                AppIdentityKind.SessionIdPrefix =>
                    session.GetSessionIdentifier.StartsWith(identity.Value, StringComparison.OrdinalIgnoreCase),
                AppIdentityKind.ExePath => MatchesExe(session, identity.Value),
                AppIdentityKind.Aumid =>
                    session.GetSessionIdentifier.Contains(identity.Value, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private bool MatchesExe(AudioSessionControl session, string exePathOrName)
    {
        try
        {
            var pid = (int)session.GetProcessID;
            if (pid <= 0) return false;
            if (TryGetProcessInfo(pid) is not { } info)
                return false;

            if (info.Path is not null && info.Path.Equals(exePathOrName, StringComparison.OrdinalIgnoreCase))
                return true;
            return info.ProcessName.Equals(Path.GetFileNameWithoutExtension(exePathOrName), StringComparison.OrdinalIgnoreCase)
                   || (info.Path is not null && Path.GetFileName(info.Path).Equals(Path.GetFileName(exePathOrName), StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private CachedProcessInfo? TryGetProcessInfo(int pid)
    {
        var now = DateTimeOffset.UtcNow;
        if (_processCache.TryGetValue(pid, out var cached) && now - cached.CachedAt < ProcessCacheTtl)
            return cached;

        try
        {
            using var proc = Process.GetProcessById(pid);
            var info = new CachedProcessInfo(SafeGetPath(proc), proc.ProcessName, now);
            _processCache[pid] = info;
            return info;
        }
        catch
        {
            _processCache.TryRemove(pid, out _);
            return null;
        }
    }

    private MMDevice ResolveDevice()
    {
        if (!_followDefault && !string.IsNullOrEmpty(_explicitDeviceId))
            return _enumerator.GetDevice(_explicitDeviceId!);

        return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static string? SafeGetPath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    private static bool IsGoXlr(string friendlyName) =>
        friendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _enumerator.Dispose();

    private readonly record struct CachedProcessInfo(string? Path, string ProcessName, DateTimeOffset CachedAt);
}

public sealed record AudioEndpointInfo(string Id, string FriendlyName, bool IsGoXlr);
public sealed record AudioSessionInfo(
    string SessionIdentifier,
    int ProcessId,
    string? ExecutablePath,
    string DisplayName,
    float Volume,
    bool Muted);
