# Komponenten-Design

## Hardware

### `IHardwareInputProvider`

```csharp
interface IHardwareInputProvider : IAsyncDisposable
{
    IObservable<HardwareConnectionState> Connection;
    IObservable<FaderValueChanged> FaderChanges;
    IObservable<ButtonStateChanged> ButtonChanges;
    IReadOnlyList<HardwareDeviceInfo> Devices { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
```

Implementierungen:

- `GoXlrUtilityProvider` — Produktion
- `SimulatedHardwareProvider` — UI/Tests

### Transport

- `NamedPipeTransport` — length-prefixed JSON
- `WebSocketStatusClient` — ID-Korrelation + Patch-Stream
- `DaemonStatusCache` — `JsonNode` + JsonPatch.Net

## Engine

- `MappingService` — aktives Profil
- `FaderProcessor` — Normalize, SoftTakeover/Absolute, Coalesce
- `ButtonProcessor` — Debounce, Press/LongPress/Release
- `ActionDispatcher` — `IActionExecutor` Registry

## Audio

- `IWindowsAudioService`
  - Endpoints enumerieren
  - Master/Endpoint Volume+Mute
  - Session discovery + aggregate set
  - Default-device follow
- `AppIdentityResolver`
- `EventContextFilter`

## Integrations

- `SendInputShortcutService`
- `MediaKeyService`
- `ProcessLaunchService`
- `DiscordShortcutActions` (nutzt Shortcut-Service)

## Config

- `SettingsStore`, `ProfileStore`, `SchemaMigrator`

## Diagnostics

- `IDiagnosticLog`, Report-Export (ohne Secrets/Pfade optional redacted)
