"""Изолированные проверки smoke публичного пути через край (Caddy).

Транспорт подменён: `docker` — исполняемый adapter в PATH, который эмулирует
`image inspect`, `compose config --images`, `compose exec caddy wget` и
`compose ps/logs`. Край (Caddy + frontend + backend) эмулируется каталогом
файлов `EDGE_DIR`: путь URL отображается в файл, `.status` задаёт код ответа.
Реальный Docker daemon, сеть, TLS и БД не запускаются; секреты вымышленные.
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
import json, os, sys, pathlib

args = sys.argv[1:]
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps({"command": "docker", "args": args}) + "\n")

REASONS = {200: "OK", 301: "Moved Permanently", 401: "Unauthorized", 404: "Not Found",
           500: "Internal Server Error", 502: "Bad Gateway", 503: "Service Unavailable"}


def emit(status, body):
    sys.stderr.write("  HTTP/1.1 %d %s\n" % (status, REASONS.get(status, "Status")))
    if body:
        sys.stdout.write(body)
    return 0 if status < 400 else 1


if args[:2] == ["image", "inspect"]:
    digests = json.loads(os.environ["DIGESTS"])
    sys.stdout.write(digests.get(args[-1], "unknown") + "\n")
    sys.exit(0)

i = args.index("compose") + 1
while i < len(args) and args[i].startswith("-"):
    if args[i] in ("--env-file", "-f", "--profile"):
        i += 2
    else:
        i += 1
sub = args[i:]

if sub[:1] == ["config"]:
    if "--images" in sub:
        sys.stdout.write("\n".join(json.loads(os.environ["IMAGES"])) + "\n")
    sys.exit(0)

if sub[:1] == ["ps"] or sub[:1] == ["logs"]:
    sys.stdout.write("stub %s output\n" % sub[0])
    sys.exit(0)

if sub[:1] == ["exec"]:
    if "wget" not in sub:
        sys.exit(0)
    wargs = sub[sub.index("wget") + 1:]
    url = next((a for a in wargs if a.startswith("http")), None)
    if url is None:
        sys.exit(0)
    discard = "-O" in wargs and wargs[wargs.index("-O") + 1] == "/dev/null"
    rest = url.split("://", 1)[1]
    path = "/" + rest.split("/", 1)[1] if "/" in rest else "/"
    path = path.split("?", 1)[0]
    edge = pathlib.Path(os.environ["EDGE_DIR"])
    key = "index.html" if path == "/" else path.lstrip("/")
    body_file = edge / key
    status_file = edge / (key + ".status")
    if status_file.exists():
        status = int(status_file.read_text().strip())
    else:
        status = 200 if body_file.exists() else 404
    body = body_file.read_text() if body_file.exists() else ""
    sys.exit(emit(status, None if discard else body))

sys.exit(0)
'''


class SmokeHarness(unittest.TestCase):
    tag = "abc123"

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "deploy").mkdir()
        for filename in ("config.sh", "release.sh", "backup-lib.sh", "deploy-lib.sh",
                         "smoke.sh", "remote-deploy.sh"):
            shutil.copy(ROOT / "deploy" / filename, self.root / "deploy" / filename)
        shutil.copy(ROOT / "docker-compose.prod.yml", self.root)

        self.backend_digest = "sha256:" + "a" * 64
        self.frontend_digest = "sha256:" + "b" * 64
        (self.root / "release.json").write_text(json.dumps({
            "commit": "deadbeef", "tag": self.tag, "builtAt": "2026-01-01T00:00:00Z",
            "backendDigest": self.backend_digest, "frontendDigest": self.frontend_digest,
            "config": {}}, indent=2) + "\n")
        (self.root / "server.conf").write_text(
            "DOCKERHUB_USER=example\nPOSTGRES_PASSWORD=smoke-secret-value\n"
            "JWT_SECRET=jwt-smoke-secret-value\nIMAGE_TAG=" + self.tag + "\n")

        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.calls = self.root / "calls.jsonl"
        stub = self.bin / "docker"
        stub.write_text(DOCKER_STUB)
        stub.chmod(0o755)

        self.edge = self.root / "edge"
        self.edge.mkdir()

        self.env = {
            "PATH": f"{self.bin}:{os.environ['PATH']}",
            "CALLS": str(self.calls),
            # Лабораторный HTTP без домена — только явный DEPLOYMENT_MODE=lab.
            "DEPLOYMENT_MODE": "lab",
            "DIGESTS": json.dumps({
                f"example/menu-backend:{self.tag}": self.backend_digest,
                f"example/menu-frontend:{self.tag}": self.frontend_digest}),
            "IMAGES": json.dumps([f"example/menu-backend:{self.tag}",
                                  f"example/menu-frontend:{self.tag}"]),
            "EDGE_DIR": str(self.edge),
        }

    def set_edge(self, key, body=None, status=None):
        path = self.edge / key
        path.parent.mkdir(parents=True, exist_ok=True)
        if body is not None:
            path.write_text(body)
        if status is not None:
            (self.edge / (key + ".status")).write_text(str(status))

    def healthy_edge(self):
        index = ('<!doctype html><html><head>'
                 '<script type="module" crossorigin src="/assets/index-abc123.js"></script>'
                 '</head><body></body></html>')
        self.set_edge("index.html", index)
        self.set_edge("assets/index-abc123.js", "console.log('ok')\n")
        self.set_edge("ready", '{"status":"ready","service":"menu-planner-api"}\n')
        self.set_edge("api/recipes", '{"error":"unauthorized"}\n', status=401)

    def run_script(self, script, env=None, args=()):
        return subprocess.run(["bash", str(self.root / "deploy" / script), *args],
                              cwd=self.root, env={**self.env, **(env or {})},
                              capture_output=True, text=True)

    def wget_urls(self):
        urls = []
        for call in self.recorded_calls():
            args = call["args"]
            if "wget" not in args:
                continue
            urls.append(next(a for a in args if a.startswith("http")))
        return urls

    def recorded_calls(self):
        if not self.calls.exists():
            return []
        return [json.loads(line) for line in self.calls.read_text().splitlines()]

    def docker_subcommands(self):
        commands = []
        for call in self.recorded_calls():
            args = call["args"]
            if "compose" in args:
                index = args.index("compose") + 1
                while index < len(args) and args[index].startswith("-"):
                    index += 2 if args[index] in ("--env-file", "-f", "--profile") else 1
                if index < len(args):
                    commands.append(args[index])
            elif args[:2] == ["image", "inspect"]:
                commands.append("image")
        return commands


class SmokeEdgeTests(SmokeHarness):
    def test_healthy_release_passes_through_edge(self):
        self.healthy_edge()
        result = self.run_script("smoke.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Smoke успешен", result.stdout)

        urls = self.wget_urls()
        self.assertTrue(urls)
        self.assertTrue(all(url.startswith("http://localhost/") or url == "http://localhost/"
                            for url in urls), urls)
        self.assertFalse(any("backend:8080" in url for url in urls), urls)
        self.assertTrue(any(url.endswith("/ready") for url in urls))
        self.assertTrue(any(url.endswith("/api/recipes") for url in urls))
        self.assertTrue(any("/assets/index-abc123.js" in url for url in urls))

        output = result.stdout + result.stderr
        self.assertNotIn("smoke-secret-value", output)
        self.assertNotIn("jwt-smoke-secret-value", output)

    def test_domain_mode_uses_external_https(self):
        self.healthy_edge()
        result = self.run_script("smoke.sh", {"DOMAIN": "menu.example.com"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        urls = self.wget_urls()
        self.assertTrue(urls)
        self.assertTrue(all(url.startswith("https://menu.example.com/") for url in urls), urls)

    def test_unavailable_database_fails_and_collects_diagnostics(self):
        self.healthy_edge()
        self.set_edge("ready", '{"status":"not-ready"}\n', status=503)
        result = self.run_script("smoke.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("readiness", result.stderr)
        self.assertNotIn("Smoke успешен", result.stdout)
        self.assertTrue((self.root / "smoke-diagnostics.log").exists())
        self.assertIn("ps", self.docker_subcommands())
        self.assertIn("logs", self.docker_subcommands())
        self.assertNotIn("smoke-secret-value", result.stdout + result.stderr)

    def test_missing_frontend_fails(self):
        self.set_edge("ready", '{"status":"ready"}\n')
        self.set_edge("api/recipes", "", status=401)
        result = self.run_script("smoke.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("SPA", result.stderr)

    def test_missing_asset_fails(self):
        index = '<!doctype html><script type="module" src="/assets/index-missing.js"></script>'
        self.set_edge("index.html", index)
        self.set_edge("ready", '{"status":"ready"}\n')
        self.set_edge("api/recipes", "", status=401)
        result = self.run_script("smoke.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("index-missing.js", result.stderr)

    def test_broken_api_route_is_not_success(self):
        self.healthy_edge()
        # Сломанный маршрут /api: запрос падает в SPA-fallback (200 HTML).
        self.set_edge("api/recipes", (self.edge / "index.html").read_text(), status=200)
        result = self.run_script("smoke.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Маршрут /api", result.stderr)

    def test_broken_ready_route_is_not_success(self):
        self.healthy_edge()
        # Маршрут /ready отсутствует в Caddy и отдаёт SPA (200 HTML).
        self.set_edge("ready", (self.edge / "index.html").read_text(), status=200)
        result = self.run_script("smoke.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("readiness", result.stderr)

    def test_http_timeout_is_bounded(self):
        self.healthy_edge()
        result = self.run_script("smoke.sh", {"SMOKE_HTTP_TIMEOUT_SECONDS": "7"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        for call in self.recorded_calls():
            args = call["args"]
            if "wget" in args:
                wargs = args[args.index("wget") + 1:]
                self.assertIn("-T", wargs)
                self.assertEqual(wargs[wargs.index("-T") + 1], "7")

    def test_invalid_timeout_is_rejected_before_edge(self):
        self.healthy_edge()
        result = self.run_script("smoke.sh", {"SMOKE_HTTP_TIMEOUT_SECONDS": "abc"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("SMOKE_HTTP_TIMEOUT_SECONDS", result.stderr)
        self.assertEqual(self.wget_urls(), [])


class SmokeProductionModeTests(SmokeHarness):
    def test_http_without_explicit_lab_mode_is_rejected(self):
        self.healthy_edge()
        result = self.run_script("smoke.sh", {"DEPLOYMENT_MODE": "production"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("lab", result.stderr)
        self.assertEqual(self.wget_urls(), [], "HTTP-запрос ушёл до отказа")

    def test_domain_mode_is_allowed_in_production(self):
        self.healthy_edge()
        result = self.run_script(
            "smoke.sh", {"DEPLOYMENT_MODE": "production", "DOMAIN": "menu.example.com"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(all(url.startswith("https://menu.example.com/")
                            for url in self.wget_urls()))


class RemoteDeployTests(SmokeHarness):
    def run_deploy(self, env=None):
        return self.run_script("remote-deploy.sh", env, ("abc123", "deadbeef"))

    def test_readiness_gates_release_state(self):
        self.healthy_edge()
        result = self.run_deploy({"READY_TIMEOUT_SECONDS": "5"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("ready ok", result.stdout)
        manifest = json.loads((self.root / "release.json").read_text())
        self.assertEqual(manifest["tag"], "abc123")
        self.assertIn("IMAGE_TAG=abc123", (self.root / "current-release").read_text())
        self.assertTrue(any(url.endswith("/ready") for url in self.wget_urls()))

    def test_unready_database_fails_bounded_with_diagnostics(self):
        self.healthy_edge()
        self.set_edge("ready", '{"status":"not-ready"}\n', status=503)
        result = self.run_deploy({"READY_TIMEOUT_SECONDS": "1"})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("readiness", result.stderr)
        self.assertFalse((self.root / "current-release").exists())
        self.assertTrue((self.root / "deploy-diagnostics.log").exists())
        self.assertIn("ps", self.docker_subcommands())
        self.assertIn("logs", self.docker_subcommands())


class ContainerHealthcheckTests(unittest.TestCase):
    def test_backend_healthcheck_checks_http_not_just_open_port(self):
        compose = (ROOT / "docker-compose.prod.yml").read_text()
        self.assertIn("http://127.0.0.1:8080/health", compose)
        self.assertNotIn("/dev/tcp/127.0.0.1/8080", compose)
        self.assertIn("restart: unless-stopped", compose)
        # HTTP-клиент для healthcheck поставляется в runtime-образ backend.
        self.assertIn("curl", (ROOT / "backend/Dockerfile").read_text())


class FrontendResilienceTests(unittest.TestCase):
    def test_transient_failure_does_not_discard_session(self):
        source = (ROOT / "frontend/src/api/client.js").read_text()
        # Сессия очищается только по явному 401 (истёк/невалиден токен),
        # но не по сетевому сбою или временному 5xx: повторный запрос не требует
        # ручного удаления данных или сессии.
        self.assertIn("response.status === 401", source)
        self.assertEqual(source.count("clearSession()"), 1)
        self.assertEqual(source.count("localStorage"), 0)


if __name__ == "__main__":
    unittest.main()
