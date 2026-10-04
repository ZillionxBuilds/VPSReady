#!/usr/bin/env bash
# Only run inside a disposable Ubuntu container/CI sandbox. No published ports or real VPS.
set -euo pipefail
if [[ "$(uname -s)" != Linux || "$(id -u)" != 0 || ! -f /.dockerenv ]]; then
  printf 'Key-only E3 requires a disposable root-run Ubuntu container. Host sshd is never changed.\n' >&2
  exit 2
fi
fixture_root="$(mktemp -d /run/vpsready-key-only.XXXXXX)"
fixture_user="vpsreadykey$RANDOM$RANDOM"
daemon_pid=''
cleanup() {
  local result=$?
  if [[ -n "$daemon_pid" ]]; then kill "$daemon_pid" 2>/dev/null || true; fi
  userdel --remove "$fixture_user" 2>/dev/null || true
  # Both targets were created uniquely by this disposable fixture, never user paths.
  rm -rf -- "$fixture_root"
  exit "$result"
}
trap cleanup EXIT
apt-get update -qq
apt-get install -y --no-install-recommends openssh-client openssh-server >/dev/null
useradd --create-home --shell /bin/sh "$fixture_user"
# Unlock only the disposable account; sshd still refuses password and interactive auth.
fixture_unlock="$(od -An -N18 -tx1 /dev/urandom | tr -d ' \n')"
printf '%s:%s\n' "$fixture_user" "$fixture_unlock" | chpasswd
unset fixture_unlock
chmod 711 "$fixture_root"
mkdir -m 755 "$fixture_root/authorized"
authorized="$fixture_root/authorized/key-only-approved.pub"
mkdir -p /run/sshd
ssh-keygen -q -t ed25519 -N '' -f "$fixture_root/host" >/dev/null
port="$(shuf -i 42000-52000 -n 1)"
cat > "$fixture_root/sshd_config" <<CONFIG
Port $port
ListenAddress 127.0.0.1
HostKey $fixture_root/host
PidFile $fixture_root/sshd.pid
AuthorizedKeysFile none
AuthorizedKeysCommand /usr/bin/cat $authorized
AuthorizedKeysCommandUser nobody
PubkeyAuthentication yes
PubkeyAcceptedAlgorithms ssh-ed25519,rsa-sha2-512,rsa-sha2-256
PasswordAuthentication no
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
AuthenticationMethods publickey
UsePAM no
PermitRootLogin no
AllowUsers $fixture_user
PrintMotd no
LogLevel ERROR
CONFIG
/usr/sbin/sshd -t -f "$fixture_root/sshd_config"
effective="$(/usr/sbin/sshd -T -f "$fixture_root/sshd_config")"
for setting in 'passwordauthentication no' 'kbdinteractiveauthentication no' 'pubkeyauthentication yes' 'authenticationmethods publickey'; do
  if ! printf '%s\n' "$effective" | grep -Fx "$setting" >/dev/null; then
    printf 'Key-only sshd fixture failed its authentication policy guard.\n' >&2
    exit 1
  fi
done
config_digest="$(sha256sum "$fixture_root/sshd_config" | cut -d ' ' -f1)"
unset effective
/usr/sbin/sshd -f "$fixture_root/sshd_config" -E "$fixture_root/daemon.log"
daemon_pid="$(cat "$fixture_root/sshd.pid")"
for _ in $(seq 1 40); do
  ssh-keyscan -T 1 -p "$port" 127.0.0.1 >/dev/null 2>&1 && break
  sleep 0.1
done
ssh-keyscan -T 1 -p "$port" 127.0.0.1 >/dev/null 2>&1
# Only disposable fixture configuration/state. Aggregate digests are retained;
# file contents, private material and remote identity are never exported.
configuration_digest() {
  local roots=()
  for path in /etc/ssh /etc/apt /var/lib/apt /var/cache/apt /var/lib/dpkg /etc/ufw /etc/default/ufw; do
    if [[ -e "$path" ]]; then roots+=("$path"); fi
  done
  { find "${roots[@]}" -type f -print0 | sort -z | xargs -0 sha256sum; sha256sum "$fixture_root/sshd_config"; } | sha256sum | cut -d ' ' -f1
}
before_readiness_digest="$(configuration_digest)"
result_file="key-only-production-$(date -u +%Y%m%dT%H%M%SZ).trx"
VPSREADY_E3_KEY_ONLY=1 VPSREADY_E3_KEY_ONLY_USER="$fixture_user" \
  VPSREADY_E3_KEY_ONLY_PORT="$port" VPSREADY_E3_KEY_ONLY_AUTHORIZED="$authorized" \
  dotnet test tests/VpsReady.UnitTests --configuration Release --filter 'FullyQualifiedName~InitialKeyOnlyE3Tests' \
    --logger "trx;LogFileName=$result_file" --results-directory artifacts/verification/123/e3
after_readiness_digest="$(configuration_digest)"
if [[ "$before_readiness_digest" != "$after_readiness_digest" ]]; then
  printf 'Read-only readiness changed disposable fixture configuration/package state.\n' >&2
  exit 1
fi
mkdir -p artifacts/verification/123/e3
cat > artifacts/verification/123/e3/key-only-policy.txt <<RESULT
evidence=E3 Local-contained Protocol
result_file=$result_file
PasswordAuthentication=no
KbdInteractiveAuthentication=no
AuthenticationMethods=publickey
PubkeyAcceptedAlgorithms=ssh-ed25519,rsa-sha2-512,rsa-sha2-256
fixture_configuration_sha256=$config_digest
initial_lifecycle_promoted_session=PASS
plain_encrypted_ed25519_rsa_and_rsa_pem=PASS
no_pub_companion=PASS
explicit_unknown_host_denial_and_retry=PASS
fresh_login_and_reconnect_from_validated_snapshot=PASS
production_readiness_required_access_and_no_sudo_unknown=PASS
rejected_new_key_auth_preserves_main_session_no_fallback=PASS
readonly_configuration_package_digest_unchanged=PASS
readonly_configuration_package_sha256=$after_readiness_digest
REAL VPS: NOT TESTED
RESULT
printf 'Contained production key-only E3 passed. REAL VPS: NOT TESTED.\n'
