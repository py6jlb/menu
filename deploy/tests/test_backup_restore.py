"""Изолированные проверки установки, бэкапа и проверочного восстановления.

Docker, rclone и systemctl подменены исполняемыми адаптерами в PATH.
rclone-remote эмулируется локальным каталогом, поэтому выгрузка, список
объектов и скачивание проверяются без сети и настоящего хранилища.
"""
import gzip
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]

DOCKER_ADAPTER = r'''#!/usr/bin/env python3
import io, json, os, pathlib, sys
args = sys.argv[1:]
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps({"command": "docker", "args": args}) + "\n")
mode = os.environ.get("DOCKER_MODE", "ok")


def mapping(target):
    host = None
    for index, arg in enumerate(args):
        if arg == "-v" and index + 1 < len(args):
            spec = args[index + 1].split(":")
            if len(spec) >= 2 and spec[1] == target:
                host = spec[0]
    return host


if args and args[0] == "compose":
    rest = args[args.index("compose") + 1:]
    if "pg_dump" in rest:
        if mode == "pgdump-fail":
            sys.stderr.write("pg_dump: error: connection lost\n")
            sys.exit(2)
        sys.stdout.write("-- dump\nCREATE TABLE t();\n")
        sys.exit(0)
    if "ps" in rest and "-q" in rest:
        if mode == "backend-missing":
            sys.exit(0)
        sys.stdout.write("backendcid\n")
        sys.exit(0)
    sys.exit(0)

if args and args[0] == "inspect":
    if mode == "no-photos-volume":
        sys.exit(0)
    sys.stdout.write("photosvol\n")
    sys.exit(0)

if args and args[0] == "run":
    if "tar" in args:
        if mode == "archive-fail":
            sys.stderr.write("tar: error: cannot read\n")
            sys.exit(3)
        host = mapping("/backup")
        for arg in args:
            if arg.startswith("/backup/") and host:
                pathlib.Path(host, pathlib.Path(arg).name).write_bytes(b"FAKE-ARCHIVE")
        sys.exit(0)
    sys.exit(0)

if args and args[0] == "exec":
    if "pg_isready" in args:
        sys.exit(0)
    if "psql" in args:
        if "-c" in args:
            sys.stdout.write("12\n")
            sys.exit(0)
        sys.stdin.buffer.read()
        if mode == "sql-fail":
            sys.stderr.write("ERROR: relation \"x\" does not exist\n")
            sys.exit(3)
        sys.exit(0)
    sys.exit(0)

if args and args[0] == "rm":
    sys.exit(0)

sys.exit(0)
'''

RCLONE_ADAPTER = r'''#!/usr/bin/env python3
import json, os, pathlib, shutil, sys
args = sys.argv[1:]
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps({"command": "rclone", "args": args}) + "\n")
remote = os.environ.get("FAKE_REMOTE", "test:bucket")
root = pathlib.Path(os.environ["FAKE_REMOTE_DIR"])
fail = os.environ.get("RCLONE_FAIL", "").split(",")
code = int(os.environ.get("RCLONE_FAIL_CODE", "2"))
op = args[0] if args else ""


def localize(path):
    if path == remote:
        return root
    if path.startswith(remote + "/"):
        return root / path[len(remote) + 1:]
    return pathlib.Path(path)


if op == "listremotes":
    if os.environ.get("RCLONE_NO_REMOTE"):
        sys.exit(0)
    print(os.environ.get("FAKE_REMOTE_NAME", "test") + ":")
    sys.exit(0)

if op in fail:
    sys.exit(code)

if op == "copyto":
    source, destination = localize(args[1]), localize(args[2])
    if os.environ.get("RCLONE_COPYTO_NODATA"):
        sys.exit(0)
    if not source.exists():
        sys.exit(1)
    destination.parent.mkdir(parents=True, exist_ok=True)
    if source.is_dir():
        shutil.copytree(source, destination, dirs_exist_ok=True)
    else:
        shutil.copy(source, destination)
    sys.exit(0)

if op == "lsf":
    target = localize(args[1])
    if not target.exists():
        sys.exit(3)
    for entry in sorted(target.iterdir()):
        if entry.is_file():
            print(entry.name)
    sys.exit(0)

if op == "deletefile":
    target = localize(args[1])
    if target.exists():
        target.unlink()
    sys.exit(0)

sys.exit(0)
'''

SYSTEMCTL_ADAPTER = r'''#!/usr/bin/env python3
import json, os, sys
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps({"command": "systemctl", "args": sys.argv[1:]}) + "\n")
sys.exit(0)
'''


