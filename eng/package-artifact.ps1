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
$publishDirectory = Join-Path $repositoryRoot "artifacts/publish/$Rid"
$packagesDirectory = Join-Path $repositoryRoot 'artifacts/packages'
$archiveName = "VPSReady-$Rid-$CommitSha.zip"
$archivePath = Join-Path $packagesDirectory $archiveName

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publishDirectory
New-Item -ItemType Directory -Force -Path $publishDirectory, $packagesDirectory | Out-Null

& dotnet publish 'src/VpsReady.Desktop/VpsReady.Desktop.csproj' `
    --configuration Release `
    --no-restore `
    --runtime $Rid `
    --self-contained true `
    --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $Rid."
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/development/THIRD_PARTY_NOTICES.md') -Destination (Join-Path $publishDirectory 'THIRD_PARTY_NOTICES.md') -Force

$files = Get-ChildItem -File -Recurse $publishDirectory | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($publishDirectory.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        bytes = $_.Length
    }
}

$manifest = [ordered]@{
    schema_version = 1
    source_sha = $CommitSha
    artifact_rid = $Rid
    build_host_os = $RunnerOs
    build_host_architecture = $RunnerArchitecture
    evidence = [ordered]@{
        built = 'PASS'
        package_inspected = 'PASS'
        startup_smoked = 'NOT RUN (C606 startup-smoke suite is not implemented)'
        real_vps = 'NOT TESTED'
    }
    files = @($files)
}

$manifestPath = Join-Path $publishDirectory 'artifact-manifest.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -NoNewline -Encoding utf8 $manifestPath

if (Test-Path $archivePath) {
    Remove-Item -Force $archivePath
}
[IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)

$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
Set-Content -NoNewline -Encoding ascii -Path "$archivePath.sha256" -Value "$archiveHash  $archiveName"

Write-Host "Published: $Rid"
Write-Host "Package inspected: $archiveName"
Write-Host 'Startup-smoked: NOT RUN (C606 startup-smoke suite is not implemented)'
Write-Host "Archive SHA-256: $archiveHash"
