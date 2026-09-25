# Meilensteine

| ID | Ziel | Abhängigkeit | Acceptance |
|---|---|---|---|
| M0 | Docs + Environment | — | docs/ komplett; Blocker dokumentiert |
| M1 | API DiagHost | M0, Utility | Pipe/WS/Fader/Buttons geloggt (HW) |
| M2 | Audio PoC | M0 | Endpoint+Session Volume steuerbar |
| M3 | Bridge | M1+M2 | Soft-Takeover Fader→Windows |
| M4 | Buttons | M3 | Actions ohne Doppeltrigger |
| M5 | WPF UI + Wizard | M3–M4 | Konfiguration grafisch |
| M6 | Discord Shortcuts | M4 | Mute/Deafen Command-sent |
| M7 | Profiles | M5 | Persistenz Import/Export |
| M8 | Lighting | M5 | Optional Post-MVP |
| M9 | Tray/Reconnect | M5 | Background reliability |
| M10 | Tests/Packaging | MVP | Release-fähig |

MVP = M0–M7 + M9 (ohne M8).