class BackupFixture(unittest.TestCase):
    SCRIPTS = ("config.sh", "backup.sh", "restore-drill.sh", "install-backup.sh")

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "deploy/systemd").mkdir(parents=True)
        for filename in self.SCRIPTS:
            shutil.copy(ROOT / "deploy" / filename, self.root / "deploy" / filename)
        for unit in ("menu-backup.service", "menu-backup.timer"):
            shutil.copy(ROOT / "deploy/systemd" / unit, self.root / "deploy/systemd" / unit)
        (self.root / "server.conf").unlink(missing_ok=True)

        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.calls_path = self.root / "calls.jsonl"
        self._adapter("docker", DOCKER_ADAPTER)
        self._adapter("rclone", RCLONE_ADAPTER)
        self._adapter("systemctl", SYSTEMCTL_ADAPTER)

        self.remote = self.root / "remote"
        (self.remote / "db").mkdir(parents=True)
        (self.remote / "photos").mkdir(parents=True)
        self.env = {
            "PATH": f"{self.bin}:{os.environ['PATH']}",
            "CALLS": str(self.calls_path),
            "DOCKERHUB_USER": "example",
            "POSTGRES_PASSWORD": "test-secret",
            "JWT_SECRET": "test-jwt",
            "BACKUP_REMOTE": "test:bucket",
            "FAKE_REMOTE_DIR": str(self.remote),
        }

    def _adapter(self, name, body):
        path = self.bin / name
        path.write_text(body)
        path.chmod(0o755)

    def run_script(self, script, env=None, args=()):
        return subprocess.run(
            ["bash", str(self.root / "deploy" / script), *args],
            cwd=self.root, env={**self.env, **(env or {})},
            capture_output=True, text=True)

    def calls_of(self, command):
        if not self.calls_path.exists():
            return []
        return [json.loads(line)["args"] for line in self.calls_path.read_text().splitlines()
                if json.loads(line)["command"] == command]

    def write_dump(self, name="2026-01-01.sql.gz"):
        path = self.remote / "db" / name
        path.write_bytes(gzip.compress(b"SELECT 1;\n"))
        return path

    def write_photos(self, name="2026-01-01.tar.gz"):
        path = self.root / name
        with tarfile.open(path, "w:gz") as archive:
            payload = self.root / "photo.txt"
            payload.write_text("photo\n")
            archive.add(payload, arcname="photo.txt")
        shutil.copy(path, self.remote / "photos" / name)
        return path


