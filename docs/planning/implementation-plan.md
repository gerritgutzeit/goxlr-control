# Implementierungsplan

Siehe auch `milestones.md`. Reihenfolge: Docs → DiagHost → Audio → Bridge → Buttons → UI → Discord → Profiles → Reliability → Packaging.

## Solution

`GoXlrControl.sln` mit Projekten laut `docs/architecture/system-overview.md`.

## Build

```powershell
dotnet build GoXlrControl.sln -c Release
dotnet test GoXlrControl.sln -c Release
dotnet publish src/GoXlrControl.App/GoXlrControl.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
```

## Entwicklungshinweise

1. Ohne Hardware: `SimulatedHardwareProvider` in Settings/DI wählen.
2. Vor Utility-Nutzung Official App beenden.
3. Fixtures unter `tests/Fixtures/` nach erstem echten `GetStatus` ergänzen.
