@{
    SchemaVersion = 1
    ProductName = 'VPSReady'
    Profiles = @(
        @{ Rid = 'win-x64'; ExpectedRunnerOs = 'Windows'; ArchiveExtension = 'zip'; BundleKind = 'archive'; SelfContained = $true },
        @{ Rid = 'win-arm64'; ExpectedRunnerOs = 'Windows'; ArchiveExtension = 'zip'; BundleKind = 'archive'; SelfContained = $true },
        @{ Rid = 'linux-x64'; ExpectedRunnerOs = 'Linux'; ArchiveExtension = 'zip'; BundleKind = 'archive'; SelfContained = $true },
        @{ Rid = 'linux-arm64'; ExpectedRunnerOs = 'Linux'; ArchiveExtension = 'zip'; BundleKind = 'archive'; SelfContained = $true },
        @{ Rid = 'osx-x64'; ExpectedRunnerOs = 'macOS'; ArchiveExtension = 'zip'; BundleKind = 'archive'; SelfContained = $true },
        @{ Rid = 'osx-arm64'; ExpectedRunnerOs = 'macOS'; ArchiveExtension = 'zip'; BundleKind = 'archive'; SelfContained = $true }
    )
    Signing = @{
        Status = 'UNSIGNED'
        Warning = 'UNSIGNED CANDIDATE: code signing and macOS notarization were not performed; verify the source SHA-256 checksum before use.'
    }
    StartupEvidence = 'NOT RUN (C606 startup-smoke suite is not implemented)'
}
