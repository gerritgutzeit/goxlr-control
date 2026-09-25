# Event-Processing

## Quellen

1. Hardware Patches (Fader Volumes, `button_down`)
2. Windows Audio Notifications (Endpoint/Session)
3. UI-Konfigurationsänderungen
4. Reconnect / Sleep-Resume

## Fader-Pipeline

```text
Patch → resolve fader via channel assignment → normalize 0..1
  → apply invert/min/max/curve → SoftTakeover gate
  → coalesce (latest wins, ~8–16ms) → ActionDispatcher → WASAPI
```

- Buttons: **kein** Coalescing; Debounce ~30–50 ms gegen Bounce
- Performance-Ziel: Median Event→Write &lt; 40 ms (messen in M3)

## Soft Takeover (Default)

Solange `|hardware - target| > deadZone` und Fader den Target-Wert noch nicht gekreuzt hat → ignorieren. Nach Cross: Absolute Follow bis Pause/Profilwechsel.

## Feedback-Vermeidung

| Loop | Mitigation |
|---|---|
| Utility Echo nach eigenem SetVolume | Ignore-Window / Command-Token |
| WASAPI Echo nach eigenem Set | EventContext-GUID |
| Profil-Load | Keine Massenschreib-Operationen; nur Mapping aktivieren |

## Reconnect

```text
Disconnected → Backoff(250ms..10s) → Pipe GetStatus → WS → Resync
→ Devices empty? NoDevice : Connected
```

Nach Resync: Soft-Takeover zurücksetzen (kein Force-Write).
