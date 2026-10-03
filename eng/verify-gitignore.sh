#!/usr/bin/env bash
set -euo pipefail
for path in src/Example/bin/generated.dll tests/Example/obj/project.assets.json artifacts/packages/candidate.zip TestResults/unit.trx .DS_Store eng/__pycache__/probe.pyc tests/example/fixtures/credentials.json tests/example/golden/id_ed25519 tests/example/scenarios/local.pem .env.local; do
  git check-ignore --no-index -q "$path" || { printf 'Expected ignored path: %s\n' "$path" >&2; exit 1; }
done
for path in src/Example/Reviewed.cs tests/example/fixtures/reviewed.json tests/example/golden/ubuntu.txt tests/example/scenarios/reviewed.cs src/Example/packages.lock.json .env.example; do
  if git check-ignore --no-index -q "$path"; then printf 'Reviewed input is unexpectedly ignored: %s\n' "$path" >&2; exit 1; fi
done
printf '%s\n' 'PASS: generated files and secrets ignored; reviewed source/fixtures remain trackable.'
