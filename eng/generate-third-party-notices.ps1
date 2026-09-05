[CmdletBinding()]
param(
    [string]$LockFile = (Join-Path $PSScriptRoot '../src/VpsReady.Desktop/packages.lock.json'),

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$PackagesRoot,

    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'

function Get-GlobalPackagesRoot {
    $line = (& dotnet nuget locals global-packages --list | Where-Object { $_ -match '^global-packages:' } | Select-Object -First 1)
    if ($line -notmatch '^global-packages:\s*(.+)$') {
        throw 'Could not resolve the NuGet global-packages location.'
    }

    return $Matches[1].Trim()
}

function Get-SingleNuspec([string]$PackageDirectory, [string]$PackageId, [string]$Version) {
    $files = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nuspec' -File)
    if ($files.Count -ne 1) {
        throw "Expected exactly one .nuspec for locked package $PackageId $Version."
    }

    return $files[0]
}

function Require-SafePackageLicenseFile([string]$PackageDirectory, [string]$FileName, [string]$PackageId, [string]$Version) {
    if ([string]::IsNullOrWhiteSpace($FileName) -or [IO.Path]::GetFileName($FileName) -ne $FileName) {
        throw "Locked package $PackageId $Version declares an unsafe license-file name."
    }

    $path = Join-Path $PackageDirectory $FileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Locked package $PackageId $Version is missing its declared license file."
    }

    return $path
}

$resolvedLock = [IO.Path]::GetFullPath($LockFile)
if (-not (Test-Path -LiteralPath $resolvedLock -PathType Leaf)) {
    throw 'Runtime lock file was not found.'
}

$resolvedPackagesRoot = if ([string]::IsNullOrWhiteSpace($PackagesRoot)) { Get-GlobalPackagesRoot } else { $PackagesRoot }
$resolvedPackagesRoot = [IO.Path]::GetFullPath($resolvedPackagesRoot)
if (-not (Test-Path -LiteralPath $resolvedPackagesRoot -PathType Container)) {
    throw 'The NuGet package cache is unavailable; restore locked runtime dependencies before generating notices.'
}

$lock = Get-Content -LiteralPath $resolvedLock -Raw | ConvertFrom-Json
$entries = @{}
foreach ($target in $lock.dependencies.PSObject.Properties) {
    if ($target.Name -ne 'net10.0' -and -not $target.Name.StartsWith('net10.0/', [StringComparison]::Ordinal)) {
        continue
    }

    foreach ($dependency in $target.Value.PSObject.Properties) {
        $package = $dependency.Value
        if ($package.type -eq 'Project') {
            continue
        }

        if ([string]::IsNullOrWhiteSpace($package.resolved) -or [string]::IsNullOrWhiteSpace($package.contentHash)) {
            throw "Locked runtime dependency $($dependency.Name) is missing resolved version or content hash."
        }

        $key = "$($dependency.Name)|$($package.resolved)|$($package.contentHash)"
        if (-not $entries.ContainsKey($key)) {
            $entries[$key] = [pscustomobject]@{ Id = $dependency.Name; Version = $package.resolved; ContentHash = $package.contentHash }
        }
    }
}

if ($entries.Count -eq 0) {
    throw 'The runtime lock contains no non-project net10.0 dependencies.'
}

