# Сборка boardswitcher для Windows: exe и установщик.
#   .\build.ps1            — exe (publish\boardswitcher.exe) и установщик (installer\Output\)
#   .\build.ps1 -NoInstaller — только exe
param([switch]$NoInstaller)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet publish -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o publish
if ($LASTEXITCODE -ne 0) { throw "Ошибка сборки exe" }

if ($NoInstaller) { return }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Не найден Inno Setup 6 (ISCC.exe). Установите: winget install JRSoftware.InnoSetup" }

& $iscc /Q installer\boardswitcher.iss
if ($LASTEXITCODE -ne 0) { throw "Ошибка сборки установщика" }
Get-ChildItem installer\Output\*.exe | Select-Object Name, Length
