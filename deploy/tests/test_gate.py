"""Проверки автоматического гейта релиза.

Гейт запускается в изолированном временном репозитории: versioned
`release-gate.sh` + `deploy/release.sh`, stub-скрипты этапов и подменённый
`docker` в PATH. Реальные backend/frontend/Postgres не запускаются; проверяется
оркестрация, выбор этапов, диагностика при провале и отсутствие публикации.

Отдельно проверяется настоящий `scripts/check-infra.sh` на намеренно неверной
Compose-конфигурации (это единственный реальный Docker CLI вызов — `config`).
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]

DOCKER_STUB = r'''#!/usr/bin/env python3
import json, os, sys

args = sys.argv[1:]
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps(args) + "\n")

needle = os.environ.get("DOCKER_FAIL_SUBSTRING", "")
if needle and needle in " ".join(args):
    sys.stderr.write(os.environ.get("DOCKER_FAIL_MESSAGE", "stub failure") + "\n")
    sys.exit(2)
sys.exit(0)
'''

STUB_SCRIPTS = ("test-postgres.sh", "check-infra.sh", "check-dependencies.sh",
                "browser-smoke.sh")


class GateHarness(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "deploy").mkdir()
        (self.root / "deploy/tests").mkdir()
        (self.root / "deploy/tests/test_placeholder.py").write_text(
            "import unittest\n\n"
            "class Placeholder(unittest.TestCase):\n"
            "    def test_ok(self):\n"
            "        self.assertTrue(True)\n")
        (self.root / "frontend").mkdir()
        (self.root / "frontend/package-lock.json").write_text("{}\n")
        shutil.copy(ROOT / "deploy/release.sh", self.root / "deploy/release.sh")

        (self.root / "scripts").mkdir()
        gate = self.root / "scripts/release-gate.sh"
        shutil.copy(ROOT / "scripts/release-gate.sh", gate)
        gate.chmod(0o755)
        for name in STUB_SCRIPTS:
            stub = self.root / "scripts" / name
            stub.write_text("#!/usr/bin/env bash\nexit 0\n")
            stub.chmod(0o755)

        (self.root / ".gitignore").write_text("deploy/gate-artifacts/\nbin/\ncalls.jsonl\n")
        subprocess.run(["git", "init", "-q", "-b", "main"], cwd=self.root, check=True)
        subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=self.root, check=True)
        subprocess.run(["git", "config", "user.name", "test"], cwd=self.root, check=True)
        subprocess.run(["git", "add", "-A"], cwd=self.root, check=True)
        subprocess.run(["git", "commit", "-q", "-m", "gate fixture"], cwd=self.root, check=True)

        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.calls = self.root / "calls.jsonl"
        stub = self.bin / "docker"
        stub.write_text(DOCKER_STUB)
        stub.chmod(0o755)
        self.artifacts = self.root / "deploy/gate-artifacts"
        self.env = {"PATH": f"{self.bin}:{os.environ['PATH']}", "CALLS": str(self.calls)}

    def run_gate(self, *args, env=None):
        return subprocess.run(
            ["bash", str(self.root / "scripts/release-gate.sh"),
             "--artifacts", str(self.artifacts), *args],
            cwd=self.root, env={**self.env, **(env or {})}, capture_output=True, text=True)

    def docker_calls(self):
        if not self.calls.exists():
            return []
        return [json.loads(line) for line in self.calls.read_text().splitlines()]

    def summary(self):
        return (self.artifacts / "summary.txt").read_text()


class GateOrchestrationTests(GateHarness):
    def test_green_fast_gate_runs_selected_stages(self):
        result = self.run_gate(
            "--only", "shell,infra,backend,postgres,migrations,frontend,deps")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        summary = self.summary()
        for stage in ("shell", "infra", "backend", "postgres", "migrations",
                      "frontend", "deps"):
            self.assertIn(f"{stage} ", summary)
            self.assertIn("OK", summary)
        self.assertNotIn("browser", summary)

    def test_browser_excluded_by_default_and_included_explicitly(self):
        default = self.run_gate()
        self.assertEqual(default.returncode, 0, default.stdout + default.stderr)
        self.assertNotIn("browser", self.summary())

        explicit = self.run_gate("--only", "browser")
        # --only browser включает этап даже без --with-browser.
        self.assertEqual(explicit.returncode, 0, explicit.stdout + explicit.stderr)
        self.assertIn("browser", self.summary())

    def test_unknown_stage_is_rejected_before_work(self):
        result = self.run_gate("--only", "does-not-exist")
        self.assertEqual(result.returncode, 2)
        self.assertIn("неизвестный этап", result.stderr)
        self.assertFalse(self.calls.exists())

    def test_gate_does_not_publish_or_deploy(self):
        result = self.run_gate(
            "--only", "shell,infra,backend,postgres,migrations,frontend,deps")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        for call in self.docker_calls():
            for forbidden in ("push", "up", "login", "deploy", "scp", "ssh"):
                self.assertNotIn(forbidden, call,
                                 f"гейт не должен выполнять «{forbidden}»: {call}")

    def test_only_runs_selected_stage(self):
        result = self.run_gate("--only", "shell")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("shell", self.summary())
        self.assertNotIn("postgres", self.summary())


class GateFailureDiagnosticsTests(GateHarness):
    def test_sql_error_fails_postgres_stage_with_diagnostic(self):
        stub = self.root / "scripts/test-postgres.sh"
        stub.write_text("#!/usr/bin/env bash\n"
                        "echo 'ERROR: relation \"Plans\" does not exist' >&2\n"
                        "exit 1\n")
        stub.chmod(0o755)

        result = self.run_gate("--only", "postgres")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("ПРОВАЛ этапа «postgres»", result.stderr)
        self.assertIn("does not exist", result.stderr)
        log = (self.artifacts / "postgres.log").read_text()
        self.assertIn("does not exist", log)
        self.assertIn("postgres", self.summary())
        self.assertIn("FAIL", self.summary())

    def test_frontend_race_fails_frontend_stage_with_diagnostic(self):
        result = self.run_gate("--only", "frontend", env={
            "DOCKER_FAIL_SUBSTRING": "npm test",
            "DOCKER_FAIL_MESSAGE": "frontend race: поздний ответ перезаписал черновик",
        })
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("ПРОВАЛ этапа «frontend»", result.stderr)
        self.assertIn("frontend race", result.stderr)
        self.assertIn("frontend race", (self.artifacts / "frontend.log").read_text())

    def test_infra_stage_failure_is_reported(self):
        stub = self.root / "scripts/check-infra.sh"
        stub.write_text("#!/usr/bin/env bash\n"
                        "echo 'caddy validate: unexpected token' >&2\n"
                        "exit 1\n")
        stub.chmod(0o755)

        result = self.run_gate("--only", "infra")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("ПРОВАЛ этапа «infra»", result.stderr)
        self.assertIn("unexpected token", result.stderr)

    def test_failure_stops_at_first_bad_stage(self):
        stub = self.root / "scripts/test-postgres.sh"
        stub.write_text("#!/usr/bin/env bash\nexit 1\n")
        stub.chmod(0o755)
        result = self.run_gate("--only", "postgres,frontend")
        self.assertNotEqual(result.returncode, 0)
        # frontend не запускался: нет его журнала.
        self.assertFalse((self.artifacts / "frontend.log").exists())


class InfraCheckerTests(unittest.TestCase):
    """Настоящий scripts/check-infra.sh на неверной конфигурации Compose."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "scripts").mkdir()
        shutil.copy(ROOT / "scripts/check-infra.sh", self.root / "scripts/check-infra.sh")
        (self.root / "scripts/check-infra.sh").chmod(0o755)
        (self.root / "deploy").mkdir()
        (self.root / "deploy/Caddyfile").write_text(":80 {}\n")
        (self.root / "deploy/otel-collector.yaml").write_text("receivers: {}\n")
        (self.root / "docker-compose.prod.yml").write_text("services: {}\n")

    def test_invalid_compose_is_rejected_with_message(self):
        (self.root / "docker-compose.yml").write_text("services:\n  backend:\n    image: [a, b\n")
        result = subprocess.run(["bash", str(self.root / "scripts/check-infra.sh")],
                                cwd=self.root, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("docker-compose.yml не проходит config", result.stderr)

    def test_checker_validates_caddy_and_collector(self):
        text = (ROOT / "scripts/check-infra.sh").read_text()
        self.assertIn("caddy validate", text)
        self.assertIn("validate --config", text)
        self.assertIn("otel-collector", text)


class GateStructureTests(unittest.TestCase):
    def test_shell_stage_uses_shellcheck_not_only_bash_n(self):
        text = (ROOT / "scripts/release-gate.sh").read_text()
        self.assertIn("shellcheck", text)
        self.assertIn("bash -n", text)

    def test_browser_smoke_is_bounded_and_cleans_up(self):
        text = (ROOT / "scripts/browser-smoke.sh").read_text()
        self.assertIn("trap cleanup EXIT", text)
        self.assertIn("down -v", text)
        self.assertIn("READY_TIMEOUT", text)
        self.assertIn("class=\"code\">[0-9]{6}", text)
        self.assertIn("PLAYWRIGHT_IMAGE", text)

        flow = (ROOT / "scripts/browser-smoke/flow.mjs").read_text()
        self.assertNotIn("waitForTimeout", flow)
        self.assertIn("waitFor", flow)

    def test_dependency_analysis_reports_findings_and_policy(self):
        text = (ROOT / "scripts/check-dependencies.sh").read_text()
        self.assertIn("--vulnerable", text)
        self.assertIn("npm audit", text)
        self.assertIn("GATE_AUDIT_ENFORCE", text)
        readme = (ROOT / "deploy/README.md").read_text()
        self.assertIn("Политика обработки findings", readme)

    def test_ci_workflow_matches_github_hosting(self):
        workflow = (ROOT / ".github/workflows/release-gate.yml").read_text()
        self.assertIn("on:", workflow)
        self.assertIn("scripts/release-gate.sh", workflow)
        self.assertIn("needs:", workflow)
        self.assertIn("secrets.", workflow)


if __name__ == "__main__":
    unittest.main()
