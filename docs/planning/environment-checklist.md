# Environment-Checkliste (lokal)

**Datum:** 2026-09-25

| Check | Ergebnis |
|---|---|
| OS | Windows 10.0.26200 (win-x64) |
| .NET SDK | 8.0.424 |
| WindowsDesktop Runtime | 8.0.30 (+ 10.0.9 Host) |
| Repo | Leer → Docs/Solution werden angelegt |
| GoXLR Mini Endpoint | Vorhanden (Soundgerät OK) |
| Weitere Audio-Geräte | Realtek USB, NVIDIA, … |
| Official GoXLR App | **LÄUFT** — Blocker für Utility |
| GoXLR Utility | **NICHT INSTALLIERT** |
| Named Pipe `@goxlr.socket` | **Nicht vorhanden** |
| winget Utility | `GoXLR-on-Linux.GoXLR-Utility` 1.2.4 verfügbar |

## Nächste manuelle Schritte (Nutzer)

1. Offizielle GoXLR App beenden.
2. `winget install GoXLR-on-Linux.GoXLR-Utility`
3. Utility starten, Webserver aktiv lassen.
4. `GoXlrControl.DiagHost` ausführen und `docs/testing/hardware-validation.md` ausfüllen.
