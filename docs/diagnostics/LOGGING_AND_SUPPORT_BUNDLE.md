# VPSReady Logging, Operation Journal and Support Bundle Contract

Status: **Binding for v0.1 release readiness**

Blind development makes diagnostics a first-class product feature. The autonomous team cannot inspect the Owner's VPS, so a failure must be explainable from safe local evidence without requesting credentials or direct server access.

## 1. Three diagnostic surfaces

### A. Activity view

Human-readable, real-time UI entries for the current session:

- action name;
- started/running/succeeded/warning/failed/cancelled/recovery-required state;
- concise user-safe message;
- duration;
- operation ID;
- next safe action.

The Activity view is not raw terminal output and must remain understandable to a non-Linux expert.

### B. Structured operation journal

Machine-readable JSON Lines records persisted locally for diagnosis. It is the authoritative chronology of what VPSReady attempted and observed.

### C. Exported support material

Two explicit user actions:

1. **Copy Safe Issue Report** — Markdown designed for a public GitHub issue, aggressively anonymized.
2. **Export Sanitized Support Bundle** — ZIP retained locally and shared only after the Owner reviews its contents.

VPSReady must never auto-upload logs, create telemetry, send crash reports or contact a hosted backend.

## 2. Storage boundary

Use a platform path service; never write logs beside the portable executable by default.

Expected policy:

- Windows: per-user Local Application Data under `VPSReady`.
- macOS: per-user Application Support under `VPSReady`.
- Linux: `$XDG_STATE_HOME/vpsready` when defined, otherwise `~/.local/state/vpsready`.

Suggested layout:

```text
<VPSReadyState>/
  logs/
    app-YYYYMMDD.jsonl
  runs/
    <run-id>/
      manifest.json
      events.jsonl
      summary.md
  exports/
```

Trust-store/config files are separate from operation logs. Passwords and private keys never enter this directory.

Default retention target:

- remove non-exported run data older than 14 days;
- enforce approximately 50 MiB total automatic diagnostic storage;
- retain the newest 20 runs when applying size cleanup;
- never delete a user-exported bundle automatically;
- provide **Open Log Folder** and **Clear Diagnostics** actions.

Exact cleanup constants may be adjusted by Principal inside scope, but limits and tests are required.

## 3. Correlation model

Generate opaque identifiers:

- `session_id` — application connection/session lifecycle;
- `run_id` — one user-invoked workflow/test sequence;
- `operation_id` — one action such as Test Connection or Enable Firewall;
- `step_id` — validate/preflight/plan/apply/verify/recovery sub-step;
- `event_id` — stable event type from a documented catalog;
- `command_id` — stable remote command definition, never an ad-hoc UI string;
- `error_code` — stable user/support taxonomy.

The UI displays the operation ID on failure so the Owner can find the same record in a bundle and issue report.

## 4. Structured event schema

Each persisted event contains applicable fields:

```json
{
  "schema_version": 1,
  "timestamp_utc": "RFC3339 UTC",
  "level": "Information|Warning|Error",
  "event_id": "ssh.connection.failed",
  "category": "Connection",
  "app_version": "0.1.0",
  "build_sha": "git SHA",
  "local_os": "macOS",
  "local_arch": "arm64",
  "session_id": "opaque id",
  "run_id": "opaque id",
  "operation_id": "opaque id",
  "step_id": "verify",
  "phase": "validate|preflight|plan|apply|verify|recovery",
  "action": "TestConnection",
  "status": "started|succeeded|failed|cancelled|recovery_required",
  "duration_ms": 1234,
  "server_ref": "non-reversible local pseudonym",
  "command_id": "ubuntu.ufw.status",
  "exit_code": 1,
  "output_policy": "metadata|sanitized_truncated|none",
  "error_code": "SSH_AUTHENTICATION_FAILED",
  "verification": "not_run|passed|failed",
  "recovery": "not_required|attempted|passed|failed",
  "message": "sanitized summary"
}
```

Omit fields that do not apply. Do not serialize secrets as null placeholders or hidden objects; keep them outside diagnostic models entirely.

## 5. Remote-command evidence

For each remote command execution, record:

- stable `command_id`;
- sanitized non-secret argument summary;
- phase and privilege mode;
- start/end/duration;
- exit code or transport failure taxonomy;
- stdout/stderr byte counts;
- sanitized and bounded output only when the command's capture policy permits it;
- timeout/cancellation/disconnect signal;
- verification command/result when applicable.

