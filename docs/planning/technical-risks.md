# Technische Risiken

| Risiko | Schwere | Status | Mitigation |
|---|---|---|---|
| Official App vs Utility | Hoch | LOCALLY: App aktiv | Wizard/Status |
| Utility nicht installiert | Hoch | LOCALLY | winget-Anleitung |
| Keine Raw-Fader-Axis | Mittel | SOURCE-CONFIRMED | Channel-Volumes |
| Mute Side-Effects | Mittel | SOURCE-CONFIRMED | Docs; später neutralize |
| HTTP/WS disabled | Mittel | DOCUMENTED | Wizard; Poll-Fallback |
| Startup Latch | Mittel | Issues | Keine Writes bis Bewegung |
| Session EventContext | Mittel | NAudio-Lücke | COM-Shim |
| UIPI Admin Games | Mittel | DOCUMENTED | Docs |
| API Schema Drift | Mittel | — | Fixtures @ 1.2.4 |
| Discord State | Niedrig | — | Command-sent only |
