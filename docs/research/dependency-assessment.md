# Abhängigkeitsbewertung

## Externe Runtime-Abhängigkeiten

| Abhängigkeit | Pflicht | Lizenz / Hinweis | Bundling |
|---|---|---|---|
| .NET 8 Desktop (oder self-contained) | Ja | Microsoft | Self-contained Publish bevorzugt |
| GoXLR Utility 1.2.4+ | Ja | Unofficial OSS | Nicht bundlen; **Patcher** installiert via winget/GitHub; Daemon-Auto-Start |
| TC-Helicon GoXLR Treiber | Ja (USB) | Hersteller | Nicht bundlen; Link zu offiziellen Quellen |
| Offizielle GoXLR App | Nein | Konflikt mit Utility | Wizard fordert Beenden |

## NuGet (geplant)

| Paket | Zweck |
|---|---|
| CommunityToolkit.Mvvm | MVVM |
| Microsoft.Extensions.Hosting | DI / Lifetime |
| NAudio.Wasapi | Core Audio |
| JsonPatch.Net | RFC 6902 Patches |
| Serilog (+ Sink File) | Logging |
| WPF-UI (optional) | Fluent Dark Theme |

## Konflikte

1. **Official App ↔ Utility:** Hard Mutex im Utility-Daemon.
2. Elgato Stream Deck GoXLR Plugin: steuert typisch Official App / Utility-Audio — parallel beobachten, kein Blocker für Control Studio solange Utility API frei bleibt.
3. Utility HTTP Remote Access: Standard lokal (`127.0.0.1`); Netzwerkzugriff nicht aktivieren.

## Branding

Kein Anspruch auf TC-Helicon-/GoXLR-Utility-Offiziellstatus. Name „GoXLR Control Studio“ = Arbeitsname; UI-Disclaimer: inoffizielle Third-Party-Software.
