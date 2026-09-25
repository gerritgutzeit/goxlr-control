# Discord API Approval Checklist

Externe Freigabe für HYBRID/NATIVE. Code schifft **keine** Restricted Scopes (`rpc`, `rpc.voice.read`, `rpc.voice.write`), solange diese Liste nicht abgeschlossen und `DiscordAuthorizationGate.AuthorizedApisEnabled` nicht bewusst aktiviert ist.

## Zielmodi

| Mode | Benötigte Freigabe | Zweck |
|---|---|---|
| **HYBRID** | Social SDK: Desktop Voice Settings **read** (`GetVoiceSettings` / `SetVoiceSettingsUpdatedCallback`, SDK ≥ 1.10.19337) | Bestätigter Mute/Deafen-State; Write bleibt Shortcuts |
| **NATIVE** | Partner: `rpc.voice.read` **und** `rpc.voice.write` (Local RPC) | Read + Write des Discord-Desktop-Clients |

Social-SDK-Lobby-`Call::SetSelfMute` ist **kein** Ersatz für Desktop-Client-Steuerung.

## Schritte

1. [ ] Discord Application im [Developer Portal](https://discord.com/developers/applications) anlegen.
2. [ ] Social SDK Getting Started für die App aktivieren (falls verfügbar).
3. [ ] Redirect URI für Desktop setzen (`http://127.0.0.1/callback` o. ä.).
4. [ ] Client ID in `AppSettings.DiscordClientId` hinterlegen (noch nicht produktiv nutzen).
5. [ ] Discord kontaktieren (Account Representative / Partner Inquiry):
   - [ ] Social SDK Desktop Voice Settings **read** für diese App (Hardware-Controller, kein Game).
   - [ ] Optional separat: `rpc.voice.read` / `rpc.voice.write` für NATIVE Write.
6. [ ] Schriftliche / Portal-Bestätigung archivieren (Datum, Scope, App-ID).
7. [ ] Erst dann: Backend implementieren (Social SDK Interop bzw. Local RPC IPC).
8. [ ] `DiscordAuthorizationGate.AuthorizedApisEnabled = true` nur in Builds mit genehmigter Konfiguration.
9. [ ] Validierung:
   - [ ] Mute in Discord-UI spiegelt sich in der App (`Confirmed`).
   - [ ] Discord-Neustart → Resync / zwischenzeitlich `UNKNOWN`.
   - [ ] HYBRID: Shortcuts steuern weiterhin Mute/Deafen.
   - [ ] NATIVE: `SET_VOICE_SETTINGS`-Lock-Verhalten dokumentiert und akzeptabel.

## Explizit verboten bis Freigabe

- IPC `AUTHORIZE` mit `rpc` / `rpc.voice.read` / `rpc.voice.write`
- Geschätzten Mute als `Confirmed` anzeigen
- SDK-managed Voice-Call-Mute als Desktop-Mute ausgeben

## Referenzen

- [`docs/research/discord-integration.md`](../research/discord-integration.md)
- [OAuth2 Scopes](https://docs.discord.com/developers/topics/oauth2) — partner-only RPC scopes
- [RPC](https://docs.discord.com/developers/topics/rpc)
- [Social SDK Release Notes](https://discord.com/developers/docs/social-sdk/release_notes.html)
