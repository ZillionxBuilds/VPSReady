# Acceptance and handoff

## Required end-state checks

- New repository name AND ID verified for every write path. Actual logged-in user is authorized; org ownership is not confused with a login.
- Existing source refs, tags and local dirty work preserved. Full-parity claims are limited to what was actually compared; unavailable legacy objects/history are named.
- Migration #1 is not confused with legacy release #1. Every active purpose has an actual mapped new Issue/PR URL or explicit unresolved status.
- One persistent Workpad per current Issue; labels match `.github/labels.yml`; actual Project/Kanban state and permissions are reported separately.
- Only genuinely active tasks recreated; no fake historical discussions/reviews, no mass reopening merged repairs, no padded numbering.
- Exactly one release-to-main proposal exists as draft/review-only when needed; no main merge or auto-merge.
- Operational links, issue routing, repository IDs and current prompts corrected; historical authorship/licensing/evidence retained with provenance. New #1 must not be used as release tracker.
- Appropriate new-repo branch/Actions/app permissions inspected; main/development approved protections restored and read back where authorized. Actual required check names/sources are verified. Unavailable capability is not reported as an empty configuration.
- Existing schedules point to the right repo/task/workspace if safely accessible; otherwise exact manual action reported. No duplicate schedule or rival orchestrator.
- Current review candidate validated, especially R19. E0/E1/E2 counts, E3/E4 skips and artifact SHA/provenance are explicit. No inherited test count turned into current PASS.
- Owner E5 is genuine or NOT_RUN. Migration completion is never main/stable readiness.

## Allowed result classification

MIGRATION_OPERATIONAL: identity, routing, active tracking and required operational setup are demonstrably coherent. Report release/code validation separately; do not imply READY_FOR_MAIN.

MIGRATION_PARTIAL: some restoration is unavailable (legacy history, Project scope, schedule access, administration or Actions). Safe completed work remains useful. Name exact missing capability, attempts, impact, responsible party and minimum action. Unavailable historical comments alone need not block safe current tracking when clearly archived as unavailable.

VALIDATION_PENDING / VALIDATION_FAILED: current candidate still lacks or fails actual required checks. A copied source SHA is not a test.

Stop when safe migration work is complete and remaining external gates are documented. Do not spin indefinitely on API 403/404/422 or create new product features to avoid stopping.

## Final Owner report template

VPSREADY MIGRATION RESULT

- Canonical repository/name/ID:
- Migration coordinator:
- Before/after main, development and release SHAs:
- Preserved refs/tags/objects; comparison completeness:
- Old metadata access result and recoverable/unavailable history:
- Old repo + issue/PR number -> new URL map:
- New review proposal (draft, not merged):
- Labels/milestones/Project/Kanban:
- Active-reference and local-remote changes:
- Branch enforcement readback:
- Actions execution/current check identities and conclusions:
- Codex/app/environment/schedule routing and dry-run outcome:
- R19 focused regression plus full E0/E1/E2:
- E3/E4 exact candidate evidence and skips:
- Artifact/checksum/source provenance:
- Settings/code/docs PRs still awaiting review:
- Remaining Owner-only actions:
- Migration state:
- Release validation state:
- REAL VPS: NOT TESTED, unless attributable current Owner evidence says otherwise.

The coordinator remains the live record. This packet and identity map retain durable migration intent, not a second automation controller.
