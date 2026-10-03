"""Offline contract probe. Pass an extracted, independently downloaded UFW source directory.

Imports upstream objects without backend initialization, command execution or
firewall access. Nothing is installed. Output contains only synthetic rules and
hashes of public upstream templates; no source is copied into product binaries.
"""
import builtins
import hashlib
import importlib.util
import json
import pathlib
import sys
import argparse

args = argparse.ArgumentParser(description=__doc__)
args.add_argument("source")
args.add_argument("--fixtures-dir", help="Create a NEW directory of offline generated test fixtures (never a system path)")
options = args.parse_args()

root = pathlib.Path(options.source).resolve()
spec = importlib.util.spec_from_file_location("ufw", root / "src/__init__.py", submodule_search_locations=[str(root / "src")])
module = importlib.util.module_from_spec(spec)
sys.modules["ufw"] = module
spec.loader.exec_module(module)
builtins._ = lambda text: text
import ufw.common
import ufw.backend_iptables
import ufw.frontend
import ufw.util

def forbidden(*args, **kwargs):
    raise AssertionError("Offline probe must never execute an external command")

ufw.util.cmd = forbidden
ufw.util.cmd_pipe = forbidden
ufw.backend_iptables.cmd = forbidden
ufw.backend_iptables.cmd_pipe = forbidden
backend = ufw.backend_iptables.UFWBackendIptables.__new__(ufw.backend_iptables.UFWBackendIptables)
v4 = ufw.common.UFWRule("allow", "tcp", "22")
v6 = ufw.common.UFWRule("allow", "tcp", "22", src="::/0", dst="::/0")
v6.set_v6(True)
backend.rules = [v4]
backend.rules6 = [v6]
frontend = ufw.frontend.UFWFrontend.__new__(ufw.frontend.UFWFrontend)
frontend.backend = backend
both = frontend.get_show_added()
backend.rules6 = []
one = frontend.get_show_added()
assert both == one
assert both.endswith("\nufw allow 22/tcp")

# Exercise the actual complete backend writer, not just one rule's formatter.
# Replace its file I/O with an in-memory sink; bypass capability probing with
# explicit synthetic capabilities. No backend initialization/commands occur.
backend.initcaps = lambda: None
backend.dryrun = False
backend.files = {"rules": str(root / "conf/user.rules"), "rules6": str(root / "conf/user6.rules")}
backend.chains = {kind: [f"{prefix}-{kind}-logging-{direction}" for prefix in ("ufw", "ufw6") for direction in ("input", "output", "forward")] for kind in ("before", "user", "after")}
backend.chains["misc"] = [f"{prefix}-logging-{action}" for prefix in ("ufw", "ufw6") for action in ("deny", "allow")]
backend.loglevels = {"off": 0, "low": 1, "medium": 2, "high": 3, "full": 4}
backend.ufw_user_limit_log = ["-m", "limit", "--limit", "3/minute", "-j", "LOG", "--log-prefix"]
backend.ufw_user_limit_log_text = "[UFW LIMIT BLOCK]"
sink = []
ufw.util.open_files = lambda path: {"tmp": 0}
ufw.util.close_files = lambda *unused: None
ufw.util.write_to_file = lambda fd, text: sink.append(text)

def normalized(text):
    return "\n".join(line for line in text.splitlines() if line.strip() and not line.lstrip().startswith("#")) + "\n"

framework_hashes = {}
fixtures = {}
for family in (4, 6):
    for limit in (False, True):
        for level in backend.loglevels:
            for port in (22, 2222):
                rule = ufw.common.UFWRule("allow", "tcp", str(port))
                rule.set_v6(family == 6)
                backend.rules = [rule] if family == 4 else []
                backend.rules6 = [rule] if family == 6 else []
                backend.caps = {"limit": {"4": limit, "6": limit}}
                backend.defaults = {"loglevel": level, "default_input_policy": "drop", "default_output_policy": "accept", "default_forward_policy": "drop"}
                sink.clear()
                backend._write_rules(family == 6)
                saved = "".join(sink)
                prefix = "ufw6" if family == 6 else "ufw"
                framework = "\n".join(line for line in normalized(saved).splitlines() if not line.startswith(f"-A {prefix}-user-input ")) + "\n"
                name = f"{family}-{port}-{level}-{int(limit)}"
                fixtures[name + ".rules"] = saved
                framework_hashes[f"{family}-{level}-{int(limit)}"] = hashlib.sha256(framework.encode()).hexdigest()
for family, name in ((4, "user.rules"), (6, "user6.rules")):
    framework_hashes[f"{family}-initial"] = hashlib.sha256(normalized((root / "conf" / name).read_text()).encode()).hexdigest()
if options.fixtures_dir:
    destination = pathlib.Path(options.fixtures_dir)
    destination.mkdir(parents=False, exist_ok=False)
    for name, contents in fixtures.items():
        (destination / name).write_text(contents)
hashes = {}
for name in ["before.rules", "after.rules", "before6.rules", "after6.rules", "sysctl.conf"]:
    lines = [line for line in (root / "conf" / name).read_text().splitlines() if line.strip() and not line.lstrip().startswith("#")]
    hashes[name] = hashlib.sha256(("\n".join(lines) + "\n").encode()).hexdigest()
print(json.dumps({"display_both": both, "display_ipv4_only": one,
                  "rule4": "-A ufw-user-input " + v4.format_rule(),
                  "rule6": "-A ufw6-user-input " + v6.format_rule(),
                  "saved_framework_sha256": framework_hashes,
                  "normalized_template_sha256": hashes}, indent=2))
