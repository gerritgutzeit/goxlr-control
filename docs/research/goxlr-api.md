# GoXLR Utility API — Forschung

**Stand:** 2026-09-25  
**Getestete Utility-Version (Ziel):** 1.2.4  
**Quellen:** [API-Wiki](https://github.com/GoXLR-on-Linux/goxlr-utility/wiki/The-GoXLR-Utility-API), Quellcode `ipc/`, `types/`, `daemon/`

## Validierungslegende

| Status | Bedeutung |
|---|---|
| DOCUMENTED | Explizit in Wiki/README |
| SOURCE-CONFIRMED | Im Utility-Quellcode verifiziert |
| LOCALLY TESTED | Auf diesem Rechner ausgeführt |
| REQUIRES HARDWARE VALIDATION | Braucht echte Mini + laufende Utility |
| UNKNOWN | Unzureichende Evidenz |

## Kommunikationsschnittstellen

### Named Pipe (primärer Discovery-Pfad)

| | |
|---|---|
| Windows-Name | `@goxlr.socket` (SOURCE-CONFIRMED: `daemon/src/servers/ipc_server.rs`) |
| Framing | `[u32 Big-Endian Länge][UTF-8 JSON]` |
| Semantik | Request/Response, gleiche JSON-Befehle wie HTTP/WS |
| .NET | `NamedPipeClientStream("@goxlr.socket")` — [offizielles C#-Gist](https://gist.github.com/FrostyCoolSlug/9ec991081964633d18f1421176a584d8) |
| Pipe-Name exakt unter diesem Windows | REQUIRES HARDWARE VALIDATION |

### HTTP (optional)

- `POST /api/command` mit JSON-Body
- Default-Port: **14564**
- Abschaltbar (`--http-disable` / UI-Einstellung)
- Discovery über `Status.config.http_settings` nach `GetStatus`

### WebSocket (Live-Updates)

- Endpoint: `/api/websocket`
- Request: `{ "id": <u32>, "data": <DaemonRequest> }`
- Response: `{ "id": <u32>, "data": <DaemonResponse> }`
- Zusätzlich: JSON-Patch (`DaemonResponse::Patch`) bei Statusänderungen
- Responses können asynchron und umgeordnet eintreffen → Request-ID-Korrelation Pflicht

### CLI

- `goxlr-client` / `goxlr-client-quiet` (typisch unter `C:\Program Files\GoXLR Utility\`)
- Nicht als Primärpfad für Live-Events

## Verbindungsablauf (Windows)

1. Utility läuft; offizielle TC-Helicon-App **nicht** (gegenseitiger Ausschluss, SOURCE-CONFIRMED).
2. Named Pipe öffnen, `"GetStatus"` senden.
3. `Status.config.http_settings` lesen (`enabled`, `bind_address`, `port`).
4. Bei `enabled`: WebSocket verbinden, erneut `GetStatus`, Status-Cache halten.
5. eingehende Patches auf Cache anwenden (JsonPatch.Net).

**Hinweis:** Wiki erwähnt `GetHttpState` — im aktuellen `DaemonRequest` **nicht** vorhanden. HTTP-State nur über `Status.config.http_settings` (SOURCE-CONFIRMED).

## Geräteidentifikation

```text
mixers.<SERIAL>.hardware.device_type   // "Mini" | "Full" | "Unknown"
mixers.<SERIAL>.hardware.serial_number
```

USB: Mini VID `0x1220` / PID `0x8fe4` (SOURCE-CONFIRMED).

Mini-spezifisch: `effects`/`sampler`/`scribble` typisch `null`.

## Relevante Statusfelder

| Pfad | Bedeutung |
|---|---|
| `fader_status.A\|B\|C\|D.channel` | Zugewiesener `ChannelName` |
| `fader_status.*.mute_state` | `Unmuted` / `MutedToX` / `MutedToAll` |
| `levels.volumes.<ChannelName>` | `u8` 0–255 |
| `button_down.<Button>` | `bool` gedrückt |
| `cough_button` | Cough-Konfiguration/State |
| `lighting` | Farben/Styles |
| `config.daemon_version` | Utility-Version |

### ChannelName (SOURCE-CONFIRMED)

`Mic`, `LineIn`, `Console`, `System`, `Game`, `Chat`, `Sample`, `Music`, `Headphones`, `MicMonitor`, `LineOut`

### Mini-Buttons (SOURCE-CONFIRMED)

`Fader1Mute`, `Fader2Mute`, `Fader3Mute`, `Fader4Mute`, `Bleep`, `Cough`

## Beispielbefehle

```json
"GetStatus"
```

```json
{ "Command": ["SERIAL", { "SetFader": ["A", "System"] }] }
```

```json
{ "Command": ["SERIAL", { "SetVolume": ["Music", 128] }] }
```

```json
{ "Command": ["SERIAL", { "SetButtonColours": ["Fader1Mute", "00FF00", "003300"] }] }
```

## Verhalten bei Störungen

| Ereignis | Verhalten | Status |
|---|---|---|
| Utility-Restart | Pipe/WS weg; Client reconnect + `GetStatus` | SOURCE-CONFIRMED |
| USB Disconnect | Mixer-Eintrag verschwindet (Patch) | SOURCE-CONFIRMED |
| HTTP disabled | Pipe bleibt; keine Live-Patches | DOCUMENTED |
| Official App startet | Utility beendet sich | SOURCE-CONFIRMED |

## Lokaler Environment-Check (2026-09-25)

| Check | Ergebnis |
|---|---|
| .NET SDK 8 | LOCALLY TESTED — 8.0.424 |
| GoXLR Mini Soundgerät | LOCALLY TESTED — „TC-HELICON GoXLR Mini“ OK |
| Offizielle GoXLR App | LOCALLY TESTED — läuft (`GoXLR App.exe`) |
| GoXLR Utility installiert | LOCALLY TESTED — **fehlt** |
| Named Pipe vorhanden | LOCALLY TESTED — **fehlt** (Utility nicht aktiv) |
| winget Utility 1.2.4 | LOCALLY TESTED — `GoXLR-on-Linux.GoXLR-Utility` verfügbar |
