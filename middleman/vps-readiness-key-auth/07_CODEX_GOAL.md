/goal VPSReady — Implement initial SSH key login and evidence-based VPS Ready checklist

MISSION
Deliver the Owner-requested KA/RC/UX journeys defined in the pinned VP-123 packet. Work SOLO through implementation, real regressions, contained protocol validation and one reviewable PR. This is new scope, not historical audit/migration replay.

REPOSITORY
ZillionxBuilds/VPSReady; required repository ID 1361332816.
Coordinator #123; release tracker #2; validation #3; Owner E5 #5; main proposal #6.
Packet: middleman/vps-readiness-key-auth/ on development.

FIRST
Verify repository identity/current refs/worktrees and preserve existing work. Read AGENTS.md and ALL nine Markdown documents plus core-basic-v1.json from the SAME packet commit supplied by the Owner. Record that SHA in #123. Reuse the current Workpad. Implement from current release/0.1.0 on one feature/123-key-auth-readiness branch; do not merge development merely to obtain the packet.

OUTCOME A — CODE READY FOR EXTERNAL REVIEW
1. Connection offers Password or SSH private key. Key-only Ubuntu login works from a disconnected app with password authentication disabled; no .pub file or password connection required.
2. Support the mandatory Ed25519/RSA plain/encrypted format matrix, validated key identity, known-host trust, correct secret lifetime, auth-mode-aware fresh login and reboot reconnection. No password/agent fallback or server security downgrade.
3. Add the real VPS Ready page with explicit read-only Check/Cancel, nine required + six advisory results, manual exclusions, versioned Core Basic profile, honest verdicts/timestamps and safe current-session freshness.
4. Failed/unknown rows navigate to the correct existing tab/section with guidance. Navigation never applies configuration, changes endpoint/key/rule data or grants confirmation. Only a fresh recheck changes readiness after configuration.
5. Preserve all previous runtime/diagnostic/access-safety fixes. No raw-output persistence, arbitrary shell, remote installer or new daemon. Docker/Coolify/Fail2ban are not default readiness gates.

ACCEPTANCE / DOD
Document 05 is binding: KA-T01–12, RC-T01–17, UX-T01–06; E0/full E1/full E2 pass, contained production E3 with password+keyboard-interactive disabled passes, and exact-SHA self-contained Mac review package/offline UI pass. All nine required checks implemented; required unknown/stale/error is never Ready. Test read-only command dispatch and unchanged configuration fixtures; test real production output contracts, not permissive fakes. Update spec/guide/manual addendum and Workpad. Existing E5 remains attributable only to its own tested binary.

EXECUTION
Follow milestones in 06. Make safest evidence-backed implementation choices within scope without routine Owner questions. No subagents, schedules, new audit loops, one PR per row, manual VPS access or requested Owner credentials. Keep existing GitHub Workpad/Kanban updated after meaningful transitions; do not create a duplicate board. Do not stop after only a helper/placeholder screen is written.

AUTHORITY / STOP
This goal authorizes feature branch code, tests, docs, pushes and ONE PR to release, not new runtime self-merge, main promotion or stable publication. DoD A ends at KEY_AUTH_READINESS_READY_FOR_EXTERNAL_REVIEW. External review and later normal authorized release integration/dev sync/final Owner packaging are Phase B, not an internal wait loop. Do not invent independent QA/approvals or bypass GitHub policy.

If a genuine technical blocker remains, finish other safe work and report FEATURE_BLOCKED with exact attempt/error, affected acceptance ID and minimal external action. Hosted Actions, signing, Kanban, other unavailable native RIDs and absent real VPS alone are not reasons to abandon the applicable local work.

FINAL
KEY_AUTH_READINESS_READY_FOR_EXTERNAL_REVIEW
or FEATURE_BLOCKED — <specific unmet requirement>
Include exact branch/head/base/PR; KA/RC/UX evidence matrix; formats/profile/checks; tests/skips; review artifact SHA/checksum; self-review limits; remaining review/integration steps. No READY_FOR_MAIN, no false Owner package readiness, no old evidence relabeling.
REAL VPS: NOT TESTED.
