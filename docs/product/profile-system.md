# Profilsystem

## Zweck

Alle Controller-Zuordnungen versioniert und portabel speichern.

## Operationen

Create, Duplicate, Rename, Delete, Activate, Export, Import, Reset-to-defaults.

## Sicherheitsregeln beim Wechsel

- Keine Massen-Volume-Writes
- Soft-Takeover zurücksetzen
- Keine Button-Actions feuern
- Ungültige Targets → „unassigned/missing“ statt Crash

## Beispielprofile (Vorlagen, nicht hartcodiert)

| Name | Idee |
|---|---|
| Desktop | Master, Browser, Music, Comms |
| Gaming | Master, Game, Discord Playback, Music |
| Editing | Browser, Editor, Music, Comms |

## Export-Format

JSON, `schemaVersion`, UTF-8. Import validiert und migriert.
