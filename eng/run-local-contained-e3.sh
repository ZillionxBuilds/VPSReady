#!/usr/bin/env bash
# E3 only: creates a disposable OpenSSH daemon bound to loopback in an
# Ubuntu-family test environment. It never contacts a VPS or public SSH host.
set -euo pipefail

if [[ "$(uname -s)" != 'Linux' ]] || ! command -v apt-get >/dev/null; then
  printf 'E3 local-contained protocol requires an Ubuntu-family CI runner.\n' >&2
  exit 2
fi

runtime_root="$(mktemp -d)"
# The disposable sshd reads only this test-owned public-key file through an
# unprivileged command. Other fixture material retains its restrictive mode.
chmod 711 "$runtime_root"
sleep_marker="$runtime_root/e3-command-sleep"
authorized_dir="$runtime_root/authorized"
mkdir -m 755 "$authorized_dir"
authorized_file="$authorized_dir/vpsready-e3-current.pub"
service_user="vpsreadye3$RANDOM$RANDOM"
service_group="$service_user"
daemon_pid=''
fixture_bin=''

as_root() {
  if [[ "$(id -u)" == '0' ]]; then
    "$@"
  else
    sudo "$@"
  fi
}

cleanup() {
  local status=$?
  if [[ -n "$daemon_pid" ]] && as_root kill -0 "$daemon_pid" 2>/dev/null; then
    as_root kill "$daemon_pid" 2>/dev/null || true
    for _ in $(seq 1 40); do
      as_root kill -0 "$daemon_pid" 2>/dev/null || break
      sleep 0.1
    done
  fi
  as_root userdel --remove "$service_user" 2>/dev/null || true
  as_root groupdel "$service_group" 2>/dev/null || true
  if [[ -n "$fixture_bin" ]]; then
    as_root rm -rf -- "$fixture_bin"
  fi
  rm -rf -- "$runtime_root"
  exit "$status"
}
trap cleanup EXIT

as_root apt-get update -qq
as_root apt-get install -y --no-install-recommends openssh-client openssh-server >/dev/null

login_value="$(od -An -N18 -tx1 /dev/urandom | tr -d ' \n')"
wrong_value="$(od -An -N18 -tx1 /dev/urandom | tr -d ' \n')"
port="$(shuf -i 42000-52000 -n 1)"

as_root groupadd "$service_group"
as_root useradd --create-home --gid "$service_group" --shell /bin/sh "$service_user"
printf '%s:%s\n' "$service_user" "$login_value" | as_root chpasswd
as_root mkdir -p /run/sshd
fixture_bin="$(as_root mktemp -d /run/vpsready-e3-bin.XXXXXX)"
as_root chmod 755 "$fixture_bin"
as_root tee "$fixture_bin/timedatectl" >/dev/null <<'TIMEZONE'
#!/bin/sh
if [ "$1" = 'show' ] && [ "$2" = '--property=Timezone' ] && [ "$3" = '--value' ]; then
  if [ -f "$VPSREADY_E3_STUB_SLEEP_MARKER" ]; then
    sleep 30
  fi
  printf 'E3-STANDARD-OUTPUT\n'
  printf 'E3-STANDARD-ERROR\n' >&2
  exit 23
fi
exec /usr/bin/timedatectl "$@"
TIMEZONE
as_root chmod 755 "$fixture_bin/timedatectl"

ssh-keygen -q -t ed25519 -N '' -f "$runtime_root/host_ed25519" >/dev/null
cat > "$runtime_root/sshd_config" <<CONFIG
Port $port
ListenAddress 127.0.0.1
HostKey $runtime_root/host_ed25519
PidFile $runtime_root/sshd.pid
AuthorizedKeysFile none
AuthorizedKeysCommand /usr/bin/cat $authorized_file
AuthorizedKeysCommandUser nobody
PubkeyAuthentication yes
PasswordAuthentication yes
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
UsePAM no
PermitRootLogin no
AllowUsers $service_user
PrintMotd no
LogLevel ERROR
SetEnv PATH=$fixture_bin:/usr/bin:/bin VPSREADY_E3_STUB_SLEEP_MARKER=$sleep_marker
CONFIG

as_root /usr/sbin/sshd -f "$runtime_root/sshd_config" -E "$runtime_root/sshd.log"
daemon_pid="$(as_root cat "$runtime_root/sshd.pid")"
for _ in $(seq 1 40); do
  if ssh-keyscan -T 1 -p "$port" 127.0.0.1 >/dev/null 2>&1; then
    break
  fi
  sleep 0.1
done
ssh-keyscan -T 1 -p "$port" 127.0.0.1 >/dev/null 2>&1 || {
  printf 'Contained OpenSSH daemon did not become ready.\n' >&2
  exit 1
}

askpass="$runtime_root/askpass"
cat > "$askpass" <<'ASKPASS'
#!/usr/bin/env bash
printf '%s\n' "$VPSREADY_E3_LOGIN_VALUE"
ASKPASS
chmod 700 "$askpass"

