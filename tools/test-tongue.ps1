param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) { $ArtifactsPath = Join-Path $root ('artifacts/tongue/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
New-Item -ItemType Directory -Path $ArtifactsPath -Force | Out-Null
& dotnet build (Join-Path $PSScriptRoot 'TongueTests/TongueTests.csproj') -c Release --artifacts-path $ArtifactsPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tongue tests build failed; prepare the pinned SDK with tools/build.ps1 first.' }
$dll = Join-Path $ArtifactsPath 'bin/TongueTests/release/TongueTests.dll'
& dotnet $dll (Join-Path $ArtifactsPath 'results.json')
if ($LASTEXITCODE -ne 0) { throw 'Tongue tests failed.' }
Write-Host "Tongue test results: $ArtifactsPath"
Write-Host 'Only ephemeral loopback ports were used; no headset, host installation or user configuration changes.'
