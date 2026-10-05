"""Изолированные проверки повторяемого bootstrap без root.

Функции bootstrap.sh вызываются через `source` напрямую, системные пути
переопределены на временный каталог, а внешние команды (sshd, systemctl, ss,
docker, ufw, fail2ban-client, install, chown, visudo, runuser, ...) подменены
исполняемыми адаптерами в PATH. Root, sshd, Docker daemon и systemd не нужны.

Проверяются публичные швы:
  * merge_authorized_key — идемпотентное слияние без потери существующих ключей;
  * validate_sshd_config — отказ до применения при неверном sshd-конфиге;
  * apply_sshd / close_legacy_port — порядок «сначала проверка, потом закрытие»;
  * install_docker_repo / verify_docker — официальный repo и явный Compose v2;
  * verify_admin_path, verify_ssh_listener, verify_fail2ban_action.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
BOOTSTRAP = ROOT / "deploy/bootstrap.sh"

NEW_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAInewkeynewkeynewkey new@host"
EXISTING_KEY = "ssh-rsa AAAAB3NzaC1yc2EAAAAexistingexistingexisting existing@local"
DIFFERENT_COMMENT = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAInewkeynewkeynewkey other@host"
SECOND_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIsecondsecondsecondsecond second@host"

STUB = r'''#!/usr/bin/env python3
import json, os, pathlib, sys

name = os.path.basename(sys.argv[0])
args = sys.argv[1:]
calls = os.environ.get("CALLS")
if calls:
    with open(calls, "a") as stream:
        stream.write(json.dumps({"command": name, "args": args}) + "\n")

if name == "id":
    if args == ["-u"]:
        print("0")
        sys.exit(0)
    if args[:1] == ["-u"]:
        print("1000")
        sys.exit(0)
    sys.exit(1)
if name == "getent":
    if args[:1] == ["passwd"]:
        print("%s:x:1000:1000::%s:/bin/bash" % (args[1], os.environ["FAKE_HOME"]))
        sys.exit(0)
    sys.exit(1)
if name == "install":
    paths = []
    index = 0
    while index < len(args):
        arg = args[index]
        if arg in ("-m", "-o", "-g", "-t"):
            index += 2
            continue
        if arg.startswith("-"):
            index += 1
            continue
        paths.append(arg)
        index += 1
    for path in paths:
        if "-d" in args:
            pathlib.Path(path).mkdir(parents=True, exist_ok=True)
        else:
            pathlib.Path(path).parent.mkdir(parents=True, exist_ok=True)
            pathlib.Path(path).touch()
    sys.exit(0)
if name == "chown":
    sys.exit(0)
if name in ("adduser", "usermod", "visudo", "ufw", "sleep",
            "mkswap", "swapon", "timedatectl", "apt-get", "systemctl"):
    sys.exit(0)
if name == "fallocate":
    targets = [arg for arg in args if not arg.startswith("-") and arg != "2G"]
    for target in targets:
        pathlib.Path(target).touch()
    sys.exit(0)
if name == "runuser":
    sys.exit(1 if os.environ.get("RUNUSER_MODE") == "fail" else 0)
if name == "dpkg":
    if "--print-architecture" in args:
        print("amd64")
    sys.exit(0)
if name == "curl":
    if "-o" in args:
        pathlib.Path(args[args.index("-o") + 1]).write_text("fake-gpg-key\n")
    sys.exit(0)
if name == "ss":
    ports = os.environ.get("SS_PORTS", "8822,22").split(",")
    for port in ports:
        if port:
            print("LISTEN 0 128 0.0.0.0:%s 0.0.0.0:*" % port)
    sys.exit(0)
if name == "sshd":
    mode = os.environ.get("SSHD_MODE", "ok")
    if "-t" in args:
        sys.exit(1 if mode == "invalid" else 0)
    if "-T" in args:
        effective = os.environ.get("SSHD_EFFECTIVE")
        if effective and pathlib.Path(effective).exists():
            sys.stdout.write(pathlib.Path(effective).read_text())
        else:
            print("port 8822")
            print("port 22")
            print("pubkeyauthentication yes")
            print("passwordauthentication no")
            print("permitrootlogin prohibit-password")
        sys.exit(0)
    sys.exit(0)
if name == "docker":
    if args[:1] == ["--version"]:
        print("Docker version 27.0.0, build test")
        sys.exit(0)
    if args[:2] == ["compose", "version"]:
        print(os.environ.get("DOCKER_COMPOSE_VERSION", "Docker Compose version v2.29.7"))
        sys.exit(0)
    sys.exit(0)
if name == "fail2ban-client":
    state = pathlib.Path(os.environ["FAIL2BAN_STATE"])
    banned = set(state.read_text().split()) if state.exists() else set()
    if args == ["-t"]:
        sys.exit(0)
    if args[:2] == ["status", "sshd"]:
        print("Status for the jail: sshd")
        print("|- Filter")
        print("`- Actions")
        print("   `- Banned IP list:\t%s" % " ".join(sorted(banned)))
        sys.exit(0)
    if args[:3] == ["set", "sshd", "banip"]:
        banned.add(args[3])
        state.write_text(" ".join(sorted(banned)))
        sys.exit(0)
    if args[:3] == ["set", "sshd", "unbanip"]:
        banned.discard(args[3])
        state.write_text(" ".join(sorted(banned)))
        sys.exit(0)
    sys.exit(0)
if name == "rclone":
    sys.exit(0)
sys.exit(0)
'''


class BootstrapTestCase(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "deploy").mkdir()
        shutil.copy(BOOTSTRAP, self.root / "deploy/bootstrap.sh")

        self.home = self.root / "home/menu"
        self.home.mkdir(parents=True)
        self.ssh_dir = self.root / "etc/ssh/sshd_config.d"
        self.ssh_dir.mkdir(parents=True)
        self.unit_dir = self.root / "etc/systemd/system"
        self.unit_dir.mkdir(parents=True)
        self.sudoers_dir = self.root / "etc/sudoers.d"
        self.sudoers_dir.mkdir(parents=True)
        self.fail2ban_dir = self.root / "etc/fail2ban/jail.d"
        self.fail2ban_dir.mkdir(parents=True)
        self.apt_dir = self.root / "etc/apt/apt.conf.d"
        self.apt_dir.mkdir(parents=True)
        (self.root / "etc/apt/sources.list.d").mkdir(parents=True)
        (self.root / "etc/apt/keyrings").mkdir(parents=True)
        self.os_release = self.root / "etc/os-release"
        self.os_release.write_text('ID=ubuntu\nVERSION_CODENAME=noble\n')
        self.fstab = self.root / "etc/fstab"

        self.bin = self.root / "bin"
        self.bin.mkdir()
        for command in ("id", "getent", "install", "chown", "adduser", "usermod",
                        "visudo", "runuser", "dpkg", "curl", "ss", "sshd", "docker",
                        "fail2ban-client", "rclone", "ufw", "sleep", "fallocate",
                        "mkswap", "swapon", "timedatectl", "apt-get", "systemctl"):
            stub = self.bin / command
            stub.write_text(STUB)
            stub.chmod(0o755)
        self.calls = self.root / "calls.jsonl"

    def env(self, **overrides):
        base = {
            "PATH": f"{self.bin}:{os.environ['PATH']}",
            "CALLS": str(self.calls),
            "FAKE_HOME": str(self.home),
            "DEPLOY_USER": "menu",
            "SSH_PORT": "8822",
            "SSH_LEGACY_PORT": "22",
            "SSH_PUBLIC_KEY": NEW_KEY,
            "APP_DIR": str(self.root / "app"),
            "SSHD_CONFIG_DIR": str(self.ssh_dir),
            "SYSTEMD_UNIT_DIR": str(self.unit_dir),
            "SUDOERS_DIR": str(self.sudoers_dir),
            "FAIL2BAN_JAIL_DIR": str(self.fail2ban_dir),
            "UNATTENDED_CONF": str(self.apt_dir / "52-menu-unattended.conf"),
            "APT_AUTO_CONF": str(self.apt_dir / "20auto-upgrades"),
            "SWAPFILE": str(self.root / "swapfile"),
            "FSTAB": str(self.fstab),
            "OS_RELEASE_FILE": str(self.os_release),
            "DOCKER_KEYRING": str(self.root / "etc/apt/keyrings/docker.asc"),
            "DOCKER_APT_LIST": str(self.root / "etc/apt/sources.list.d/docker.list"),
            "LEGACY_MARKER": str(self.ssh_dir / ".menu-legacy-closed"),
            "FAIL2BAN_STATE": str(self.root / "f2b.state"),
            "SSHD_MODE": "ok",
            "SS_PORTS": "8822,22",
            "SSH_LISTEN_ATTEMPTS": "2",
        }
        base.update(overrides)
        return base

    def call(self, function, args=(), env=None):
        script = 'source "$1" || exit; shift; "$@"'
        return subprocess.run(
            ["bash", "-c", script, "bootstrap", str(self.root / "deploy/bootstrap.sh"),
             function, *args],
            cwd=self.root, env=self.env(**(env or {})), capture_output=True, text=True)

    def calls_list(self):
        if not self.calls.exists():
            return []
        return [json.loads(line) for line in self.calls.read_text().splitlines()]

    def run_bootstrap(self, env=None):
        return self.call("bootstrap", env=env)

    def authorized_keys(self):
        return (self.home / ".ssh/authorized_keys").read_text()


class AuthorizedKeysTests(BootstrapTestCase):
    def test_repeatable_runs_preserve_existing_and_avoid_duplicates(self):
        key_file = self.home / ".ssh/authorized_keys"
        key_file.parent.mkdir(parents=True)
        key_file.write_text(EXISTING_KEY + "\n# существующий комментарий\n")
        first = self.run_bootstrap()
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        second = self.run_bootstrap()
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        lines = self.authorized_keys().splitlines()
        self.assertEqual(lines.count(EXISTING_KEY), 1)
        self.assertEqual(lines.count(NEW_KEY), 1)
        self.assertIn("# существующий комментарий", lines)

    def test_same_key_with_different_comment_is_not_duplicated(self):
        self.assertEqual(self.call("merge_authorized_key",
                                   (str(self.home / ".ssh/authorized_keys"), NEW_KEY)).returncode, 0)
        result = self.call("merge_authorized_key",
                           (str(self.home / ".ssh/authorized_keys"), DIFFERENT_COMMENT))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.authorized_keys().splitlines(), [NEW_KEY])

    def test_missing_key_fails_without_touching_access(self):
        result = self.run_bootstrap(env={"SSH_PUBLIC_KEY": "", "SSH_PUBLIC_KEY_FILE": ""})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("SSH_PUBLIC_KEY", result.stderr)
        self.assertFalse((self.ssh_dir / "99-menu-hardening.conf").exists())

    def test_second_key_is_appended_and_invalid_key_is_rejected(self):
        path = str(self.home / ".ssh/authorized_keys")
        self.assertEqual(self.call("merge_authorized_key", (path, NEW_KEY)).returncode, 0)
        self.assertEqual(self.call("merge_authorized_key", (path, SECOND_KEY)).returncode, 0)
        self.assertEqual(self.authorized_keys().splitlines(), [NEW_KEY, SECOND_KEY])
        result = self.call("merge_authorized_key", (path, "not-a-key"))
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.authorized_keys().splitlines(), [NEW_KEY, SECOND_KEY])


class SshdValidationTests(BootstrapTestCase):
    def test_invalid_config_is_rejected_before_apply(self):
        result = self.run_bootstrap(env={"SSHD_MODE": "invalid"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("sshd -t", result.stderr)
        self.assertTrue((self.ssh_dir / "98-menu-legacy-port.conf").exists(),
                        "Старый порт не должен пропасть при неверной конфигурации")
        restarts = [call for call in self.calls_list()
                    if call["command"] == "systemctl" and "restart" in call["args"]]
        self.assertEqual(restarts, [], "Неверная конфигурация не должна применяться")

    def test_invalid_effective_config_is_rejected(self):
        effective = self.root / "effective.conf"
        effective.write_text("port 8822\nport 22\npubkeyauthentication yes\n"
                             "passwordauthentication yes\npermitrootlogin prohibit-password\n")
        result = self.call("validate_sshd_config", env={"SSHD_EFFECTIVE": str(effective)})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("паролю", result.stderr)

    def test_socket_activation_listens_on_both_ports(self):
        (self.ssh_dir / "98-menu-legacy-port.conf").write_text("Port 22\n")
        result = self.call("apply_sshd")
        self.assertEqual(result.returncode, 0, result.stderr)
        dropin = (self.unit_dir / "ssh.socket.d/10-menu-ports.conf").read_text()
        self.assertIn("ListenStream=8822", dropin)
        self.assertIn("ListenStream=22", dropin)
        self.assertIn({"command": "systemctl", "args": ["restart", "ssh.socket"]}, self.calls_list())

    def test_legacy_listen_stream_removed_when_legacy_conf_absent(self):
        (self.ssh_dir / ".menu-legacy-closed").touch()
        result = self.call("apply_sshd")
        self.assertEqual(result.returncode, 0, result.stderr)
        dropin = (self.unit_dir / "ssh.socket.d/10-menu-ports.conf").read_text()
        self.assertIn("ListenStream=8822", dropin)
        self.assertNotIn("ListenStream=22", dropin)


class CloseLegacyPortTests(BootstrapTestCase):
    def test_validates_before_closing_and_keeps_marker_after(self):
        first = self.run_bootstrap()
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        legacy = self.ssh_dir / "98-menu-legacy-port.conf"
        marker = self.ssh_dir / ".menu-legacy-closed"
        self.assertTrue(legacy.exists())
        self.calls.unlink(missing_ok=True)
        result = self.call("close_legacy_port")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(legacy.exists())
        self.assertTrue(marker.exists())
        dropin = (self.unit_dir / "ssh.socket.d/10-menu-ports.conf").read_text()
        self.assertIn("ListenStream=8822", dropin)
        self.assertNotIn("ListenStream=22", dropin, "старый порт не должен открыться снова")
        calls = self.calls_list()
        sshd_index = next(i for i, call in enumerate(calls) if call["command"] == "sshd")
        restart_index = next(i for i, call in enumerate(calls)
                             if call["command"] == "systemctl" and "restart" in call["args"])
        self.assertLess(sshd_index, restart_index, "Сначала проверка sshd, потом применение")

    def test_invalid_config_does_not_close_legacy_port(self):
        (self.ssh_dir / "98-menu-legacy-port.conf").write_text("Port 22\n")
        result = self.call("close_legacy_port", env={"SSHD_MODE": "invalid"})
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue((self.ssh_dir / "98-menu-legacy-port.conf").exists())
        self.assertFalse((self.ssh_dir / ".menu-legacy-closed").exists())

    def test_refuses_when_new_port_is_not_listening(self):
        (self.ssh_dir / "98-menu-legacy-port.conf").write_text("Port 22\n")
        result = self.call("close_legacy_port", env={"SS_PORTS": "22", "SSH_LISTEN_ATTEMPTS": "1"})
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue((self.ssh_dir / "98-menu-legacy-port.conf").exists())


class DockerAndToolsTests(BootstrapTestCase):
    def test_official_repository_with_signed_key_is_used(self):
        result = self.call("install_docker_repo")
        self.assertEqual(result.returncode, 0, result.stderr)
        source = (self.root / "etc/apt/sources.list.d/docker.list").read_text()
        self.assertIn("download.docker.com/linux/ubuntu", source)
        self.assertIn("signed-by=", source)
        source_calls = " ".join(" ".join(call["args"]) for call in self.calls_list())
        self.assertIn("download.docker.com/linux/ubuntu/gpg", source_calls)
        self.assertNotIn("get.docker.com", source_calls)

    def test_compose_v2_is_checked_explicitly(self):
        self.assertEqual(self.call("verify_docker").returncode, 0)
        for version in ("Docker Compose version v1.29.2", "", "docker-compose 1.29.2"):
            with self.subTest(version=version):
                result = self.call("verify_docker", env={"DOCKER_COMPOSE_VERSION": version})
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("Compose v2", result.stderr)

    def test_missing_tool_is_rejected(self):
        (self.bin / "rclone").unlink()
        result = self.call("verify_docker")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("rclone", result.stderr)


class AdminAndFail2banTests(BootstrapTestCase):
    def test_admin_path_uses_nopasswd_and_is_verified(self):
        result = self.call("configure_admin_access")
        self.assertEqual(result.returncode, 0, result.stderr)
        sudoers = (self.sudoers_dir / "90-menu-menu").read_text()
        self.assertIn("NOPASSWD:", sudoers)
        self.assertIn("menu ALL=(root)", sudoers)
        self.assertTrue(any(call["command"] == "runuser" for call in self.calls_list()))

    def test_broken_admin_path_is_rejected(self):
        result = self.call("verify_admin_path", env={"RUNUSER_MODE": "fail"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("sudo", result.stderr)

    def test_fail2ban_real_ban_action_is_verified(self):
        result = self.call("verify_fail2ban_action")
        self.assertEqual(result.returncode, 0, result.stderr)
        state = (self.root / "f2b.state").read_text().strip()
        self.assertEqual(state, "", "Пробный бан должен быть снят после проверки")
        bans = [call for call in self.calls_list()
                if call["command"] == "fail2ban-client" and call["args"][:3] == ["set", "sshd", "banip"]]
        self.assertEqual(len(bans), 1)

    def test_fail2ban_jail_targets_the_selected_port(self):
        result = self.call("configure_fail2ban")
        self.assertEqual(result.returncode, 0, result.stderr)
        jail = (self.fail2ban_dir / "menu-sshd.local").read_text()
        self.assertIn("port = 8822", jail)
        self.assertIn({"command": "systemctl", "args": ["restart", "fail2ban"]}, self.calls_list())


if __name__ == "__main__":
    unittest.main()