ssh_runner="$runtime_root/ssh-run"
cat > "$ssh_runner" <<'RUNNER'
#!/usr/bin/env bash
set -euo pipefail
exec ssh -F /dev/null -o BatchMode=no -o NumberOfPasswordPrompts=1 -o ConnectTimeout=3 \
  -o UserKnownHostsFile="$VPSREADY_E3_KNOWN_HOSTS" -o StrictHostKeyChecking=yes \
  -p "$VPSREADY_E3_PORT" "$VPSREADY_E3_USER@127.0.0.1" "$@"
RUNNER
chmod 700 "$ssh_runner"

run_ssh() {
  DISPLAY=':0' SSH_ASKPASS_REQUIRE=force SSH_ASKPASS="$askpass" VPSREADY_E3_LOGIN_VALUE="$login_value" \
    VPSREADY_E3_KNOWN_HOSTS="$runtime_root/known_hosts" VPSREADY_E3_PORT="$port" VPSREADY_E3_USER="$service_user" \
    "$ssh_runner" "$@"
}

# Unknown host must fail before authentication; this is the contained host-key
# callback/proof path and is deliberately not auto-accepted.
if run_ssh 'true' >"$runtime_root/unknown.out" 2>"$runtime_root/unknown.err"; then
  printf 'Contained host-key verification unexpectedly accepted an unknown key.\n' >&2
  exit 1
fi

ssh-keyscan -T 2 -p "$port" 127.0.0.1 > "$runtime_root/known_hosts"
chmod 600 "$runtime_root/known_hosts"

# C504 proves the exact production SSH.NET password path against this same
# disposable loopback daemon. The fixture values remain process environment
# only and are never written to test output or artifacts.
VPSREADY_E3_DOTNET_PASSWORD="$login_value" VPSREADY_E3_DOTNET_USER="$service_user" VPSREADY_E3_DOTNET_PORT="$port" \
  VPSREADY_E3_STUB_SLEEP_MARKER="$sleep_marker" \
  VPSREADY_E3_AUTHORIZED_KEY_FILE="$authorized_file" \
  dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj --configuration Release --filter "Category=E3" --logger "trx;LogFileName=e3-production-sshnet.trx" --results-directory TestResults

set +e
run_ssh 'printf stdout; printf stderr >&2; exit 7' >"$runtime_root/command.out" 2>"$runtime_root/command.err"
command_status=$?
set -e
test "$command_status" -eq 7
test "$(cat "$runtime_root/command.out")" = 'stdout'
test "$(cat "$runtime_root/command.err")" = 'stderr'

wrong_askpass="$runtime_root/wrong-askpass"
cat > "$wrong_askpass" <<'ASKPASS'
#!/usr/bin/env bash
printf '%s\n' "$VPSREADY_E3_WRONG_VALUE"
ASKPASS
chmod 700 "$wrong_askpass"
if DISPLAY=':0' SSH_ASKPASS_REQUIRE=force SSH_ASKPASS="$wrong_askpass" VPSREADY_E3_WRONG_VALUE="$wrong_value" \
  VPSREADY_E3_KNOWN_HOSTS="$runtime_root/known_hosts" VPSREADY_E3_PORT="$port" VPSREADY_E3_USER="$service_user" \
  "$ssh_runner" 'true' >"$runtime_root/wrong.out" 2>"$runtime_root/wrong.err"; then
  printf 'Contained OpenSSH daemon unexpectedly accepted incorrect password authentication.\n' >&2
  exit 1
fi

set +e
DISPLAY=':0' SSH_ASKPASS_REQUIRE=force SSH_ASKPASS="$askpass" VPSREADY_E3_LOGIN_VALUE="$login_value" \
  VPSREADY_E3_KNOWN_HOSTS="$runtime_root/known_hosts" VPSREADY_E3_PORT="$port" VPSREADY_E3_USER="$service_user" \
  timeout 2 "$ssh_runner" 'sleep 15' >"$runtime_root/timeout.out" 2>"$runtime_root/timeout.err"
timeout_status=$?
set -e
if [[ "$timeout_status" == 0 ]]; then
  printf 'Contained OpenSSH timeout scenario unexpectedly completed.\n' >&2
  exit 1
fi
test "$timeout_status" -eq 124

mkdir -p TestResults/e3
cat > TestResults/e3/local-contained-protocol.txt <<'RESULT'
evidence_class=E3 local-contained protocol
environment=disposable Ubuntu-family loopback OpenSSH; no port published
production_sshnet_password_auth_and_reconnect=PASS
production_sshnet_generated_named_key_auth=PASS
production_sshnet_wrong_key_rejected=PASS
production_sshnet_unknown_host_fail_closed=PASS
production_sshnet_bounded_stdout_stderr_exit=PASS
production_sshnet_command_timeout_and_cancellation=PASS
openssh_cli_unknown_host_fail_closed=PASS
openssh_cli_known_host_match=PASS
openssh_cli_wrong_password_rejected=PASS
openssh_cli_stdout_stderr_exit=PASS
openssh_cli_timeout=PASS
cleanup=exit trap removes daemon,user,group,fixture and temporary files
REAL VPS: NOT TESTED
RESULT

printf 'E3 local-contained OpenSSH protocol checks passed. REAL VPS: NOT TESTED.\n'
