# Release-Prozess

## Version bump

1. `src/GoXlrControl.App/GoXlrControl.App.csproj` → `<Version>`
2. `packaging/GoXlrControlStudio.iss` → `#define MyAppVersion`

## Build

```powershell
dotnet test GoXlrControl.sln -c Release
.\packaging\publish.ps1
```

## Installer

Inno Setup 6: `packaging/GoXlrControlStudio.iss` kompilieren → `artifacts/installer/`.

## Checkliste

- [ ] Tests grün
- [ ] Hardware-Protokoll aktualisiert (falls Geräte-Release)
- [ ] README Known Limitations aktuell
- [ ] Utility-Version in docs/research erwähnt
- [ ] Keine Secrets in Diagnostic-Samples
