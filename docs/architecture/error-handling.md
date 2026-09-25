# Fehlerbehandlung und Sicherheit

## Prinzipien

- Ein fehlgeschlagenes Subsystem darf die App nicht crashen.
- UI bleibt bei Disconnect bedienbar.
- Keine stillen gefährlichen Systemänderungen.

## Fehlerklassen

| Klasse | Reaktion |
|---|---|
| Malformed JSON / Patch | Log + ignore Patch; optional Resync |
| Pipe/WS Verlust | Reconnect-State + Backoff |
| Official App Conflict | Dedizierter Status + Anleitung |
| Audio Target missing | No-op + Diagnostics-Hinweis |
| SendInput failure | Log; UI „Befehl fehlgeschlagen“ |
| Corrupted Profile | Backup `.bak`, Default-Profil laden |
| Logging failure | App weiterlaufen lassen |

## Sicherheit

- Keine Cloud-Accounts für Kernfunktionen
- Keine Credentials in Klartext nötig
- Shortcuts nur explizit konfigurierte Chords
- Keine beliebigen Script-Executions im MVP
- Diagnostic Export: optionale Pfad-Redaktion; keine Mic-Samples
- Utility-API nur localhost

## Threat (lokal)

Lokaler Prozess mit User-Rechten kann ohnehin Volume/SendInput. Risiko = Fehlkonfiguration (falscher Shortcut) und Utility-Remote-Bind — Default lokal halten.
