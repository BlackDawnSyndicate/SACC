# Runs the core tests, then publishes SACC as a single-file exe and zips it into dist/.
# Usage: ./build.ps1            (framework-dependent; needs the .NET 8 Desktop Runtime)
#        ./build.ps1 -SelfContained   (bundles the runtime; larger, runs anywhere on win-x64)
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet test tests/Sacc.Core.Tests --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

$version = ([xml](Get-Content src/Sacc.App/Sacc.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$flavor = if ($SelfContained) { 'win-x64-selfcontained' } else { 'win-x64' }
$out = "dist/SACC-$version-$flavor"
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue

dotnet publish src/Sacc.App -c Release -r win-x64 --self-contained:$($SelfContained.IsPresent) `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Copy-Item README.md $out
if (Test-Path LICENSE) { Copy-Item LICENSE $out }
$zip = "$out.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$out/*" -DestinationPath $zip
Write-Host "Built $zip"
