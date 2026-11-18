# Builds dist\Auxer.exe and the installer dist\Auxer-Setup-<version>.exe.
# Run:  powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
# Needs the .NET 9 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$version = ([xml](Get-Content Auxer.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Building Auxer $version"

Remove-Item -Recurse -Force dist -ErrorAction SilentlyContinue
dotnet publish -c Release -o dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Find Inno Setup (6 or 7) wherever it was installed, via its uninstall entry, then the usual folders.
$uninstall = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
             'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
             'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
$fromRegistry = Get-ItemProperty $uninstall -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -like 'Inno Setup*' -and $_.InstallLocation } |
    Sort-Object DisplayName -Descending | ForEach-Object { Join-Path $_.InstallLocation 'ISCC.exe' }
$iscc = @($fromRegistry) + @('7', '6' | ForEach-Object {
        "$env:LOCALAPPDATA\Programs\Inno Setup $_\ISCC.exe", "$env:ProgramFiles\Inno Setup $_\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup $_\ISCC.exe" }) |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup not found. Install it with: winget install JRSoftware.InnoSetup" }
Write-Host "Using $iscc"

& $iscc /Q "/DAppVersion=$version" installer\Auxer.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

Get-ChildItem dist -File | Format-Table Name, @{ n = 'Size'; e = { '{0:N0} KB' -f ($_.Length / 1KB) } }
