# Discord-Integration

## Zwei Kontrollarten

| Typ | Mechanismus | Status |
|---|---|---|
| A — Discord Playback Volume | Windows Audio Session von Discord | MVP (aktiv) |
| B — Mic Mute / Deafen | `IDiscordIntegration` — Default **FALLBACK** (SendInput-Keybinds) | MVP (aktiv) |
| Confirmed Mute-State | Social SDK Read und/oder Local RPC (partner-genehmigt) | **Approval-gated** |

## Architektur

Die App nutzt `IDiscordIntegration` mit drei Betriebsmodi:

| Mode | Read | Write | Verfügbarkeit ohne Discord-Approval |
|---|---|---|---|
| **FALLBACK** (Default) | `UNKNOWN` | Shortcuts (`SendInput`) | Sofort |
| **HYBRID** | Social SDK `GetVoiceSettings` / Callback (wenn genehmigt) | Shortcuts | Gated |
| **NATIVE** | Local RPC `GET_VOICE_SETTINGS` + `VOICE_SETTINGS_UPDATE` | Local RPC `SET_VOICE_SETTINGS` | Gated (`rpc.voice.write`) |

**Regeln:** Geschätzten State nie als `Confirmed` anzeigen. Ohne Verifikation: `Unknown`. Nach unseren Shortcuts (oder manuellem Sync): `Mirrored` — GoXLR-LEDs dürfen dem Mirror folgen, UI kennzeichnet „Mirrored (LED)“. Live-Read aus Discord nur nach genehmigter API (`Confirmed`).

---

## Feasibility: Social SDK ≥ 1.10.19337

Quellen:

- [Social SDK Release Notes 1.10.19337](https://discord.com/developers/docs/social-sdk/release_notes.html) (2026-09-01)
- [`Client::GetVoiceSettings`](https://discord.com/developers/docs/social-sdk/classdiscordpp_1_1Client.html) / `SetVoiceSettingsUpdatedCallback`
- [`VoiceSettings`](https://discord.com/developers/docs/social-sdk/classdiscordpp_1_1VoiceSettings.html) (`SelfMute` / `SelfDeaf`)

| Kriterium | Ergebnis |
|---|---|
| Desktop-Client Mute/Deafen **lesen** | Ja — read-only Zugriff auf Voice Settings des Discord-Desktop-Clients |
| Desktop-Client Mute/Deafen **schreiben** | Nein — dokumentiert als read-only |
| `Call::SetSelfMute` / `SetSelfMuteAll` | Steuern **SDK-managed Lobby-Calls**, nicht den bestehenden Desktop-Call |
| Autorisierung | „Requires a running Discord desktop client and an **approved Social SDK integration**.“ |
| Plattform | Offiziell C++ / Unity / Unreal; kein offizielles standalone C# für WPF |
| Urteil | Geeignet als autorisierter **READ**-Pfad für HYBRID — erst nach Approval. Kein NATIVE-Write-Ersatz. |

---

## Feasibility: Discord Local RPC

Quellen:

- [RPC Topic](https://docs.discord.com/developers/topics/rpc) — `GET_VOICE_SETTINGS`, `SET_VOICE_SETTINGS`, `VOICE_SETTINGS_UPDATE`
- [OAuth2 Scopes](https://docs.discord.com/developers/topics/oauth2) — `rpc.voice.read` / `rpc.voice.write` **„only available to approved partners“**

| Kriterium | Ergebnis |
|---|---|
| Desktop Read | `GET_VOICE_SETTINGS` → `mute` / `deaf` |
| Desktop Write | `SET_VOICE_SETTINGS` (sperrt Voice Settings solange verbunden) |
| Scopes | Partner-Approval erforderlich; nicht für beliebige Apps freischaltbar |
| Urteil | Technisch korrekt für NATIVE Read+Write — **nicht shippable** ohne Partner-Approval. Keine Live-Calls auf Restricted Scopes ohne Portal-Bestätigung. |

Siehe Approval-Checkliste: [`docs/planning/discord-approval-checklist.md`](../planning/discord-approval-checklist.md).

---

## Keybinds (FALLBACK)

Quelle: [Discord Keybinds Support](https://support.discord.com/hc/en-us/articles/217083547-How-do-I-add-different-Keybinds)

- **Toggle Mute** / **Toggle Deafen** in Discord Desktop
- Nur Desktop-App (nicht Browser)
- Gleiche Combos in Control Studio und Discord
- Deafen deaktiviert laut Discord auch das Mikrofon

### Input-Injection

- `SendInput` (Win32), volle Key-Down/Up-Sequenzen
- Risiken: UIPI (Admin-Spiele), Discord geschlossen, Keybind-Settings-Seite offen

## State-Modell

| Reliability | Bedeutung | Mute/Deafen-Felder |
|---|---|---|
| `Unknown` | Kein verifizierter Discord-State | `Unknown` |
| `Mirrored` | Lokaler Mirror nach Shortcut/Sync (für LEDs) | On/Off |
| `CommandSent` | Shortcut abgesetzt (Legacy/ohne Mirror-Update) | oft noch `Unknown` |
| `Confirmed` | Discord bestätigt (nur nach genehmigter API) | On/Off |

## Konfiguration

```text
AppSettings {
  discordMuteChord / discordDeafenChord
  discordIntegrationMode: Fallback | Hybrid | Native  // Default Fallback
  discordClientId?: string                            // erst nach Approval genutzt
}
```

Runtime-Gate `DiscordAuthorizationGate.AuthorizedApisEnabled` (Default `false`) verhindert Hybrid/Native-Backends ohne validierte Freigabe.
