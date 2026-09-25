# Discord-Integration

## Zwei Kontrollarten

| Typ | Mechanismus | MVP |
|---|---|---|
| A — Discord Playback Volume | Windows Audio Session von Discord | Ja |
| B — Mic Mute / Deafen | Discord Desktop Keybinds via `SendInput` | Ja |
| Confirmed Mute-State | Discord Local RPC + OAuth | Nein (Post-MVP) |

## Keybinds (DOCUMENTED)

Quelle: [Discord Keybinds Support](https://support.discord.com/hc/en-us/articles/217083547-How-do-I-add-different-Keybinds)

- **Toggle Mute** / **Toggle Deafen** konfigurierbar in Discord Desktop
- Nur Desktop-App (nicht Browser)
- Nutzer muss dieselben Combos in Control Studio und Discord setzen
- Deafen deaktiviert laut Discord auch das Mikrofon

## Input-Injection

- `SendInput` (Win32) — empfohlen
- Volle Key-Down/Up-Sequenzen inkl. Modifier
- Kein Feststecken von Modifiern (try/finally Release)

### Einschränkungen

| Szenario | Risiko |
|---|---|
| Spiel als Admin, App nicht | UIPI kann Injection blockieren |
| Discord geschlossen | Keybind greift nicht |
| Keybind-Settings-Seite offen | Discord deaktiviert Keybinds |

## State-Modell (MVP)

| Zustand | Bedeutung | UI |
|---|---|---|
| Command sent | Shortcut abgesetzt | Impuls / „gesendet“ |
| Estimated | lokaler Toggle nach Send | Nur mit Disclaimer |
| Confirmed | Discord bestätigt | **Nicht im MVP** |

Hardware-LEDs dürfen geschätzten Mute **nicht** als bestätigt darstellen.

## Konfiguration

```text
DiscordShortcuts {
  muteCombo: KeyChord
  deafenCombo: KeyChord
}
```

Wizard/Settings erklären Spiegelung in Discord User Settings → Keybinds.
