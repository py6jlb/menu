"""Проверки тикета 72: недоступность, старые бэкапы и нехватка ресурсов.

Транспорт подменён исполняемыми адаптерами в PATH: `curl` (публичный путь,
канал оповещения, heartbeat), `rclone` (complete-наборы в локальном каталоге),
`df`/`nproc` (ресурсы), `systemctl` (units). Внешний край эмулируется каталогом
`EDGE_DIR`: путь URL отображается в файл, `.status` задаёт код ответа. Реальные
VPS, сеть, Docker, systemd и объектное хранилище не запускаются; все секреты
вымышленные.
"""
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

from test_backup_restore import RCLONE_ADAPTER


ROOT = Path(__file__).resolve().parents[2]

CURL_ADAPTER = r'''#!/usr/bin/env python3
import json, os, pathlib, sys
args = sys.argv[1:]
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps({"command": "curl", "args": args}) + "\n")


def value(flag):
    if flag in args:
        return args[args.index(flag) + 1]
    return None


config = value("--config")
if os.environ.get("CURL_CONNECT_FAIL") and not config:
    sys.stderr.write("curl: (7) Failed to connect\n")
    sys.exit(7)

url = None
for arg in args:
    if arg.startswith("http://") or arg.startswith("https://"):
        url = arg
if config:
    for line in pathlib.Path(config).read_text().splitlines():
        if line.strip().startswith("url"):
            url = line.split("=", 1)[1].strip().strip('"')
if url:
    with open(os.path.join(os.environ["CALLS_DIR"], "urls.txt"), "a") as stream:
        stream.write(url + "\n")

data = value("--data-binary")
if data and data.startswith("@"):
    with open(os.path.join(os.environ["CALLS_DIR"], "posts.txt"), "a") as stream:
        stream.write(pathlib.Path(data[1:]).read_text() + "\n")

path = "/"
if url:
    rest = url.split("://", 1)[1]
    path = "/" + rest.split("/", 1)[1] if "/" in rest else "/"
    path = path.split("?", 1)[0]

webhook = os.environ.get("WEBHOOK_PATH", "/hook")
heartbeat = os.environ.get("HEARTBEAT_PATH", "/heartbeat")
if path == webhook:
    code = 500 if os.environ.get("WEBHOOK_FAIL") else int(os.environ.get("WEBHOOK_CODE", "200"))
elif path == heartbeat:
    code = 500 if os.environ.get("HEARTBEAT_FAIL") else 200
else:
    edge = pathlib.Path(os.environ["EDGE_DIR"])
    key = "index.html" if path == "/" else path.lstrip("/")
    body_file = edge / key
    status_file = edge / (key + ".status")
    if status_file.exists():
        code = int(status_file.read_text().strip())
    else:
        code = 200 if body_file.exists() else 404
    body = body_file.read_text() if body_file.exists() else ""
    out = value("-o")
    if out and out != "/dev/null":
        pathlib.Path(out).write_text(body)

sys.stdout.write(str(code))
sys.exit(0)
'''

DF_ADAPTER = r'''#!/usr/bin/env python3
import os, pathlib, sys
sequence = os.environ.get("FAKE_DF_SEQUENCE")
if sequence:
    entries = [entry for entry in sequence.split(";") if entry]
    counter = pathlib.Path(os.environ["CALLS_DIR"]) / "df.n"
    index = int(counter.read_text()) if counter.exists() else 0
    entry = entries[index % len(entries)]
    counter.write_text(str(index + 1))
    avail, used = entry.split(",")
else:
    avail = os.environ.get("FAKE_DF_AVAILABLE_MB", "50000")
    used = os.environ.get("FAKE_DF_USED_PERCENT", "10")
sys.stdout.write("Filesystem 1024-blocks Used Available Capacity Mounted on\n")
sys.stdout.write("/dev/fake 100000 50000 %s %s%% /app\n" % (avail, used))
'''

NPROC_ADAPTER = r'''#!/usr/bin/env python3
import os, sys
sys.stdout.write(os.environ.get("FAKE_NPROC", "4") + "\n")
'''

