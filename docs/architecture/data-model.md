# Datenmodell

## Persistenzpfade

```text
%AppData%\GoXlrControlStudio\
  settings.json
  profiles\<id>.json
  logs\
```

## Schema (versioniert)

`schemaVersion` Integer; Migration in `SchemaMigrator`.

### AppSettings

| Feld | Typ | Default |
|---|---|---|
| schemaVersion | int | 1 |
| startWithWindows | bool | false |
| startMinimized | bool | false |
| closeToTray | bool | true |
| selectedDeviceSerial | string? | null |
| followDefaultPlayback | bool | true |
| selectedPlaybackDeviceId | string? | null |
| syncModeDefault | SoftTakeover\|Absolute | SoftTakeover |
| logLevel | string | Information |
| wizardCompleted | bool | false |
| discordMuteChord | string? | null |
| discordDeafenChord | string? | null |

### Profile

| Feld | Inhalt |
|---|---|
| id | GUID |
| name, description | string |
| faders | 4× FaderBinding |
| buttons | ButtonBinding[] |
| playbackDevicePreference | FollowDefault \| Explicit |

### FaderBinding

`faderId`, `label`, `target`, `min`/`max` (0–1), `invert`, `curve`, `syncMode`, `deadZone`

### FaderTarget (diskriminiert)

`None` | `MasterVolume` | `EndpointVolume` | `Application` | `ApplicationGroup` | `DiscordPlayback`

### AppIdentity

`kind` (`ExePath` | `SessionIdPrefix` | `Aumid`), `value`, `displayName`, `iconCacheKey`

### ButtonBinding

`buttonId`, `onPress`, `onLongPress?`, `onRelease?` → `ActionRef { type, parameters }`

## Runtime (nicht persistiert)

Device serial map, live session list, connection state, soft-takeover locks, command-sent pulses.
