"""Структурные проверки наблюдаемости и диагностики (тикет 71).

Тесты читают поставляемые артефакты (Collector, Compose, Caddyfile, deploy-скрипты)
без запуска Docker daemon. Проверяются: экспорт только по настроенным pipelines,
границы памяти/пакета, права и ротация volume, release-id backend, минимизация
share-токенов в журналах края и поиск контрольной записи по trace-id.
"""
import re
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


def service_block(text, name):
    lines = text.splitlines()
    collected = []
    inside = False
    for line in lines:
        if re.match(rf"^  {re.escape(name)}:\s*$", line):
            inside = True
            collected.append(line)
            continue
        if inside:
            if re.match(r"^  [A-Za-z0-9_-]+:\s*$", line) or re.match(r"^[A-Za-z]", line):
                break
            collected.append(line)
    return "\n".join(collected)


class CollectorPipelineTests(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / "deploy/otel-collector.yaml").read_text()

    def test_only_logs_pipeline_is_configured(self):
        self.assertRegex(self.text, r"(?m)^\s*logs:")
        self.assertNotRegex(self.text, r"(?m)^\s*traces:")
        self.assertNotRegex(self.text, r"(?m)^\s*metrics:")

    def test_memory_and_batch_are_bounded(self):
        self.assertIn("memory_limiter", self.text)
        self.assertRegex(self.text, r"limit_mib:\s*\d+")
        self.assertRegex(self.text, r"spike_limit_mib:\s*\d+")
        self.assertRegex(self.text, r"send_batch_max_size:\s*\d+")
        self.assertRegex(self.text, r"timeout:\s*\d+")

    def test_file_rotation_is_defined(self):
        self.assertIn("/var/log/otel/logs.json", self.text)
        self.assertRegex(self.text, r"max_megabytes:\s*\d+")
        self.assertRegex(self.text, r"max_days:\s*\d+")
        self.assertRegex(self.text, r"max_backups:\s*\d+")


class CollectorVolumeRightsTests(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / "docker-compose.prod.yml").read_text()

    def test_init_prepares_volume_for_collector_user(self):
        init = service_block(self.text, "otel-logs-init")
        self.assertIn("chown", init)
        self.assertIn("10001:10001", init)
        self.assertIn("otel_logs:/var/log/otel", init)

    def test_collector_runs_as_unprivileged_uid_and_waits_for_init(self):
        collector = service_block(self.text, "otel-collector")
        self.assertIn('user: "10001:10001"', collector)
        self.assertIn("service_completed_successfully", collector)
        self.assertIn("no-new-privileges:true", collector)

    def test_collector_does_not_publish_ports(self):
        collector = service_block(self.text, "otel-collector")
        self.assertNotIn("ports:", collector)


class BackendReleaseIdentityTests(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / "docker-compose.prod.yml").read_text()

    def test_backend_receives_release_id(self):
        backend = service_block(self.text, "backend")
        self.assertRegex(backend, r'RELEASE_ID:\s*"\$\{IMAGE_TAG[^"]*}"')

    def test_traces_export_is_explicitly_disabled(self):
        backend = service_block(self.text, "backend")
        self.assertIn("OTEL_TRACES_EXPORT_ENABLED:", backend)
        self.assertIn('"false"', backend)


class EdgeLogMinimizationTests(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / "deploy/Caddyfile").read_text()

    def test_global_filter_redacts_share_tokens_in_paths(self):
        self.assertIn("request>uri regexp", self.text)
        self.assertRegex(self.text, r"api/shared\|r")
        self.assertIn("REDACTED", self.text)

    def test_access_logging_is_enabled(self):
        self.assertRegex(self.text, r"(?m)^\s*log\s*$")


class DiagnosticsScriptTests(unittest.TestCase):
    def setUp(self):
        self.script = (ROOT / "deploy/logs.sh").read_text()

    def test_looks_up_control_record_by_trace_id(self):
        self.assertIn("OTEL_LOGS_VOLUME", self.script)
        self.assertIn("logs.json", self.script)
        self.assertIn("grep -F", self.script)

    def test_rejects_unsafe_trace_id(self):
        self.assertIn("[A-Za-z0-9._-]", self.script)

    def test_script_is_delivered_and_hashed(self):
        self.assertIn("deploy/logs.sh", (ROOT / "deploy/release.sh").read_text())
        self.assertIn("deploy/logs.sh", (ROOT / "deploy/deploy.sh").read_text())


class ObservabilityDocumentationTests(unittest.TestCase):
    def test_readme_documents_pipelines_window_and_lookup(self):
        readme = (ROOT / "deploy/README.md").read_text()
        for marker in (
            "Наблюдаемость",
            "OTEL_TRACES_EXPORT_ENABLED",
            "OTEL_LOG_QUEUE_SIZE",
            "trace-id",
            "logs.sh",
            "10001:10001",
            "не более 10",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, readme)


if __name__ == "__main__":
    unittest.main()
