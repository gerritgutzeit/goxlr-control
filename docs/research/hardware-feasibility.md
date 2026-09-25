# Hardware-Machbarkeit — GoXLR Mini als Controller

## Kernfrage

Können vier physische Fader und die Mini-Buttons zuverlässig als unabhängige Controller-Eingänge gelesen werden, **ohne** dass Windows-Audio über die GoXLR laufen muss?

**Antwort:** Ja, mit Einschränkungen. Die API exponiert **Kanalvolumen**, keine Roh-Analogachsen. Audio-Unabhängigkeit gilt, solange Windows andere Endpoints nutzt.

## Fader-Modell

| Annahme | Realität | Status |
|---|---|---|
| Raw fader position in JSON | **Nicht vorhanden** | SOURCE-CONFIRMED |
| Fader → Channel-Assignment | `fader_status.{A..D}.channel` | SOURCE-CONFIRMED |
| Wert | `levels.volumes[channel]` als `u8` 0–255 | DOCUMENTED + SOURCE-CONFIRMED |
| Events | JSON-Patch bei Änderung | DOCUMENTED |

### Strategie für 4 unabhängige Eingänge

1. Fader A–D auf **vier verschiedene** Channels legen: `System`, `Game`, `Chat`, `Music`.
2. Bei Volume-Patches den zugewiesenen Channel lesen und auf 0.0–1.0 normalisieren (`/ 255.0`).
3. Bei geänderter Channel-Zuweisung Mapping neu auflösen.
4. Doppelte Channel-Zuweisung: UI-Warnung; beide Fader teilen denselben Wert.

### Feedback / Echo

Eigene `SetVolume`/`SetFader`-Befehle erzeugen Patches. Kurzzeitiges Ignore-Fenster oder Herkunfts-Tag verhindert, dass die App eigene Änderungen als physische Bewegung interpretiert.

### Startup-Latch

Bekanntes Firmware-/Utility-Verhalten: Werte können bis zur ersten physischen Bewegung „tot“ wirken (Issues #236/#255). Bis zur ersten echten Bewegung **keine** Windows-Volume-Writes.

## Buttons

| Button | Erkennung | Side-Effect auf GoXLR |
|---|---|---|
| Fader1–4 Mute | `button_down` | Mute-State des Faderns |
| Bleep | `button_down` | Bleep-Audio intern |
| Cough | `button_down` + `cough_button` | Mic-Mute intern |

Press = `false→true`, Release = `true→false`. Long-Press über lokale Hold-Dauer.

**REQUIRES HARDWARE VALIDATION:** Debounce, Hold-Timing, Event-Rate.

## Lighting

Mini unterstützt Fader- und Button-Farben über Utility-Commands. Firmware-eigene Mute-Farben können kollidieren. Milestone 8; nicht MVP-kritisch.

## Audio-Unabhängigkeit

- Voraussetzung: GoXLR ist **nicht** Windows Default Playback/Recording.
- Faderbewegung ändert GoXLR-interne Channel-Volumes — unkritisch, wenn kein App-Audio über die Mini läuft.
- App darf **niemals** still Default-Devices auf die GoXLR umstellen.

## Proof-of-Concept (Milestone 1)

`tools/GoXlrControl.DiagHost`:

1. Pipe-Connect + `GetStatus`
2. Mini erkennen (`device_type == Mini`)
3. Vier Faderwerte + Channel-Zuweisung anzeigen
4. Button-Events mit Timestamp loggen
5. Connection-Status + Initial vs. Change unterscheiden
6. Event-Log exportieren

**Nicht als LOCALLY TESTED markieren**, bis physisch gegen Hardware gelaufen.

## Blocker vor Hardware-Test

1. Offizielle GoXLR App beenden
2. GoXLR Utility 1.2.4 installieren (`winget install GoXLR-on-Linux.GoXLR-Utility`)
3. Utility-Webserver aktiv lassen
4. Mini per USB verbunden
