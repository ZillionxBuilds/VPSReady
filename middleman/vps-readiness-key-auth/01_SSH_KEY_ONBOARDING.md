# Initial SSH private-key authentication

Requirement family **KA**. Authentication is a first-class session property, not an optional password textbox.

## KA-01: Connection form

Always show Host/IP, SSH port (default 22), username and authentication choice **Password / SSH private key**. Password mode preserves existing behavior. Key mode shows a Browse private key control, local-only selected filename, algorithm/fingerprint metadata after validation, and a distinct masked **Key passphrase (only when encrypted)** field. A key passphrase is neither the remote login password nor a sudo password.

Key mode must allow Test Connection with an empty server-password field. It must offer only the explicitly selected key authentication method, never automatic password/keyboard-interactive fallback, agent forwarding, key-directory enumeration or multiple guessed identities. Auth failure must not suggest enabling server password/root login as the remedy.

Support typing via framework text-input semantics, normal navigation and secret clearing. Reuse the existing protected input policy; do not log clipboard contents or invent a new plaintext binding for passphrases.

## KA-02: Explicit first delivery format matrix

Required interoperable login fixtures:
- Ed25519 OpenSSH private key, unencrypted.
- Ed25519 OpenSSH private key, passphrase-protected using normal OpenSSH AES-256-CTR/bcrypt output.
- RSA 2048/3072/4096 OpenSSH private key, unencrypted and the same encrypted OpenSSH format.
- RSA traditional PKCS#1 PEM private key, unencrypted (common `.pem` login case).

Use the pinned maintained SSH.NET/BouncyCastle implementation. Verify parser/API behavior against the pinned versions before implementation; no homegrown decryptor or signature code. RSA must negotiate rsa-sha2 signatures; never enable legacy SHA-1 signatures to pass a fixture. Ed25519 remains the app's generated-key format. Supporting RSA for existing login does not require rewriting the existing Ed25519 deployment UI.

Out of this delivery: PuTTY `.ppk`, certificates, FIDO/security keys, SSH agent/OS keychain integration, MFA sequences, ProxyJump/ProxyCommand, automatic OpenSSH config import, and every possible PEM cipher. Reject unsupported formats with fixed safe guidance; never convert or overwrite the user's key. File extensions alone do not determine key type. Capability beyond the required matrix is not claimed until tested.

## KA-03: Standalone private keys and identity

Initial login must work with the private file alone. Do not require `<private>.pub`, generate a companion on disk, deploy a key first, or establish a password session first. Derive the canonical OpenSSH public-key fingerprint from the validated private key in memory.

Do not weaken existing deployment pair validation: deployment still must prove its selected public/private identity. The new initial-login selector is a separate purpose and must not accept a `.pub` file as a private key. It does not silently trust an adjacent unrelated `.pub`.

Bound reads to 256 KiB and reject non-regular, linked/reparse, unreadable, malformed or oversized files through the existing safe-file boundaries. Check restrictive permissions where supported; report a local permission problem and let the user fix it intentionally. Do not chmod arbitrary selected files automatically. Validate the bytes actually used for authentication, not a path that is reopened unchecked later.

## KA-04: Trust and lifecycle

Flow: validate inputs/key -> create candidate auth transport -> perform normal host-key assessment -> explicit unknown/changed-key review -> authenticate only to the trusted endpoint -> minimum remote command -> promote that transport as the main ApplicationSession.

An unknown/changed key never becomes trusted because the client private key is valid. Accepting trust does not itself mean Connected. Preserve the explicit retry flow; clear failed-attempt secrets and request the passphrase again when required. Use the existing trust identity rules for host+port; do not rewrite trust records or collapse different endpoints.

Changing host/port/user/auth mode/key selection while idle or an attempt is pending invalidates the old candidate authority, trust review and dependent confirmations. Old async completions cannot reinstate a prior target. Auth rejection, local key unlock/parse failure, permission failure, network/refusal, trust, timeout and cancel remain distinct where evidence supports distinction. Do not diagnose server-wide password policy from a generic authentication denial. MFA/additional-auth-required may be surfaced specifically only when the library provides that evidence; otherwise report unsupported/authentication failure without guessing.

No-sudo accounts can still connect and inspect permitted facts. Restricted management privilege is not a key-authentication failure.

## KA-05: Session credential ownership and fresh connections

Introduce an auth-mode-aware, disposable session credential lease. Password and private-key leases must support creating a FRESH SSH.NET authentication graph for an authorized reconnect/probe. Reusing an old disposed graph or a cleared password buffer is forbidden.

For key mode, retain only the minimum validated in-memory key material/handle and, only when necessary, the clearable unlock secret for the active session. Explain in UI that session-only memory enables reconnect. No persistent secret storage, copied key files, plaintext passphrase preferences, command arguments/environment variables, telemetry, or logs. Clear UI buffers after submission; dispose/clear owned buffers on failure, disconnect, key/mode/target replacement and app shutdown. Unavoidable short-lived managed strings in third-party APIs must be documented; do not promise that every managed allocation can be forcibly erased.

Choose one tested ownership design: a session-owned validated key handle with safe attempt lifetimes, or a bounded validated bytes/unlock lease that creates a new disposable key object per attempt. Do not mix lifetime owners. Serialize credential use through the session. A readiness probe must use the CURRENT SESSION credential, not a different key selected later on the SSH-management tab.

The session snapshot represents the key selected at connection time. Reconnect may use that same validated in-memory identity even if the disk path later changes; it must never silently load key B from the path. A new user-requested key selection/reconnection validates the new file and requires fresh trust/session review as applicable. Document snapshot lifetime; test file A->B replacement.

## KA-06: Reboot/reconnect and ancillary workflows

Update ReconnectAsync and its capability/preflight so a key-authenticated main session can reconnect after an authorized reboot without a server password. Trust must be rechecked on every new connection, along with boot-identity/minimum-verification requirements already present. Before reboot, if the credential lease cannot support the planned reconnect, request re-unlock/reselection or refuse BEFORE dispatch; never discover an intentionally missing auth capability only after reboot.

Run Check's fresh-connection probe creates and disposes a separate connection; it never swaps/disconnects the main session. One failed credential attempt per user-requested check, no guessing/brute-force retries. Reboot retries follow the existing finite recovery budget and stop on definitive auth/trust errors.

Firewall/system/readiness actions on a key session must work without accessing a password-only interface. Keep existing public-key deployment and separate-key-login verification usable; private-key authentication is not proof that a newly selected deployment key works. Do not edit sshd_config, authorized_keys, sudoers or server password policy during initial connection or readiness checks.