SYSTEMCTL_ADAPTER = r'''#!/usr/bin/env python3
import json, os, sys
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps({"command": "systemctl", "args": sys.argv[1:]}) + "\n")
sys.exit(0)
'''

HOSTNAME_ADAPTER = r'''#!/usr/bin/env python3
import os, sys
sys.stdout.write(os.environ.get("FAKE_HOSTNAME", "test-host") + "\n")
'''


class AlertFixture(unittest.TestCase):
    SCRIPTS = ("config.sh", "release.sh", "backup-lib.sh", "deploy-lib.sh",
               "alert-lib.sh", "alert.sh", "install-monitor.sh")
    UNITS = ("menu-monitor.service", "menu-monitor.timer")

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "deploy/systemd").mkdir(parents=True)
        for filename in self.SCRIPTS:
            source = ROOT / "deploy" / filename
            if source.exists():
                shutil.copy(source, self.root / "deploy" / filename)
        for unit in self.UNITS:
            source = ROOT / "deploy/systemd" / unit
            if source.exists():
                shutil.copy(source, self.root / "deploy/systemd" / unit)

        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.calls_path = self.root / "calls.jsonl"
        self._adapter("curl", CURL_ADAPTER)
        self._adapter("rclone", RCLONE_ADAPTER)
        self._adapter("df", DF_ADAPTER)
        self._adapter("nproc", NPROC_ADAPTER)
        self._adapter("systemctl", SYSTEMCTL_ADAPTER)
        self._adapter("hostname", HOSTNAME_ADAPTER)

        self.edge = self.root / "edge"
        self.edge.mkdir()
        self.healthy_edge()

        self.remote = self.root / "remote"
        for name in ("db", "photos", "manifests", "complete"):
            (self.remote / name).mkdir(parents=True)
        self.seed_complete(hours_ago=1)

        self.meminfo = self.root / "meminfo"
        self.loadavg = self.root / "loadavg"
        self.write_meminfo(1024)
        self.write_loadavg(0.10)

        self.state = self.root / "alert-state"
        self.env = {
            "PATH": f"{self.bin}:{os.environ['PATH']}",
            "CALLS": str(self.calls_path),
            "CALLS_DIR": str(self.root),
            "DOCKERHUB_USER": "example",
            "POSTGRES_PASSWORD": "test-secret",
            "JWT_SECRET": "test-jwt",
            "DEPLOYMENT_MODE": "lab",
            "BACKUP_REMOTE": "test:bucket",
            "FAKE_REMOTE_DIR": str(self.remote),
            "BACKUP_STATE_FILE": str(self.root / "backup-state"),
            "ALERT_STATE_FILE": str(self.state),
            "ALERT_WEBHOOK_URL": "http://alerts.test/hook?token=webhook-secret-123",
            "ALERT_HEARTBEAT_URL": "http://alerts.test/heartbeat?token=deadman-secret-456",
            "ALERT_PUBLIC_URL": "http://localhost",
            "ALERT_INSTALL_ID": "test-install",
            "ALERT_HTTP_TIMEOUT_SECONDS": "5",
            "ALERT_BREACH_SAMPLES": "1",
            "ALERT_SAMPLE_INTERVAL_SECONDS": "0",
            "BACKUP_MAX_AGE_HOURS": "26",
            "ALERT_DEDUP_SECONDS": "3600",
            "ALERT_DISK_MIN_FREE_MB": "2048",
            "ALERT_DISK_MAX_USED_PERCENT": "90",
            "ALERT_MEM_MIN_MB": "256",
            "ALERT_LOAD_MAX_PER_CPU": "2",
            "ALERT_CPU_COUNT": "4",
            "ALERT_PROC_MEMINFO": str(self.meminfo),
            "ALERT_PROC_LOADAVG": str(self.loadavg),
            "EDGE_DIR": str(self.edge),
        }

    def _adapter(self, name, body):
        path = self.bin / name
        path.write_text(body)
        path.chmod(0o755)

    def healthy_edge(self):
        (self.edge / "ready").write_text('{"status":"ready","service":"menu"}\n')
        (self.edge / "index.html").write_text("<!doctype html><div id=app></div>\n")

    def set_edge(self, key, body=None, status=None):
        path = self.edge / key
        path.parent.mkdir(parents=True, exist_ok=True)
        if body is not None:
            path.write_text(body)
        if status is not None:
            (self.edge / (key + ".status")).write_text(str(status))

    def clear_edge_status(self, key):
        (self.edge / (key + ".status")).unlink(missing_ok=True)

    def write_meminfo(self, available_mb):
        self.meminfo.write_text(
            f"MemTotal: 2097152 kB\nMemAvailable: {int(available_mb) * 1024} kB\n")

    def write_loadavg(self, load):
        self.loadavg.write_text(f"{load} 0.10 0.10 1/100 1\n")

    def seed_complete(self, hours_ago, set_id=None):
        set_id = set_id or self.set_id(hours_ago)
        (self.remote / "complete" / set_id).write_text(set_id + "\n")
        return set_id

    def seed_incomplete(self, hours_ago):
        set_id = self.set_id(hours_ago)
        (self.remote / "db" / f"{set_id}.sql.gz").write_text("partial\n")
        return set_id

    def drop_complete(self):
        for path in (self.remote / "complete").iterdir():
            path.unlink()

    @staticmethod
    def set_id(hours_ago):
        stamp = datetime.now(timezone.utc) - timedelta(hours=hours_ago)
        return stamp.strftime("%Y-%m-%dT%H%M%S") + "000000000Z"

    def run_script(self, script="alert.sh", env=None, args=()):
        return subprocess.run(
            ["bash", str(self.root / "deploy" / script), *args],
            cwd=self.root, env={**self.env, **(env or {})},
            capture_output=True, text=True)

    def calls_of(self, command):
        if not self.calls_path.exists():
            return []
        return [json.loads(line)["args"] for line in self.calls_path.read_text().splitlines()
                if json.loads(line)["command"] == command]

    def curl_calls(self):
        return self.calls_of("curl")

    def urls(self):
        path = self.root / "urls.txt"
        if not path.exists():
            return []
        return [line for line in path.read_text().splitlines() if line]

    def posts(self):
        path = self.root / "posts.txt"
        if not path.exists():
            return []
        return [line for line in path.read_text().splitlines() if line]

    def webhook_posts(self):
        return [post for post in self.posts()
                if any(kind in post for kind in ("ALERT", "RECOVERED", "TEST"))]