Do not log a credential-bearing raw command. Passwords must never be supplied through command-line arguments, shell interpolation, environment logging or `sudo -S` transcripts.

Default output limit: at most 64 KiB per stdout/stderr stream after redaction, with an explicit truncation marker and original captured length metadata. Highly sensitive commands use metadata-only or no-output policy.

## 6. Redaction and privacy pipeline

One central redaction service must run before data reaches Activity UI, file sink, support bundle, issue report, exception dialog, screenshot text or test artifact.

Required protections:

1. Typed classification for credential, password, passphrase, private key, token, host identifier, username/path and command output.
2. Dynamic exact-value registration for session secrets so accidental copies are erased.
3. Structural redaction by field/key name.
4. Pattern detection for private-key blocks, authorization headers, token-like values and common credential formats.
5. Pseudonymization of host/IP/username in the public-safe report.
6. Public keys represented by algorithm/fingerprint only unless the user explicitly views the public key in its dedicated feature.
7. Exception sanitization before rendering or persistence.
8. Fail-closed behavior: if safe redaction cannot be guaranteed, omit the payload and log `PAYLOAD_OMITTED_BY_REDACTION_POLICY`.

No v0.1 UI offers an “export unredacted” option.

Tests must feed secrets through message templates, exception text, multiline stdout/stderr, nested objects, file paths and cancellation/failure paths and assert that none appear in memory snapshots exposed to UI, persisted files or exported bundles.

## 7. Error taxonomy

At minimum use stable codes for:

- input validation;
- DNS/network unreachable;
- connection refused;
- timeout;
- cancellation;
- unknown host key;
- changed host key;
- authentication failure;
- privilege/sudo unavailable;
- unsupported OS/command unavailable;
- remote command failure;
- parse/partial-result failure;
- verification failure;
- rollback/recovery failure;
- local file/permission/collision failure;
- package-manager lock;
- reconnect timeout;
- unexpected internal failure.

User messages explain impact and next action. Diagnostic details retain safe type/stack/command IDs without leaking raw credentials.

## 8. Crash and startup failure

Register supported Avalonia/.NET unhandled-exception hooks. On an unexpected failure:

- attempt to flush a minimal sanitized crash event;
- show a friendly dialog with error/operation ID and log location;
- offer Copy Safe Issue Report and Export Sanitized Support Bundle when possible;
- never silently exit when a user-safe explanation can be shown;
- never auto-send the crash.

If normal logging initialization fails, use a minimal local fallback that cannot include connection credentials.

## 9. Support bundle contents

Suggested filename:

`vpsready-support-<UTC>-<short-run-id>.zip`

Required contents:

- `manifest.json` — schema, app version, build SHA, artifact RID, export time, included files and SHA-256 checksums;
- `issue-report.md` — public-safe summary;
- `run-summary.md` — stages/results/operation IDs;
- `events.jsonl` — redacted structured events for selected run(s);
- `environment.json` — local OS/arch/runtime and safe remote capability/version summary;
- `known-limitations.md` — release/evidence boundary when relevant.

Explicit exclusions:

- passwords/passphrases/tokens;
- private keys or key file contents;
- full public keys;
- local SSH config contents;
- known-host/trust-store contents;
- `authorized_keys` contents;
- raw server IP/hostname/username by default;
- unrelated user files;
- unredacted command output;
- provider account details.

Before export, show the destination and a concise inclusion/privacy notice. The bundle remains local until the user chooses to share it.

## 10. Safe GitHub issue report

The copied Markdown should include:

- VPSReady version/build SHA and release branch;
- local OS/architecture;
- test stage and action;
- operation ID/error code;
- expected and observed safe summary;
- retry/recovery outcome;
- evidence class `E5 Owner real VPS` when produced during Owner testing;
- sanitized server capability summary;
- bundle filename/checksum if the Owner chooses to share it.

It must warn that the repository is public and ask the Owner to review attachments. Raw bundles are not automatically attached.

## 11. Definition of Done for diagnostics

Before Principal creates `release/*`:

- all remote feature paths emit correlated start/result/verification events;
- error codes and event/command IDs are documented and stable enough for support;
- redaction adversarial tests pass;
- retention, cleanup and atomic journal writes are tested;
- Activity UI remains bounded/responsive;
- support bundle and safe issue report export work on supported local platforms where testable;
- exported fixtures contain no seeded secret values;
- a crash/startup failure produces a useful safe path;
- Owner test instructions explain how to collect and report diagnostics.