class BackupFailureTests(BackupFixture):
    def test_success_reaches_remote_and_declares_success(self):
        result = self.run_script("backup.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Готово", result.stdout)
        copyto = [call for call in self.calls_of("rclone") if call and call[0] == "copyto"]
        self.assertTrue(any("db/" in " ".join(call) for call in copyto))
        self.assertTrue(any("photos/" in " ".join(call) for call in copyto))

    def test_dump_failure_is_nonzero_without_success_log(self):
        result = self.run_script("backup.sh", {"DOCKER_MODE": "pgdump-fail"})
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("Готово", result.stdout)

    def test_archive_failure_is_nonzero_without_success_log(self):
        result = self.run_script("backup.sh", {"DOCKER_MODE": "archive-fail"})
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("Готово", result.stdout)

    def test_missing_backend_container_is_nonzero(self):
        result = self.run_script("backup.sh", {"DOCKER_MODE": "backend-missing"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("backend", result.stderr)
        self.assertNotIn("Готово", result.stdout)

    def test_missing_photos_volume_is_nonzero(self):
        result = self.run_script("backup.sh", {"DOCKER_MODE": "no-photos-volume"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("volume", result.stderr)
        self.assertNotIn("Готово", result.stdout)

    def test_upload_failure_is_nonzero_without_success_log(self):
        result = self.run_script("backup.sh", {"RCLONE_FAIL": "copyto"})
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("Готово", result.stdout)

    def test_upload_without_remote_object_is_nonzero(self):
        result = self.run_script("backup.sh", {"RCLONE_COPYTO_NODATA": "1"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Проверка выгрузки", result.stderr)
        self.assertNotIn("Готово", result.stdout)

    def test_storage_listing_error_is_nonzero(self):
        result = self.run_script("backup.sh", {"RCLONE_FAIL": "lsf", "RCLONE_FAIL_CODE": "2"})
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("Готово", result.stdout)


class RestoreDrillTests(BackupFixture):
    def test_runs_without_argument_and_verifies_remote_set(self):
        self.write_dump()
        self.write_photos()
        result = self.run_script("restore-drill.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Drill успешен", result.stdout)

        starts = [call for call in self.calls_of("docker") if call[:1] == ["run"] and "postgres:16" in call]
        self.assertEqual(len(starts), 1)
        self.assertIn("--network", starts[0])
        self.assertIn("none", starts[0])
        name = starts[0][starts[0].index("--name") + 1]
        self.assertNotEqual(name, "menu-restore-drill")
        self.assertTrue(name.startswith("menu-restore-drill-"))

        psql = [call for call in self.calls_of("docker") if "psql" in call]
        self.assertTrue(any("-v" in call and "ON_ERROR_STOP=1" in call for call in psql))
        self.assertTrue(any("-U" in call and call[call.index("-U") + 1] == "menu" for call in psql))
        self.assertTrue(any("-d" in call and call[call.index("-d") + 1] == "menu_planner" for call in psql))

        cleanup = [call for call in self.calls_of("docker") if call[:1] == ["rm"]]
        self.assertTrue(any("-f" in call and "-v" in call and name in call for call in cleanup))

    def test_explicit_empty_argument_is_treated_as_missing(self):
        self.write_dump()
        self.write_photos()
        result = self.run_script("restore-drill.sh", args=("",))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Drill успешен", result.stdout)

    def test_sql_error_aborts(self):
        self.write_dump()
        self.write_photos()
        result = self.run_script("restore-drill.sh", {"DOCKER_MODE": "sql-fail"})
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("Drill успешен", result.stdout)

    def test_configured_database_identity_is_used(self):
        self.write_dump()
        self.write_photos()
        (self.root / "server.conf").write_text(
            "POSTGRES_DB=family_db\nPOSTGRES_USER=family_user\n")
        result = self.run_script("restore-drill.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        starts = [call for call in self.calls_of("docker") if call[:1] == ["run"] and "postgres:16" in call]
        joined = " ".join(starts[0])
        self.assertIn("POSTGRES_DB=family_db", joined)
        self.assertIn("POSTGRES_USER=family_user", joined)
        psql = [call for call in self.calls_of("docker") if "psql" in call and "-c" not in call]
        self.assertTrue(any(call[call.index("-U") + 1] == "family_user" for call in psql))
        self.assertTrue(any(call[call.index("-d") + 1] == "family_db" for call in psql))

    def test_missing_photos_archive_is_not_success(self):
        self.write_dump()
        result = self.run_script("restore-drill.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("Drill успешен", result.stdout)
        self.assertIn("фото", result.stderr)

    def test_corrupt_dump_is_rejected_before_container_start(self):
        (self.remote / "db/2026-01-01.sql.gz").write_bytes(b"not a gzip stream")
        self.write_photos()
        result = self.run_script("restore-drill.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("Drill успешен", result.stdout)
        starts = [call for call in self.calls_of("docker") if call[:1] == ["run"] and "postgres:16" in call]
        self.assertEqual(starts, [])


class InstallBackupTests(BackupFixture):
    def setUp(self):
        super().setUp()
        self.user = subprocess.run(["id", "-un"], capture_output=True, text=True, check=True).stdout.strip()
        self.units = self.root / "etc-systemd"
        self.env.update({"DEPLOY_USER": self.user, "SYSTEMD_UNIT_DIR": str(self.units)})

    def test_installs_renders_and_enables_units(self):
        result = self.run_script("install-backup.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        service = (self.units / "menu-backup.service").read_text()
        self.assertIn(f"User={self.user}", service)
        self.assertIn(f"WorkingDirectory={self.root}", service)
        self.assertIn(f"ExecStart={self.root}/deploy/backup.sh", service)
        self.assertTrue((self.units / "menu-backup.timer").exists())
        actions = self.calls_of("systemctl")
        self.assertIn(["daemon-reload"], actions)
        self.assertIn(["enable", "--now", "menu-backup.timer"], actions)

    def test_unconfigured_remote_is_rejected(self):
        result = self.run_script("install-backup.sh", {"RCLONE_NO_REMOTE": "1"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("test", result.stderr)
        self.assertFalse(self.units.exists())
        self.assertEqual(self.calls_of("systemctl"), [])

    def test_missing_rclone_is_rejected(self):
        result = self.run_script("install-backup.sh", {"RCLONE_BIN": "rclone-absent-for-test"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("rclone", result.stderr)
        self.assertEqual(self.calls_of("systemctl"), [])

    def test_unreadable_backup_script_is_rejected(self):
        (self.root / "deploy/backup.sh").chmod(0o000)
        result = self.run_script("install-backup.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("backup.sh", result.stderr)
        self.assertFalse(self.units.exists())
        self.assertEqual(self.calls_of("systemctl"), [])


class DocumentationTests(unittest.TestCase):
    def test_readme_describes_delivered_install_without_checkout(self):
        readme = (ROOT / "deploy/README.md").read_text()
        self.assertIn("/opt/menu/deploy/install-backup.sh", readme)
        self.assertNotIn("restore-drill.sh ''", readme)
        self.assertNotIn("sudo install -m 644 deploy/systemd", readme)
        self.assertIn("rclone config", readme)

    def test_backup_drill_scripts_are_delivered(self):
        deploy = (ROOT / "deploy/deploy.sh").read_text()
        self.assertIn("install-backup.sh", deploy)


if __name__ == "__main__":
    unittest.main()
