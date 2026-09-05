[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Smoke')]
    [ValidatePattern('^(win|linux|osx)-(x64|arm64)$')]
    [string]$Rid,

    [Parameter(Mandatory = $true, ParameterSetName = 'Smoke')]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedCommitSha,

    [Parameter(Mandatory = $true, ParameterSetName = 'Smoke')]
    [ValidateSet('Windows', 'Linux', 'macOS')]
    [string]$RunnerOs,

    [Parameter(Mandatory = $true, ParameterSetName = 'Smoke')]
    [ValidateSet('X64', 'ARM64')]
    [string]$RunnerArchitecture,

    [Parameter(ParameterSetName = 'Smoke')]
    [ValidateRange(1, 60)]
    [int]$StartupTimeoutSeconds = 8,

    [Parameter(ParameterSetName = 'Smoke')]
    [ValidateRange(1, 60)]
    [int]$ShutdownTimeoutSeconds = 12,

    [Parameter(ParameterSetName = 'SelfTest', Mandatory = $true)]
    [switch]$SelfTest
)

# This script is CI-only packaging evidence. It never changes production
# composition and never captures application output into retained artifacts.
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Get-RidTarget([string]$Value) {
    $parts = $Value.Split('-', 2)
    $targetOs = switch ($parts[0]) {
        'win' { 'Windows' }
        'linux' { 'Linux' }
        'osx' { 'macOS' }
        default { throw 'Unsupported startup-smoke RID family.' }
    }

    [pscustomobject]@{ Os = $targetOs; Architecture = $parts[1] }
}

function Get-CurrentHost {
    $os = if ([System.OperatingSystem]::IsWindows()) {
        'Windows'
    } elseif ([System.OperatingSystem]::IsMacOS()) {
        'macOS'
    } elseif ([System.OperatingSystem]::IsLinux()) {
        'Linux'
    } else {
        'Unsupported'
    }

    $architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
        'X64' { 'x64' }
        'Arm64' { 'arm64' }
        default { 'unsupported' }
    }

    [pscustomobject]@{ Os = $os; Architecture = $architecture }
}

function Assert-ChildPath([string]$Root, [string]$Candidate) {
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $resolvedCandidate = [IO.Path]::GetFullPath($Candidate)
    $prefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCandidate.StartsWith($prefix, [StringComparison]::Ordinal)) {
        throw 'Startup-smoke path escaped its controlled artifact directory.'
    }

    return $resolvedCandidate
}

function New-Report([string]$Status, [string]$Reason, [string]$ErrorCode, $Target, $Host) {
    [ordered]@{
        schema_version = 1
        evidence_class = 'E4 Packaging/actual matching CI host'
        artifact_rid = $Rid
        source_sha = $ExpectedCommitSha
        runner_os_declared = $RunnerOs
        runner_architecture_declared = $RunnerArchitecture
        host_os_observed = $Host.Os
        host_architecture_observed = $Host.Architecture
        target_os = $Target.Os
        target_architecture = $Target.Architecture
        matching_host = ($Target.Os -eq $Host.Os -and $Target.Architecture -eq $Host.Architecture)
        status = $Status
        result_reason = $Reason
        error_code = $ErrorCode
        self_contained = $true
        direct_apphost_no_dotnet_guard = 'NOT RUN'
        startup_observation = 'NOT RUN'
        cleanup = 'NOT RUN'
    }
}

