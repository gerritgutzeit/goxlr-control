# Windows Core Audio — Integration

## Ziel

Lautstärke und Mute für Endpoints und Anwendungs-Sessions steuern, **ohne** Audio-Routing zu ändern und ohne Admin-Rechte.

## APIs

| Bedarf | Interface | Status |
|---|---|---|
| Geräte enumerieren | `IMMDeviceEnumerator` | DOCUMENTED |
| Master Volume/Mute | `IAudioEndpointVolume` | DOCUMENTED |
| Sessions | `IAudioSessionManager2`, `IAudioSessionControl2` | DOCUMENTED |
| Session Volume/Mute | `ISimpleAudioVolume` | DOCUMENTED |
| Device-Änderungen | `IMMNotificationClient` | DOCUMENTED |
| Neue Sessions | `IAudioSessionNotification` | DOCUMENTED |

## Bibliothekswahl

**Primär:** `NAudio.Wasapi` (.NET 8 tauglich)  
**Ergänzung:** dünner COM-Shim für Session-`EventContext` (NAudio setzt bei Session-Volume oft `Guid.Empty`)  
**Nicht:** AudioSwitcher (enthält PolicyConfig / Default-Device-Setzen)

## App-Identität (Persistenz)

| Quelle | Verwendung |
|---|---|
| Executable-Pfad | Primärer persistenter Key |
| `GetSessionIdentifier` | Sekundär / Aggregation |
| PID | Nur transient |
| AUMID | Optional für Store/Host-Apps |

Mehrere Sessions derselben App (Browser/Electron) → aggregieren und Volumen auf alle schreiben.

## Feedback-Loops

Eigene App-GUID als `EventContext`. Eingehende Volume-Callbacks mit gleichem Context ignorieren.

## Exclusive Mode

`ISimpleAudioVolume` greift primär im Shared Mode. Exclusive-Apps: UI-Hinweis „eingeschränkt steuerbar“.

## Soft Takeover

Kein OS-Feature. App-Logik: Hardware-Wert erst übernehmen, wenn physischer Fader den Software-Zielwert kreuzt (Default). Absolute Mode optional.

## Verbote

- `IPolicyConfig` / `SetDefaultEndpoint`
- Stilles Umleiten auf GoXLR
- Audio-Capture / Recording von Mic- oder App-Streams
