# GoXLR Control Studio

Inoffizielle Windows-Desktop-Anwendung, die eine **TC-Helicon GoXLR Mini** über die [GoXLR Utility](https://github.com/GoXLR-on-Linux/goxlr-utility) als programmierbaren Hardware-Controller nutzt.

> Nicht affiliated mit TC-Helicon / Music Tribe oder dem GoXLR Utility Projekt.

## Was es tut

- Fader steuern Windows-Master- und Anwendungs-Lautstärke
- Buttons lösen Mute, Media-Keys, Shortcuts (z. B. Discord Mute/Deafen) aus
- Profile lokal speichern
- **Kein** Audio-Routing über die GoXLR erforderlich

## Voraussetzungen

1. Windows 10/11 x64
2. Offizielle GoXLR-Treiber (bereits vorhanden, wenn Official App je installiert war)
3. [GoXLR Utility 1.2.4+](https://github.com/GoXLR-on-Linux/goxlr-utility/releases) — **nicht** parallel zur Official App  
   Die App kann die Utility selbst installieren (winget → GitHub-Installer) und den Daemon automatisch starten. Die Utility-UI musst du nicht manuell öffnen.
4. .NET 8 (bei self-contained Build nicht nötig)

## Schnellstart (Entwicklung)

```powershell
dotnet build GoXlrControl.sln
dotnet test GoXlrControl.sln
dotnet run --project tools/GoXlrControl.DiagHost
dotnet run --project src/GoXlrControl.App
```

Simulation ohne Hardware: Settings → **Simulated Hardware** aktivieren und App neu starten. DiagHost fragt beim Start nach Simulation.

## Publish

```powershell
.\packaging\publish.ps1
```

Optional Installer: Inno Setup 6 mit `packaging/GoXlrControlStudio.iss`.

## Bekannte Einschränkungen

- GoXLR Utility und offizielle GoXLR App schließen sich gegenseitig aus.
- Fader liefern Channel-Volumen (0–255), keine Rohachsen.
- Discord Mute/Deafen = Shortcut „Command sent“, kein bestätigter Discord-State.
- Physische Hardware-Validierung steht lokal noch aus, solange Utility nicht installiert ist (siehe `docs/testing/hardware-validation.md`).
- Lighting / OBS / Auto-Profile-Switch sind Post-MVP.

## Dokumentation

Siehe [`docs/`](docs/) — Architektur, Forschung, MVP, Tests (Deutsch).

## Lizenz

Siehe `LICENSE` (MIT).
