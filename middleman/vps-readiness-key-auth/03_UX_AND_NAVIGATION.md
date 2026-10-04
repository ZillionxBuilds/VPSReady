# Desktop UX and guided remediation

Use existing C#/Avalonia desktop design, keyboard navigation and accessibility. This is an in-app page, not the offline Owner E5 HTML recorder. No browser runtime dependency.

## Connection screen

```text
Connection
Host / IP      [_________________]   Port [22]
Username       [_________________]
Authentication ( ) Password   (o) SSH private key

Private key    [Selected local filename] [Browse...]
Key passphrase [*****************]  (only when encrypted)
Key identity   Ed25519 / SHA256:...  (local display; not raw key)

Private key remains local. Unlock material is held only for this session.
[Test Connection] [Cancel]
```

The private key is not uploaded. The normal SSH authentication signature exchange is distinct from transferring the private key file. Keep host trust review visible; do not silently accept an unknown/changed host. Preserve no-password-persistence and input clearing.

Once connected, show the authenticated method and selected local key alias/algorithm without revealing secret paths in diagnostics. A disconnected user can still choose/generate/select keys using existing appropriate controls; no circular requirement to log in with a password before choosing a key.

## New navigation item

Add `ShellPage.Readiness` with label **VPS Ready** after Overview. Keep all six existing pages. Default app launch remains Connection; successful connection offers a visible `Check VPS readiness` action rather than silently running checks/secondary logins. Readiness page remains navigable while disconnected but its Check is disabled and a Connect button routes to Connection.

```text
VPS Ready                       [Core Basic · Ubuntu / UFW v1]
Current session: VPS-A · SSH key · last check 13:45
[Check VPS readiness] [Cancel] [View safe report]
Read-only checks. Includes one new SSH login using this session.

NEEDS ATTENTION
Required: 7/9 passed    1 action needed    1 not verified

[All] [Needs attention] [Unknown] [Passed] [Optional / manual]

Access
  PASS    Trusted current session
  PASS    New SSH login succeeded
Security baseline
  FAIL    UFW is inactive                  [Open Firewall]
  UNKNOWN SSH protection not evaluated    [Review SSH access]
System baseline
  PASS    Package audit
  PASS    Root available space
  FAIL    Reboot pending                   [Open System]

Recommendations / outside this profile
  WARN    Updates from existing cache     [Review packages]
  MANUAL  Provider firewall / backup      [View guidance]
```

The sketch is illustrative; counts in implementation must come from actual result rows, never hardcoded sample data. UI labels can follow the existing English app; documentation includes Thai explanations. Use icons and text, not color alone. Each row has check name, required/advisory/manual tag, state, safe reason, evidence source category/time, destination button and expandable explanation.

## Route contract

Typed `ReadinessActionTarget(Page, Section, CheckId, SessionId, Generation)` only. Map exactly:
- Connection/authentication -> `ShellPage.Connection`, credential method/input section.
- Overview/platform or storage -> `ShellPage.Overview`, corresponding facts and help.
- Firewall/status or ssh-access -> `ShellPage.Firewall`, fresh state/rule review section.
- SSH keys/key-access -> `ShellPage.SshKeysAndConfig`, select/deploy/separate-auth section; generation is optional.
- System/privilege -> `ShellPage.System`, privilege explanation/status panel.
- System/packages -> System package plan/update controls.
- System/reboot -> System reboot-required status/confirmation controls.
- System/identity-time -> System hostname/timezone section.
- System/time-guidance -> System read-only synchronization guidance; do not invent an enable-NTP action.
- Activity/diagnostics -> `ShellPage.ActivityAndDiagnostics`, correlated operation if still available.

Implement small read-only guidance panels where an existing page lacks the named explanation (privilege/storage/time). Do not imply the app can resize a disk, grant sudo, repair arbitrary dpkg state or manage provider firewalls. Label those actions `Review guidance`, not `Fix automatically`.

## Button behavior

A click navigates, scrolls/focuses the relevant section and displays the check's reason. It NEVER invokes apply/reboot/enable/deploy, prechecks a confirmation, changes a port/source, saves a configuration, or fetches external content automatically. Existing ViewModel workflows remain responsible for validation and explicit consent.

Before navigation, validate that the action still belongs to the current session/generation. If stale, permit plain navigation but discard old target payload/selection and display `Reconnect or recheck before configuring`. Never apply an old row's endpoint/key/rule to a new session.

Provide `Back to VPS Ready` on guided pages. After any possible configuration mutation, show `Result stale — run check again`. Do not automatically rerun a heavyweight check on every keystroke or tab switch. A failed required check must not lock the user out of its configuration tab; readiness is advisory workflow guidance, not a circular global permission gate.

## Reporting

Reuse Activity/Safe Issue Report/support bundle. Distinguish `check completed; baseline not ready` from `check failed`. Store stable profile/check IDs, state/reason codes, timestamp, duration and opaque correlation. Raw endpoints, usernames, private/public key material, passphrases, full config, raw command output and chosen local paths must not enter exported data. Existing deliberate local display of host/fingerprint remains subject to existing app policy. No new report uploader or auto-GitHub issue creation from VPS data.
