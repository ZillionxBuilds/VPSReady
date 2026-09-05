#!/usr/bin/env bash
# E3 only: creates a disposable OpenSSH daemon bound to loopback on a hosted
# CI runner. It does not contact a public endpoint, a VPS, or an Owner host.
set -euo pipefail

if [[ "$(uname -s)" != 'Linux' ]] || ! command -v apt-get >/dev/null; then
  printf 'E3 local-contained protocol requires an Ubuntu-family CI runner.\n' >&2
  exit 2
fi

runtime_root="$(mktemp -d)"
service_user="vpsreadye3$RANDOM$RANDOM"
service_group="$service_user"
daemon_pid=''

cleanup() {
  local status=$?
  if [[ -n "$daemon_pid" ]] && kill -0 "$daemon_pid" 2>/dev/null; then
    kill "$daemon_pid" 2>/dev/null || true
    wait "$daemon_pid" 2>/dev/null || true
  fi
  sudo userdel --remove "$service_user" 2>/dev/null || true
  rm -rf "$runtime_root"
  exit "$status"
}
trap cleanup EXIT

sudo apt-get update -qq
sudo apt-get install -y --no-install-recommends openssh-client openssh-server >/dev/null

login_value="$(od -An -N18 -tx1 /dev/urandom | tr -d ' \n')"
wrong_value="$(od -An -N18 -tx1 /dev/urandom | tr -d ' \n')"
port="$(shuf -i 42000-52000 -n 1)"

sudo groupadd "$service_group"
sudo useradd --create-home --gid "$service_group" --shell /bin/sh "$service_user"
printf '%s:%s\n' "$service_user" "$login_value" | sudo chpasswd
sudo mkdir -p /run/sshd

ssh-keygen -q -t ed25519 -N '' -f "$runtime_root/host_ed25519" >/dev/null
cat > "$runtime_root/sshd_config" <<CONFIG
Port $port
ListenAddress 127.0.0.1
HostKey $runtime_root/host_ed25519
PidFile $runtime_root/sshd.pid
AuthorizedKeysFile none
PasswordAuthentication yes
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
UsePAM no
PermitRootLogin no
AllowUsers $service_user
PrintMotd no
LogLevel ERROR
CONFIG

sudo /usr/sbin/sshd -D -f "$runtime_root/sshd_config" -E "$runtime_root/sshd.log" &
daemon_pid=$!
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
environment=disposable loopback OpenSSH on the CI runner
password_auth=PASS
host_key_unknown_fail_closed=PASS
host_key_known_match=PASS
minimum_command_stdout_stderr_exit=PASS
wrong_password_rejected=PASS
timeout=PASS
cleanup=scheduled by exit trap
REAL VPS: NOT TESTED
RESULT

printf 'E3 local-contained OpenSSH protocol checks passed. REAL VPS: NOT TESTED.\n'
