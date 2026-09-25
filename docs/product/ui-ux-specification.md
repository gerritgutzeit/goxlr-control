# UI/UX-Spezifikation

## Visuelle Richtung

- Dark Default
- Restrained Accent (z. B. Teal/Cyan, kein Purple-Glow-Klischee)
- Moderne Controller-Console (Stream-Deck-Konfigurationsklarheit + Audio-Mixer-Lesbarkeit)
- Wenig Animation; Fokus auf Status und Zuordnung

## Navigation

Sidebar oder Top-Tabs: Dashboard · Fader · Buttons · Profile · Settings · Diagnostics

## Dashboard

- Device Name, Serial, Connection chips (Utility / Device)
- Aktives Profil
- Vier vertikale Fader (A–D), physische Mini-Anordnung
- Doppelanzeige: Hardware-Position vs. Software-Target (Differenz hervorheben)
- Button-Leiste mit Action-Labels
- App-Icons an Fader-Targets

## Fader-Konfiguration

Modal/Page: Target-Typ, Device, App-Picker (laufend + gespeichert), Label, Min/Max, Curve, Sync-Mode, Live-Monitor, Test-Button.

## Button-Konfiguration

Mini-Skizze (4 Mute + Bleep + Cough). Action-Kategorien. Shortcut-Capture (Keyboard Hook während Capture).

## Profiles / Settings / Diagnostics / Wizard

Entsprechend Plan Abschnitte 11–13. Wizard blockiert fortsetzen bei Official-App-Konflikt und fehlender Utility.

## Zustände

`Connected`, `Reconnecting`, `UtilityMissing`, `OfficialAppConflict`, `NoDevice`, `ControllerPaused`
