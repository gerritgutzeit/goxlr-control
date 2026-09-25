# Teststrategie

## Unit

Normalization, SoftTakeover, Debounce, Profile serialize/migrate, Patch apply, Reconnect state machine, Action registry.

## Integration

- Named Pipe / WebSocket mit Fixtures (aufgezeichnete Status/Patches)
- Audio Enumeration (wo Umgebung erlaubt)
- Request-ID-Korrelation

## Simulated Hardware

`SimulatedHardwareProvider` für UI und Engine ohne Gerät.

## Hardware (manuell, Evidenzpflicht)

Siehe `hardware-validation.md`. Nicht als bestanden markieren ohne Protokoll.

## App-Szenarien

Discord auf/zu/restart, Spotify, Multi-Session Browser, fehlende Apps, Game foreground, Default-Device-Wechsel.
