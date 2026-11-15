param(
    [string]$LegacySdkSource = '.sdk/source/VRCFaceTracking',
    [string]$NativeSdkSource = '.sdk/compat/source/vrcft6-steam',
    [string]$ModuleDll,
    [string]$InstalledHostDir
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
function ProjectPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $root $Path))
}
$hosts = @(
    @{Name='vrcft5'; Source=(ProjectPath $LegacySdkSource); Commit='7add14873e398c4095d06913cdeee7a01fc43fab'; Native='false'},
    @{Name='vrcft6'; Source=(ProjectPath $NativeSdkSource); Commit='3d36379253d26961560d84acecd01d509aea467a'; Native='true'}
)
foreach ($hostInfo in $hosts) {
    $commit = & git -C $hostInfo.Source rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $commit -cne $hostInfo.Commit) { throw "Expected $($hostInfo.Name) source at $($hostInfo.Commit)" }
    $dirty = & git -C $hostInfo.Source status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw "Official source has tracked changes: $($hostInfo.Source)" }
}
if ($InstalledHostDir) {
    $InstalledHostDir = [IO.Path]::GetFullPath($InstalledHostDir)
    foreach ($name in @('VRCFaceTracking.Core','VRCFaceTracking.SDK','VRCFaceTracking.ModuleProcess')) {
        $file = Join-Path $InstalledHostDir "$name.dll"
        if ([Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString() -ne '6.0.0.0' -or
            [Diagnostics.FileVersionInfo]::GetVersionInfo($file).ProductVersion -notlike "*+$($hosts[1].Commit)") {
            throw "Installed components do not match the pinned Steam 6 host: $file"
        }
    }
}
if (-not $ModuleDll) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -SdkSource $hosts[0].Source
    $ModuleDll = Join-Path $root 'bin/Release/net10.0/UnifiedPicoModule.dll'
} else { $ModuleDll = ProjectPath $ModuleDll }
$run = Join-Path $root ('artifacts/compat/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $run | Out-Null
Copy-Item $ModuleDll (Join-Path $run 'UnifiedPicoModule.dll')
$reports = @()
foreach ($hostInfo in $hosts) {
    $output = Join-Path $run $hostInfo.Name
    New-Item -ItemType Directory -Path $output | Out-Null
    $buildLog = Join-Path $output 'build.log'
    & dotnet build (Join-Path $PSScriptRoot 'CompatibilityTests/CompatibilityTests.csproj') -c Release `
        --artifacts-path $output "-p:HostSource=$($hostInfo.Source)" "-p:ModuleDll=$(Join-Path $run 'UnifiedPicoModule.dll')" `
        "-p:NativeHost=$($hostInfo.Native)" --nologo *> $buildLog
    if ($LASTEXITCODE -ne 0) { Get-Content $buildLog -Tail 25; throw "Compatibility build failed: $buildLog" }
    $bin = Join-Path $output 'bin/CompatibilityTests/release'
    if ($hostInfo.Native -eq 'true' -and $InstalledHostDir) {
        foreach ($file in @('VRCFaceTracking.Core.dll','VRCFaceTracking.SDK.dll','VRCFaceTracking.ModuleProcess.dll','fti_osc.dll')) {
            Copy-Item (Join-Path $InstalledHostDir $file) $bin -Force
        }
    }
    $profile = Join-Path $output 'profile'
    New-Item -ItemType Directory -Path $profile | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.ArgumentList.Add((Join-Path $bin 'CompatibilityTests.dll'))
    $start.ArgumentList.Add((Join-Path $output 'results.json'))
    $start.WorkingDirectory = $profile
    $start.Environment['APPDATA'] = $profile; $start.Environment['LOCALAPPDATA'] = $profile
    $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw "Compatibility timeout: $($hostInfo.Name)" }
    $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    $text | Set-Content (Join-Path $output 'tests.log') -Encoding utf8
    Write-Host $text
    $code = $process.ExitCode; $process.Dispose()
    if ($code -ne 0) { throw "Compatibility tests failed: $output" }
    $reports += @{host=$hostInfo.Name; result=(Join-Path $output 'results.json')}
}
@{module=$ModuleDll; installedHost=$InstalledHostDir; hosts=$reports} | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $run 'summary.json') -Encoding utf8
Write-Host "Compatibility evidence: $run"
Write-Host 'No daily host, headset, production tracking port or user profile was used.'
