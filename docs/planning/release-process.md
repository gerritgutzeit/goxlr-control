# Release-Prozess

## Primärweg: Velopack + GitHub Actions

1. Version in `src/GoXlrControl.App/GoXlrControl.App.csproj` → `<Version>` anpassen (z. B. `0.2.0`).
2. Optional lokal: `RepositoryUrl` im csproj auf dein GitHub-Repo setzen (Updater liest das).
3. Committen und taggen:

```powershell
git add -A
git commit -m "release: v0.2.0"
git tag v0.2.0
git push origin HEAD --tags
```

4. Workflow **Release** baut self-contained win-x64, packt mit Velopack (`vpk`) und lädt Assets auf die GitHub Release.
5. Nutzer laden die **Setup.exe** aus den Release-Assets.
6. Installierte Apps prüfen beim Start Releases; bei neuer Version erscheint der Button **Update**.

Tag-Format: `vX.Y.Z` (muss zur csproj-Version passen).

## Lokal packen (ohne CI)

```powershell
dotnet test GoXlrControl.sln -c Release
.\packaging\publish.ps1
.\packaging\velopack-pack.ps1
```

Ausgabe: `artifacts/releases/` (Setup + nupkg + Manifest).

`vpk` einmalig: `dotnet tool install -g vpk --version 1.2.158`

## Legacy: Inno Setup (ohne Auto-Update)

Nach `.\packaging\publish.ps1` optional Inno Setup 6 mit `packaging/GoXlrControlStudio.iss` → `artifacts/installer/`.
Nur für manuelle Installer ohne Velopack-Updater.

## Checkliste

- [ ] Tests grün
- [ ] `<Version>` und Tag `vX.Y.Z` identisch
- [ ] Release-Assets enthalten Setup.exe + RELEASES / assets Manifest
- [ ] Hardware-Protokoll aktualisiert (falls Geräte-Release)
- [ ] README Known Limitations aktuell
- [ ] Utility-Version in docs/research erwähnt
- [ ] Keine Secrets in Diagnostic-Samples
