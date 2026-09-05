[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(win|linux|osx)-(x64|arm64)$')]
    [string]$Rid,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$CommitSha,

    [Parameter(Mandatory = $true)]
    [string]$RunnerOs,

    [Parameter(Mandatory = $true)]
    [string]$RunnerArchitecture
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$profiles = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'packaging-profiles.psd1')
$matchingProfiles = @($profiles.Profiles | Where-Object { $_.Rid -eq $Rid })
if ($matchingProfiles.Count -ne 1) {
    throw "No unique packaging profile exists for $Rid."
}

$profile = $matchingProfiles[0]
if ($profile.ExpectedRunnerOs -ne $RunnerOs -or -not $profile.SelfContained -or $profile.ArchiveExtension -ne 'zip') {
    throw "Packaging profile for $Rid does not permit this self-contained ZIP on $RunnerOs."
}

$appVersion = (& dotnet msbuild 'src/VpsReady.Desktop/VpsReady.Desktop.csproj' -nologo -getProperty:Version | Select-Object -Last 1).Trim()
if ($appVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
    throw 'The package version is not a safe version identifier.'
}

$publishDirectory = Join-Path $repositoryRoot "artifacts/publish/$Rid"
$packagesDirectory = Join-Path $repositoryRoot 'artifacts/packages'
$archiveName = "$($profiles.ProductName)-$appVersion-$Rid-$CommitSha.zip"
$archivePath = Join-Path $packagesDirectory $archiveName

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publishDirectory
New-Item -ItemType Directory -Force -Path $publishDirectory, $packagesDirectory | Out-Null

& dotnet publish 'src/VpsReady.Desktop/VpsReady.Desktop.csproj' `
    --configuration Release `
    --no-restore `
    --runtime $Rid `
    --self-contained true `
    -p:VpsReadyBuildSha=$CommitSha `
    -p:Version=$appVersion `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $Rid."
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/development/THIRD_PARTY_NOTICES.md') -Destination (Join-Path $publishDirectory 'THIRD_PARTY_NOTICES.md') -Force
Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -File -Recurse | Remove-Item -Force

$noticePath = Join-Path $publishDirectory 'PACKAGE_NOTICE.md'
$notice = @(
    "$($profiles.ProductName) $appVersion candidate package",
    "Source SHA: $CommitSha",
    "Signing status: $($profiles.Signing.Status)",
    $profiles.Signing.Warning,
    'Evidence: built and archive-inspected only; startup was not run.'
) -join [Environment]::NewLine
$notice | Set-Content -NoNewline -Encoding utf8 $noticePath

$files = Get-ChildItem -File -Recurse $publishDirectory | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($publishDirectory.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        bytes = $_.Length
    }
}

$manifest = [ordered]@{
    schema_version = 1
    product = $profiles.ProductName
    app_version = $appVersion
    source_sha = $CommitSha
    artifact_rid = $Rid
    bundle_kind = $profile.BundleKind
    self_contained = $profile.SelfContained
    build_host_os = $RunnerOs
    build_host_architecture = $RunnerArchitecture
    signing = [ordered]@{
        status = $profiles.Signing.Status
        warning = $profiles.Signing.Warning
    }
    evidence = [ordered]@{
        built = 'PASS'
        package_inspected = 'PASS'
        startup_smoked = $profiles.StartupEvidence
        real_vps = 'NOT TESTED'
    }
    files = @($files)
}

$manifestPath = Join-Path $publishDirectory 'artifact-manifest.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 5
if ($manifestJson.Contains($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package manifest must not contain a developer path.'
}
$manifestJson | Set-Content -NoNewline -Encoding utf8 $manifestPath

if (Test-Path $archivePath) {
    Remove-Item -Force $archivePath
}
[IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)

$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
Set-Content -NoNewline -Encoding ascii -Path "$archivePath.sha256" -Value "$archiveHash  $archiveName"

Write-Host "Published: $Rid ($appVersion)"
Write-Host "Package inspected: $archiveName"
Write-Host "Signing status: $($profiles.Signing.Status)"
Write-Host "Startup-smoked: $($profiles.StartupEvidence)"
Write-Host "Archive SHA-256: $archiveHash"
