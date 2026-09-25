# Pack Velopack release from artifacts\app
# Requires: dotnet tool install -g vpk --version 1.2.158
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$vpkVersion = "1.2.158"
$packId = "GoXlrControlStudio"
$mainExe = "GoXlrControlStudio.exe"
$packDir = Join-Path $root "artifacts\app"
$outDir = Join-Path $root "artifacts\releases"

if (-not (Test-Path (Join-Path $packDir $mainExe))) {
    Write-Error "Publish output missing. Run .\packaging\publish.ps1 first."
}

$version = $env:GOXLR_PACK_VERSION
if (-not $version) {
    $version = (Select-Xml -Path "src\GoXlrControl.App\GoXlrControl.App.csproj" -XPath "//Version").Node.InnerText
}
if (-not $version) { $version = "0.1.0" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    Write-Host "Installing vpk $vpkVersion…"
    dotnet tool install -g vpk --version $vpkVersion
}

$icon = Join-Path $root "src\GoXlrControl.App\Assets\app.ico"

vpk pack `
  --packId $packId `
  --packVersion $version `
  --packDir $packDir `
  --mainExe $mainExe `
  --packTitle "GoXLR Control Studio" `
  --packAuthors "GoXLR Control Studio Contributors" `
  --icon $icon `
  --outputDir $outDir

Write-Host "Velopack packages in $outDir"
Write-Host "Installer: look for Setup.exe / ${packId}-win-Setup.exe"
