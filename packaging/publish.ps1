# Publish self-contained win-x64 build
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$out = Join-Path $root "artifacts\app"
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet publish "src\GoXlrControl.App\GoXlrControl.App.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o $out

Write-Host "Published to $out"
Write-Host "Optional: compile packaging\GoXlrControlStudio.iss with Inno Setup 6"
