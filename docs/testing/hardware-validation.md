# Hardware-Validierungsprotokoll

**Status:** AUSSTEHEND — Utility nicht installiert / Official App aktiv (Stand 2026-09-25)

## Vorbereitung

- [ ] Official GoXLR App beendet
- [ ] GoXLR Utility 1.2.4 installiert und gestartet
- [ ] Webserver enabled
- [ ] Mini USB verbunden
- [ ] Windows Default Playback ≠ GoXLR
- [ ] DiagHost gestartet

## Tests

| ID | Test | Ergebnis | Evidenz |
|---|---|---|---|
| H1 | Mini Detection | | Timestamp/Log |
| H2 | Fader A–D Werte | | |
| H3 | Unique Channel Assign | | |
| H4 | Fast Fader Sweep Event-Rate | | |
| H5 | Alle Mini-Buttons Press/Release | | |
| H6 | USB Unplug/Replug | | |
| H7 | Utility Restart | | |
| H8 | Sleep/Resume | | |
| H9 | Pipe-Name `@goxlr.socket` | | |
| H10 | Latenz Event→Volume (M3) | | |
| H11 | Statusfarben: Mapping/Mute/Pause/Soft-Takeover sichtbar | | |
| H12 | PeakProxy reagiert auf Spotify/Browser ohne GoXLR-Routing | | |
| H13 | USB-Last / Flackern bei ~10 Hz PeakProxy akzeptabel | | |

## Lighting-Hinweise

- Firmware-`Meter`-Modus wird **nicht** für Windows-Peaks genutzt (nur GoXLR-Kanal-Audio).
- Erwartetes Feedback: `TwoColour`/`Gradient` über `SetFaderColours`.
- Discord-LEDs = nie „confirmed mute“.

## Notizen

_Hier Testergebnis nach erstem Hardware-Lauf eintragen._