class AvailabilityTests(AlertFixture):
    def test_public_path_and_readiness_failure_alerts(self):
        self.set_edge("ready", status=503, body="")
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        posts = self.webhook_posts()
        self.assertEqual(len(posts), 1)
        self.assertIn("public_path", posts[0])
        self.assertIn("next=", posts[0])

    def test_unreachable_public_path_alerts_with_connect_cause(self):
        result = self.run_script(env={"CURL_CONNECT_FAIL": "1"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(any("connect" in post for post in self.webhook_posts()))

    def test_healthy_run_has_no_alert_and_pings_heartbeat(self):
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.webhook_posts(), [])
        self.assertTrue(any("heartbeat" in url for url in self.urls()),
                        "heartbeat не отправлен")

    def test_spa_without_html_alerts(self):
        (self.edge / "index.html").write_text("plain text, not html\n")
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(any("public_path" in post and "spa" in post
                            for post in self.webhook_posts()),
                        "SPA без HTML не дала тревоги")


class BackupAgeTests(AlertFixture):
    def test_incomplete_upload_does_not_count_as_backup(self):
        self.drop_complete()
        self.seed_incomplete(hours_ago=1)
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        posts = self.webhook_posts()
        self.assertTrue(any("backup" in post and "missing" in post for post in posts),
                        f"неполный набор не дал тревоги: {posts}")

    def test_stale_complete_set_alerts_with_age(self):
        self.drop_complete()
        self.seed_complete(hours_ago=100)
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        posts = self.webhook_posts()
        self.assertTrue(any("backup" in post and "stale" in post for post in posts),
                        f"старый complete-набор не дал тревоги: {posts}")

    def test_fresh_complete_set_is_healthy(self):
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse([post for post in self.webhook_posts() if "backup" in post])


class ResourceTests(AlertFixture):
    def test_single_spike_does_not_alarm(self):
        env = {"ALERT_BREACH_SAMPLES": "2", "ALERT_SAMPLE_INTERVAL_SECONDS": "0",
               "FAKE_DF_SEQUENCE": "10,99;50000,10"}
        result = self.run_script(env=env)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse([post for post in self.webhook_posts() if "disk" in post],
                         "краткий всплеск вызвал ложную тревогу")

    def test_sustained_disk_pressure_alerts(self):
        env = {"ALERT_BREACH_SAMPLES": "2", "ALERT_SAMPLE_INTERVAL_SECONDS": "0",
               "FAKE_DF_SEQUENCE": "10,99;10,99"}
        result = self.run_script(env=env)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(any("disk" in post for post in self.webhook_posts()),
                        "устойчивое давление диска не дало тревоги")

    def test_memory_pressure_alerts_with_configurable_threshold(self):
        self.write_meminfo(100)
        result = self.run_script(env={"ALERT_MEM_MIN_MB": "256"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(any("memory" in post and "available" in post
                            for post in self.webhook_posts()),
                        "давление памяти не дало тревоги")

    def test_load_pressure_alerts_with_configurable_threshold(self):
        self.write_loadavg(100.0)
        result = self.run_script(env={"ALERT_LOAD_MAX_PER_CPU": "2"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(any("load" in post for post in self.webhook_posts()),
                        "высокая нагрузка не дала тревоги")

    def test_unreadable_metric_alerts_without_false_recovery(self):
        self.write_meminfo(100)
        first = self.run_script(env={"ALERT_MEM_MIN_MB": "256"})
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        self.assertTrue(any("memory" in post and "available" in post
                            for post in self.webhook_posts()))
        second = self.run_script(env={
            "ALERT_MEM_MIN_MB": "256",
            "ALERT_PROC_MEMINFO": str(self.root / "missing-meminfo")})
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        posts = self.webhook_posts()
        self.assertEqual(len(posts), 2, f"нечитаемая метрика не дала отдельной тревоги: {posts}")
        self.assertIn("unreadable", posts[1])
        self.assertNotIn("RECOVERED", posts[1],
                         "нечитаемая метрика выдана за восстановление")


class ChannelTests(AlertFixture):
    def test_missing_channel_is_rejected_before_external_actions(self):
        result = self.run_script(env={"ALERT_WEBHOOK_URL": ""})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("ALERT_WEBHOOK_URL", result.stderr)
        self.assertEqual(self.curl_calls(), [])

    def test_test_notification_works_without_incident(self):
        result = self.run_script(args=("--test",))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        posts = self.posts()
        self.assertEqual(len(posts), 1)
        self.assertIn("TEST", posts[0])
        self.assertIn("test-install", posts[0])

    def test_delivery_failure_is_reported_nonzero(self):
        result = self.run_script(args=("--test",), env={"WEBHOOK_FAIL": "1"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("доставка не удалась", result.stderr)

    def test_heartbeat_failure_is_reported_nonzero(self):
        result = self.run_script(env={"HEARTBEAT_FAIL": "1"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("heartbeat", result.stderr)

    def test_secret_channel_is_not_logged_or_put_in_argv(self):
        self.set_edge("ready", status=503, body="")
        result = self.run_script()
        combined = result.stdout + result.stderr
        self.assertNotIn("webhook-secret-123", combined)
        self.assertNotIn("deadman-secret-456", combined)
        for call in self.curl_calls():
            for arg in call:
                self.assertNotIn("webhook-secret-123", arg)
                self.assertNotIn("deadman-secret-456", arg)
        if self.state.exists():
            state = self.state.read_text()
            self.assertNotIn("webhook-secret-123", state)
            self.assertNotIn("deadman-secret-456", state)

    def test_message_contains_time_install_release_problem_and_next_step(self):
        self.set_edge("ready", status=503, body="")
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        post = self.webhook_posts()[0]
        for marker in ("test-install", "time=", "release=", "public_path", "next="):
            with self.subTest(marker=marker):
                self.assertIn(marker, post)


class DedupTests(AlertFixture):
    def test_duplicate_event_is_suppressed_within_window(self):
        self.set_edge("ready", status=503, body="")
        first = self.run_script()
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        second = self.run_script()
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        self.assertEqual(len(self.webhook_posts()), 1, "повторное событие не дедуплицировано")

    def test_recovery_sends_separate_message_and_clears_state(self):
        self.set_edge("ready", status=503, body="")
        first = self.run_script()
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        self.set_edge("ready", status=200, body='{"status":"ready"}\n')
        self.clear_edge_status("ready")
        second = self.run_script()
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        posts = self.webhook_posts()
        self.assertEqual(len(posts), 2)
        self.assertIn("RECOVERED", posts[1])
        self.assertIn("public_path", posts[1])


class StructuralTests(unittest.TestCase):
    def test_monitor_units_are_bounded_and_installed(self):
        service = (ROOT / "deploy/systemd/menu-monitor.service").read_text()
        for marker in ("Type=oneshot", "User=", "ExecStart=/opt/menu/deploy/alert.sh",
                       "MemoryMax=", "CPUQuota=", "Nice="):
            with self.subTest(marker=marker):
                self.assertIn(marker, service)
        timer = (ROOT / "deploy/systemd/menu-monitor.timer").read_text()
        self.assertIn("OnUnitActiveSec=5min", timer)
        self.assertIn("Persistent=true", timer)

    def test_alert_scripts_are_delivered_and_hashed(self):
        release = (ROOT / "deploy/release.sh").read_text()
        deploy = (ROOT / "deploy/deploy.sh").read_text()
        for script in ("deploy/alert-lib.sh", "deploy/alert.sh"):
            with self.subTest(script=script):
                self.assertIn(script, release)
                self.assertIn(script, deploy)
        self.assertIn("deploy/install-monitor.sh", deploy)
        self.assertIn("menu-monitor", deploy)

    def test_readme_documents_limits_channel_and_verification(self):
        readme = (ROOT / "deploy/README.md").read_text()
        for marker in ("Обнаружение недоступности", "ALERT_WEBHOOK_URL",
                       "полном отказе VPS", "не выдаётся за внешний",
                       "ALERT_HEARTBEAT_URL", "проверочное уведомление",
                       "дедуплиц", "следующий диагностический"):
            with self.subTest(marker=marker):
                self.assertIn(marker, readme)

    def test_install_monitor_requires_channel_and_installs_units(self):
        script = (ROOT / "deploy/install-monitor.sh").read_text()
        self.assertIn("ALERT_WEBHOOK_URL", script)
        self.assertIn("menu-monitor.service", script)
        self.assertIn("menu-monitor.timer", script)
        self.assertIn("daemon-reload", script)
        self.assertIn("enable", script)


class InstallMonitorTests(AlertFixture):
    def setUp(self):
        super().setUp()
        self.user = subprocess.run(["id", "-un"], capture_output=True, text=True,
                                   check=True).stdout.strip()
        self.units = self.root / "etc-systemd"
        self.env.update({"DEPLOY_USER": self.user, "SYSTEMD_UNIT_DIR": str(self.units)})

    def test_installs_renders_and_enables_monitor_units(self):
        result = self.run_script("install-monitor.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        service = (self.units / "menu-monitor.service").read_text()
        self.assertIn(f"User={self.user}", service)
        self.assertIn(f"ExecStart={self.root}/deploy/alert.sh", service)
        self.assertTrue((self.units / "menu-monitor.timer").exists())
        actions = self.calls_of("systemctl")
        self.assertIn(["daemon-reload"], actions)
        self.assertIn(["enable", "--now", "menu-monitor.timer"], actions)

    def test_unconfigured_channel_is_rejected_before_install(self):
        result = self.run_script("install-monitor.sh", env={"ALERT_WEBHOOK_URL": ""})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("ALERT_WEBHOOK_URL", result.stderr)
        self.assertFalse(self.units.exists())
        self.assertEqual(self.calls_of("systemctl"), [])


if __name__ == "__main__":
    unittest.main()