$inventory = foreach ($entry in $entries.Values | Sort-Object Id, Version, ContentHash) {
    $packageDirectory = Join-Path $resolvedPackagesRoot (Join-Path $entry.Id.ToLowerInvariant() $entry.Version)
    if (-not (Test-Path -LiteralPath $packageDirectory -PathType Container)) {
        throw "Locked package $($entry.Id) $($entry.Version) is unavailable in the NuGet cache; restore before generating notices."
    }

    $nuspec = Get-SingleNuspec $packageDirectory $entry.Id $entry.Version
    [xml]$xml = Get-Content -LiteralPath $nuspec.FullName -Raw
    $metadata = $xml.package.metadata
    $license = $metadata.license
    if ($null -eq $license -or [string]::IsNullOrWhiteSpace($license.type) -or [string]::IsNullOrWhiteSpace($license.InnerText)) {
        throw "Locked package $($entry.Id) $($entry.Version) does not declare auditable license metadata."
    }

    $licenseFile = $null
    if ($license.type -eq 'file') {
        $licenseFile = Require-SafePackageLicenseFile $packageDirectory $license.InnerText.Trim() $entry.Id $entry.Version
    } elseif ($license.type -ne 'expression') {
        throw "Locked package $($entry.Id) $($entry.Version) has unsupported license metadata type '$($license.type)'."
    }

    $embeddedLicenseFiles = @(Get-ChildItem -LiteralPath $packageDirectory -File -Filter 'LICENSE*' | Sort-Object Name)
    foreach ($embeddedLicenseFile in $embeddedLicenseFiles) {
        if ([IO.Path]::GetFileName($embeddedLicenseFile.Name) -ne $embeddedLicenseFile.Name) {
            throw "Locked package $($entry.Id) $($entry.Version) has an unsafe embedded license-file name."
        }
    }
    if ($null -ne $licenseFile -and -not ($embeddedLicenseFiles.FullName -contains $licenseFile)) {
        $embeddedLicenseFiles += Get-Item -LiteralPath $licenseFile
    }

    [pscustomobject]@{
        Id = $entry.Id; Version = $entry.Version; ContentHash = $entry.ContentHash
        LicenseType = $license.type; LicenseValue = $license.InnerText.Trim(); LicenseUrl = [string]$metadata.licenseUrl
        ProjectUrl = [string]$metadata.projectUrl; Copyright = [string]$metadata.copyright; LicenseFiles = $embeddedLicenseFiles
    }
}

$lines = [Collections.Generic.List[string]]::new()
$markdownTick = [char]96
$lines.Add('# VPSReady Third-Party Notices')
$lines.Add('')
$lines.Add('This distributed notice is generated during packaging from the exact locked runtime dependency graph of `src/VpsReady.Desktop/packages.lock.json`. Test-only dependencies are excluded. It is an inventory and notice-preservation artifact, not legal advice.')
$lines.Add('')
$lines.Add("Runtime packages recorded: $($inventory.Count).")
$lines.Add('')
$lines.Add('| Package | Version | Locked package content hash | License metadata | Package source |')
$lines.Add('| --- | --- | --- | --- | --- |')
foreach ($package in $inventory) {
    $licenseMetadata = "$($package.LicenseType): $($package.LicenseValue)"
    if (-not [string]::IsNullOrWhiteSpace($package.LicenseUrl)) { $licenseMetadata += " ([source]($($package.LicenseUrl)))" }
    $source = "https://www.nuget.org/packages/$([Uri]::EscapeDataString($package.Id))/$([Uri]::EscapeDataString($package.Version))"
    $lines.Add("| $markdownTick$($package.Id)$markdownTick | $markdownTick$($package.Version)$markdownTick | $markdownTick$($package.ContentHash)$markdownTick | $licenseMetadata | [NuGet package]($source) |")
}

$lines.Add('')
$lines.Add('## Package metadata')
foreach ($package in $inventory) {
    $lines.Add('')
    $lines.Add("### $($package.Id) $($package.Version)")
    if (-not [string]::IsNullOrWhiteSpace($package.Copyright)) { $lines.Add("- Copyright metadata: $($package.Copyright)") }
    if (-not [string]::IsNullOrWhiteSpace($package.ProjectUrl)) { $lines.Add("- Project metadata: <$($package.ProjectUrl)>") }
    $lines.Add("- License metadata: $($package.LicenseType): $($package.LicenseValue)")
}

foreach ($package in $inventory | Where-Object { $_.LicenseFiles.Count -gt 0 }) {
    foreach ($licenseFile in $package.LicenseFiles) {
        $lines.Add('')
        $lines.Add("## Package-supplied license file — $($package.Id) $($package.Version)")
        $lines.Add('')
        $lines.Add("The following content is copied verbatim from the package-supplied $($licenseFile.Name) file.")
        $lines.Add('')
        $lines.Add((Get-Content -LiteralPath $licenseFile.FullName -Raw).TrimEnd([char[]]"`r`n"))
    }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
if ([string]::IsNullOrWhiteSpace($outputDirectory)) { throw 'The notice output path must have a parent directory.' }
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
[IO.File]::WriteAllText($resolvedOutput, ($lines -join [Environment]::NewLine) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

if ($PassThru) {
    [pscustomobject]@{ RuntimePackageCount = $inventory.Count; Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedOutput).Hash.ToLowerInvariant() }
}
