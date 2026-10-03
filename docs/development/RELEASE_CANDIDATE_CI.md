# Release Candidate CI Evidence

`/.github/workflows/release-candidate-ci.yml` runs only for a `release/*`
branch after the Principal creates that branch. It is not a release-decision
workflow and it never contacts a VPS, provider, public endpoint, or deployment
environment.

The workflow captures one immutable source SHA at its start, checks out that
exact SHA separately for build, test, and each candidate package, and names
all retained artifacts with that SHA. It retains a provenance report, safe test
reports when their artifact scan passes, six self-contained package archives
with checksum sidecars, and a collected manifest only when build, test, and
every package job succeeded.

Package manifests and the collector state `BLIND_CI_ARTIFACTS_ONLY` and
`NOT MADE` for release decision. A green CI result therefore supplies E0-E4
artifact evidence only; Gate C Principal approval and the Owner real-VPS stage
remain separate. `REAL VPS: NOT TESTED.`
