# VPSReady Third-Party Notice Generation Policy

Distributed artifacts must include a generated `THIRD_PARTY_NOTICES.md` that
matches the exact runtime dependency graph locked in
`src/VpsReady.Desktop/packages.lock.json`. This source-policy document is not
itself copied into an archive: `eng/package-artifact.ps1` invokes
`eng/generate-third-party-notices.ps1` after locked restore and writes the
artifact-local notice file.

The generator:

- takes the union of non-project packages from every `net10.0` runtime target
  graph, excluding test-project locks;
- records exact package ID, resolved version, lock content hash, package
  source URL, and package-declared license/copyright/project metadata;
- copies package-supplied `LICENSE*` files verbatim, including a
  package-declared file license when present; and
- fails closed when a locked package, `.nuspec`, license declaration, or
  declared license file cannot be resolved from the restored cache.

`eng/verify-third-party-notices.sh` regenerates the inventory, compares every
locked runtime package to its notice entry, and rejects developer cache paths
and private-key material. The artifact manifest records the generated notice
filename, SHA-256, source-lock path, and runtime package count so package
inspection can verify archive alignment.

This policy records package metadata and notice handling; it is not legal
advice and does not change VPSReady's Apache-2.0 license.
