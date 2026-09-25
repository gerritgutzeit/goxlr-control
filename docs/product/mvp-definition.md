# MVP-Definition

## Kleinste nützliche vollständige App

1. Verbindung zur GoXLR Utility + Mini-Erkennung  
2. Vier Fader → Soft-Takeover → Windows Master/App Volume  
3. Programmierbare Mini-Buttons (Mute, Media, Shortcuts inkl. Discord)  
4. Profile speichern/laden  
5. Tray + Reconnect + First-Run Wizard + Diagnostics  

## Explizit außerhalb MVP

- Hardware Lighting Sync (M8)
- OBS
- Discord confirmed mute/deafen (HYBRID/NATIVE) — **approval-gated**, nicht angenommen
- Auto Profile Switching
- Direct USB Fallback
- Double-Press / Modifier-Makros

Discord Mute/Deafen im MVP = **FALLBACK** (`IDiscordIntegration` + Shortcuts). Confirmed State erst nach Discord Social-SDK-/Partner-Freigabe.

## Frühestes Teilprodukt

Ende Milestone 3: Bridge ohne volle UI (DiagHost/Bridge kann Volumes steuern).
