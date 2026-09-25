# Publish self-contained win-x64 build (folder — required for Velopack)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$out = Join-Path $root "artifacts\app"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$version = (Select-Xml -Path "src\GoXlrControl.App\GoXlrControl.App.csproj" -XPath "//Version").Node.InnerText
if (-not $version) { $version = "0.1.0" }

dotnet publish "src\GoXlrControl.App\GoXlrControl.App.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:Version=$version `
  -o $out

Write-Host "Published v$version to $out"
Write-Host "Next: .\packaging\velopack-pack.ps1"
Write-Host "Legacy (ohne Auto-Update): Inno Setup packaging\GoXlrControlStudio.iss"