function Write-SafeReport($Report, [string]$Path) {
    $json = $Report | ConvertTo-Json -Depth 4
    if ($json -match '(?i)-----BEGIN [A-Z ]*PRIVATE KEY-----|VPSREADY_(SEEDED|TEST)_SECRET|vpsready-seeded-secret-do-not-export|(password|passphrase|token|secret)\s*[=:]') {
        throw 'Startup-smoke report safety policy rejected unsafe content.'
    }

    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Stop-SmokeProcess([Diagnostics.Process]$Process, [int]$TimeoutSeconds) {
    if ($Process.HasExited) {
        return 'PROCESS_EXITED_AFTER_BOUNDED_SHUTDOWN'
    }

    if ([System.OperatingSystem]::IsWindows()) {
        [void]$Process.CloseMainWindow()
    } else {
        $signalStartInfo = [Diagnostics.ProcessStartInfo]::new()
        $signalStartInfo.FileName = '/bin/kill'
        $signalStartInfo.Arguments = "-TERM $($Process.Id)"
        $signalStartInfo.UseShellExecute = $false
        $signalStartInfo.RedirectStandardOutput = $true
        $signalStartInfo.RedirectStandardError = $true
        $signal = [Diagnostics.Process]::Start($signalStartInfo)
        $signal.WaitForExit(5000) | Out-Null
    }

    if ($Process.WaitForExit($TimeoutSeconds * 1000)) {
        return 'PROCESS_EXITED_AFTER_BOUNDED_SHUTDOWN'
    }

    $Process.Kill($true)
    if ($Process.WaitForExit(5000)) {
        return 'PROCESS_TREE_CLEANED_AFTER_BOUNDED_SHUTDOWN'
    }

    throw 'Startup-smoke process tree did not terminate during bounded cleanup.'
}

if ($SelfTest) {
    $linux = Get-RidTarget 'linux-x64'
    $windows = Get-RidTarget 'win-arm64'
    if ($linux.Os -ne 'Linux' -or $linux.Architecture -ne 'x64' -or $windows.Os -ne 'Windows' -or $windows.Architecture -ne 'arm64') {
        throw 'Startup-smoke target normalization self-test failed.'
    }

    $safeFixture = [ordered]@{ status = 'NOT_RUN'; result_reason = 'RUNNER_ARCHITECTURE_MISMATCH'; error_code = 'NONE' } | ConvertTo-Json
    if ($safeFixture -match 'VPSREADY_(SEEDED|TEST)_SECRET|-----BEGIN [A-Z ]*PRIVATE KEY-----') {
        throw 'Startup-smoke report sanitization self-test failed.'
    }

    Write-Host 'Startup-smoke self-test passed: bounded target mapping and sanitized NOT_RUN reporting are configured.'
    exit 0
}

$target = Get-RidTarget $Rid
$host = Get-CurrentHost
$smokeRoot = Join-Path $repositoryRoot 'artifacts/startup-smoke'
$ridDirectory = Assert-ChildPath $smokeRoot (Join-Path $smokeRoot $Rid)
$reportPath = Join-Path $ridDirectory 'startup-smoke-report.json'

if (Test-Path -LiteralPath $ridDirectory) {
    Remove-Item -LiteralPath $ridDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $ridDirectory | Out-Null

$report = New-Report 'FAIL' 'STARTUP_SMOKE_NOT_COMPLETED' 'PACKAGING_STARTUP_SMOKE_FAILED' $target $host
$process = $null

try {
    $archive = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'artifacts/packages') -File -Filter "VPSReady-*-$Rid-$ExpectedCommitSha.zip")
    if ($archive.Count -ne 1) {
        throw 'Expected exactly one SHA-tied package archive for startup smoke.'
    }

    $sidecar = "$($archive[0].FullName).sha256"
    if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) {
        throw 'Startup-smoke archive checksum sidecar is missing.'
    }

    $sidecarLine = [IO.File]::ReadAllText($sidecar).Trim()
    if ($sidecarLine -notmatch '^([0-9a-f]{64})  ([^/\\\r\n]+\.zip)$' -or $Matches[2] -ne $archive[0].Name) {
        throw 'Startup-smoke archive checksum sidecar is malformed.'
    }

    $actualArchiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive[0].FullName).Hash.ToLowerInvariant()
    if ($actualArchiveHash -ne $Matches[1]) {
        throw 'Startup-smoke archive checksum does not match its sidecar.'
    }

    $extracted = Assert-ChildPath $ridDirectory (Join-Path $ridDirectory 'extracted')
    [IO.Compression.ZipFile]::ExtractToDirectory($archive[0].FullName, $extracted)
    $manifestPath = Join-Path $extracted 'artifact-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'Startup-smoke archive manifest is missing.'
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.source_sha -ne $ExpectedCommitSha -or $manifest.artifact_rid -ne $Rid -or $manifest.self_contained -ne $true -or $manifest.evidence.built -ne 'PASS') {
        throw 'Startup-smoke archive manifest did not prove the expected self-contained package identity.'
    }

    $report.archive_sha256 = $actualArchiveHash
    $report.app_version = [string]$manifest.app_version

    if ($target.Os -ne $host.Os) {
        $report.status = 'NOT_RUN'
        $report.result_reason = 'RUNNER_OS_MISMATCH'
        $report.error_code = 'NONE'
        Write-SafeReport $report $reportPath
        Write-Host "Startup smoke NOT RUN for $Rid: matching host OS is unavailable."
        exit 0
    }

    if ($target.Architecture -ne $host.Architecture) {
        $report.status = 'NOT_RUN'
        $report.result_reason = 'RUNNER_ARCHITECTURE_MISMATCH'
        $report.error_code = 'NONE'
        Write-SafeReport $report $reportPath
        Write-Host "Startup smoke NOT RUN for $Rid: matching host architecture is unavailable."
        exit 0
    }

    $executableName = if ($target.Os -eq 'Windows') { 'VpsReady.Desktop.exe' } else { 'VpsReady.Desktop' }
    $executable = Join-Path $extracted $executableName
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'Startup-smoke apphost is missing from the extracted package.'
    }

    $shimDirectory = Assert-ChildPath $ridDirectory (Join-Path $ridDirectory 'no-dotnet')
    New-Item -ItemType Directory -Force -Path $shimDirectory | Out-Null
    $shim = if ($target.Os -eq 'Windows') { Join-Path $shimDirectory 'dotnet.cmd' } else { Join-Path $shimDirectory 'dotnet' }
    if ($target.Os -eq 'Windows') {
        [IO.File]::WriteAllText($shim, "@echo off`r`nexit /b 77`r`n", [Text.UTF8Encoding]::new($false))
    } else {
        [IO.File]::WriteAllText($shim, "#!/bin/sh`nexit 77`n", [Text.UTF8Encoding]::new($false))
        & /bin/chmod 700 $shim
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the no-dotnet startup guard.' }
    }

    & $shim
    if ($LASTEXITCODE -ne 77) {
        throw 'The no-dotnet startup guard did not fail closed.'
    }

    $report.direct_apphost_no_dotnet_guard = 'PASS'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = $extracted
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment['PATH'] = $shimDirectory + [IO.Path]::PathSeparator + [Environment]::GetEnvironmentVariable('PATH')
    $missingDotnetRoot = Join-Path $shimDirectory 'missing-dotnet-runtime'
    $startInfo.Environment['DOTNET_ROOT'] = $missingDotnetRoot
    $startInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'

    if ($target.Os -eq 'Linux') {
        $xvfb = Get-Command xvfb-run -ErrorAction SilentlyContinue
        if ($null -eq $xvfb) {
            $report.status = 'NOT_RUN'
            $report.result_reason = 'HEADLESS_DISPLAY_UNAVAILABLE'
            $report.error_code = 'NONE'
            Write-SafeReport $report $reportPath
            Write-Host "Startup smoke NOT RUN for $Rid: no contained headless display launcher is available."
            exit 0
        }

        $startInfo.FileName = $xvfb.Source
        [void]$startInfo.ArgumentList.Add('-a')
        [void]$startInfo.ArgumentList.Add($executable)
    } else {
        $startInfo.FileName = $executable
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Startup-smoke process did not start.'
    }

    Start-Sleep -Seconds $StartupTimeoutSeconds
    $process.Refresh()
    if ($process.HasExited) {
        throw 'Startup-smoke process exited before bounded liveness observation.'
    }

    $report.startup_observation = 'PROCESS_RUNNING_AFTER_BOUNDED_WAIT'
    $cleanup = Stop-SmokeProcess $process $ShutdownTimeoutSeconds

    $report.status = 'PASS'
    $report.result_reason = 'DIRECT_SELF_CONTAINED_APPHOST_STARTED'
    $report.error_code = 'NONE'
    $report.cleanup = $cleanup
    Write-SafeReport $report $reportPath
    Write-Host "Startup smoke PASS for $Rid: direct self-contained apphost remained running and exited after bounded shutdown."
} catch {
    $report.status = 'FAIL'
    $report.result_reason = 'STARTUP_SMOKE_FAILED'
    $report.error_code = 'PACKAGING_STARTUP_SMOKE_FAILED'
    $report.cleanup = if ($null -ne $process -and -not $process.HasExited) { 'FORCED_CLEANUP_REQUIRED' } else { 'NOT_REQUIRED' }
    Write-SafeReport $report $reportPath
    throw 'PACKAGING_STARTUP_SMOKE_FAILED: see the sanitized startup-smoke report artifact.'
} finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(5000) | Out-Null
    }
}
