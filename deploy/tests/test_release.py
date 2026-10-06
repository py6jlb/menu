"""Проверки доверенной библиотеки воспроизводимого релиза."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
LIBRARY = ROOT / "deploy/release.sh"


def run_bash(script, cwd=None):
    return subprocess.run(
        ["bash", "-eu", "-c", 'source "$1"; ' + script, "test", str(LIBRARY)],
        cwd=cwd, env={"PATH": os.environ["PATH"]}, capture_output=True, text=True)


class ReleaseLibraryTests(unittest.TestCase):
    def test_clean_tree_is_required(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            subprocess.run(["git", "init", "-q", "-b", "main"], cwd=repo, check=True)
            subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=repo, check=True)
            subprocess.run(["git", "config", "user.name", "test"], cwd=repo, check=True)
            (repo / "tracked.txt").write_text("one\n")
            subprocess.run(["git", "add", "tracked.txt"], cwd=repo, check=True)
            subprocess.run(["git", "commit", "-q", "-m", "one"], cwd=repo, check=True)

            ok = run_bash(f'release_require_clean_tree "{repo}"', cwd=repo)
            self.assertEqual(ok.returncode, 0, ok.stderr)

            (repo / "tracked.txt").write_text("changed\n")
            dirty = run_bash(f'release_require_clean_tree "{repo}"', cwd=repo)
            self.assertNotEqual(dirty.returncode, 0)
            self.assertIn("Релиз:", dirty.stderr)

            subprocess.run(["git", "checkout", "--", "tracked.txt"], cwd=repo, check=True)
            (repo / "untracked.txt").write_text("new\n")
            untracked = run_bash(f'release_require_clean_tree "{repo}"', cwd=repo)
            self.assertNotEqual(untracked.returncode, 0)

    def test_resolve_commit_rejects_unknown_revision(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            subprocess.run(["git", "init", "-q", "-b", "main"], cwd=repo, check=True)
            subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=repo, check=True)
            subprocess.run(["git", "config", "user.name", "test"], cwd=repo, check=True)
            (repo / "a.txt").write_text("a\n")
            subprocess.run(["git", "add", "a.txt"], cwd=repo, check=True)
            subprocess.run(["git", "commit", "-q", "-m", "a"], cwd=repo, check=True)
            head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=repo,
                                  capture_output=True, text=True, check=True).stdout.strip()

            resolved = run_bash(f'release_resolve_commit "{repo}" HEAD', cwd=repo)
            self.assertEqual(resolved.returncode, 0, resolved.stderr)
            self.assertEqual(resolved.stdout.strip(), head)

            missing = run_bash(f'release_resolve_commit "{repo}" does-not-exist', cwd=repo)
            self.assertNotEqual(missing.returncode, 0)
            self.assertIn("не найден коммит", missing.stderr)

    def test_manifest_roundtrip_and_config_verification(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "deploy").mkdir()
            (root / "docker-compose.prod.yml").write_text("services: {}\n")
            (root / "deploy/Caddyfile").write_text(":80 {}\n")
            hashes = root / "hashes.txt"
            manifest = root / "release.json"

            rendered = run_bash(
                f'release_config_hashes "{root}" > "{hashes}"; '
                f'release_manifest "{manifest}" deadbeef abc123 2026-01-02T03:04:05Z '
                f'sha256:aaa sha256:bbb "{hashes}"', cwd=root)
            self.assertEqual(rendered.returncode, 0, rendered.stderr)

            body = manifest.read_text()
            self.assertIn('"commit": "deadbeef"', body)
            self.assertIn('"tag": "abc123"', body)
            self.assertIn('"backendDigest": "sha256:aaa"', body)
            self.assertIn("docker-compose.prod.yml", body)
            self.assertIn("deploy/Caddyfile", body)

            verified = run_bash(f'release_verify_config "{manifest}" "{hashes}"', cwd=root)
            self.assertEqual(verified.returncode, 0, verified.stderr)

            (root / "deploy/Caddyfile").write_text(":80 { server_name x; }\n")
            run_bash(f'release_config_hashes "{root}" > "{hashes}"', cwd=root)
            mismatch = run_bash(f'release_verify_config "{manifest}" "{hashes}"', cwd=root)
            self.assertNotEqual(mismatch.returncode, 0)
            self.assertIn("не соответствует manifest", mismatch.stderr)

    def test_manifest_config_hash_reads_value(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "hashes.txt").write_text("deploy/Caddyfile aabbcc\n")
            manifest = root / "release.json"
            run_bash(
                f'release_manifest "{manifest}" c t 2026-01-01T00:00:00Z sha256:b sha256:f '
                f'"{root}/hashes.txt"', cwd=root)
            read = run_bash(
                f'printf "%s" "$(release_manifest_field "{manifest}" deploy/Caddyfile)"',
                cwd=root)
            self.assertEqual(read.stdout, "aabbcc")


class ReleaseStructureTests(unittest.TestCase):
    def test_base_images_are_pinned_and_node_is_lts(self):
        dockerfile = (ROOT / "frontend/Dockerfile").read_text()
        self.assertIn("FROM node:24-alpine", dockerfile)
        self.assertIn("npm ci", dockerfile)
        self.assertNotIn("npm install", dockerfile)

        backend = (ROOT / "backend/Dockerfile").read_text()
        self.assertIn("mcr.microsoft.com/dotnet/sdk:10.0", backend)
        self.assertIn("mcr.microsoft.com/dotnet/aspnet:10.0", backend)

        for name in ("docker-compose.yml", "docker-compose.prod.yml"):
            text = (ROOT / name).read_text()
            self.assertNotIn(":latest", text, f"{name}: образ на latest")
            self.assertIn("postgres:16", text)

        self.assertEqual((ROOT / "frontend/.nvmrc").read_text().strip(), "24")
        self.assertIn('"node": ">=24 <25"', (ROOT / "frontend/package.json").read_text())
        self.assertIn("RELEASE_NODE_IMAGE", (ROOT / "deploy/release.sh").read_text())
        self.assertIn("NODE_IMAGE", (ROOT / "scripts/release-gate.sh").read_text())

    def test_spa_missing_asset_returns_404(self):
        nginx = (ROOT / "frontend/nginx.prod.conf").read_text()
        self.assertIn("location ^~ /assets/", nginx)
        self.assertIn("try_files $uri =404;", nginx)
        self.assertIn("try_files $uri $uri/ /index.html;", nginx)

    def test_used_base_images_are_in_pinned_list(self):
        declared = run_bash("release_base_images").stdout.split()
        self.assertIn("node:24-alpine", declared)
        self.assertIn("nginx:1.27-alpine", declared)

        used = set()
        for name in ("backend/Dockerfile", "frontend/Dockerfile"):
            for line in (ROOT / name).read_text().splitlines():
                if line.startswith("FROM "):
                    used.add(line.split()[1])
        for name in ("docker-compose.yml", "docker-compose.prod.yml"):
            for line in (ROOT / name).read_text().splitlines():
                stripped = line.strip()
                if stripped.startswith("image:") and "${" not in stripped:
                    used.add(stripped.split(":", 1)[1].strip().strip('"'))

        for image in sorted(used):
            self.assertIn(image, declared, f"{image} не закреплён в release_base_images")

    def test_smoke_and_publish_gate_structure(self):
        smoke = (ROOT / "deploy/smoke.sh").read_text()
        self.assertIn("release_manifest_field", smoke)
        self.assertIn("config --images", smoke)

        build = (ROOT / "deploy/build-push.sh").read_text()
        self.assertNotIn("PUBLISH_ENV", build)
        self.assertIn("--publish", build)


class ReleaseBuildTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "deploy").mkdir()
        for filename in ("config.sh", "release.sh", "build-push.sh"):
            shutil.copy(ROOT / "deploy" / filename, self.root / "deploy" / filename)
        (self.root / "scripts").mkdir()
        shutil.copy(ROOT / "scripts/release-gate.sh", self.root / "scripts/release-gate.sh")
        (self.root / "scripts/release-gate.sh").chmod(0o755)
        for filename in ("test-postgres.sh", "check-infra.sh", "check-dependencies.sh",
                         "browser-smoke.sh"):
            stub = self.root / "scripts" / filename
            stub.write_text("#!/usr/bin/env bash\nexit 0\n")
            stub.chmod(0o755)
        (self.root / "deploy/tests").mkdir()
        (self.root / "deploy/tests/test_placeholder.py").write_text(
            "import unittest\n\n"
            "class Placeholder(unittest.TestCase):\n"
            "    def test_ok(self):\n"
            "        self.assertTrue(True)\n")
        (self.root / "docker-compose.prod.yml").write_text("services: {}\n")
        (self.root / "deploy/Caddyfile").write_text(":80 {}\n")
        (self.root / "frontend").mkdir()
        (self.root / "frontend/package-lock.json").write_text("{}\n")
        (self.root / ".gitignore").write_text("deploy/release/\ndeploy/gate-artifacts/\nbin/\ncalls.jsonl\n")
        subprocess.run(["git", "init", "-q", "-b", "main"], cwd=self.root, check=True)
        subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=self.root, check=True)
        subprocess.run(["git", "config", "user.name", "test"], cwd=self.root, check=True)
        subprocess.run(["git", "add", "-A"], cwd=self.root, check=True)
        subprocess.run(["git", "commit", "-q", "-m", "release"], cwd=self.root, check=True)

        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.calls_path = self.root / "calls.jsonl"
        stub = self.bin / "docker"
        stub.write_text(
            "#!/usr/bin/env python3\nimport json, os, sys\n"
            "a = sys.argv[1:]\n"
            "open(os.environ['CALLS'], 'a').write(json.dumps(a) + '\\n')\n"
            "if a[:2] == ['image', 'inspect']:\n"
            "    name = a[-1]\n"
            "    print(('example/menu-backend' if 'backend' in name else 'example/menu-frontend')"
            " + '@sha256:' + 'a' * 64)\n"
            "sys.exit(0)\n")
        stub.chmod(0o755)
        self.env = {"PATH": f"{self.bin}:{os.environ['PATH']}", "CALLS": str(self.calls_path),
                    "DOCKERHUB_USER": "example", "IMAGE_TAG": "abc123"}

    def calls(self):
        path = self.root / "calls.jsonl"
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def test_dirty_checkout_is_rejected(self):
        (self.root / "docker-compose.prod.yml").write_text("services: { changed: true }\n")
        result = subprocess.run(["bash", str(self.root / "deploy/build-push.sh")],
                                cwd=self.root, env=self.env, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Релиз:", result.stderr)
        self.assertEqual(self.calls(), [])

    def test_build_does_not_publish_until_explicit(self):
        result = subprocess.run(["bash", str(self.root / "deploy/build-push.sh")],
                                cwd=self.root, env=self.env, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(any("push" in call for call in self.calls()))
        manifest = self.root / "deploy/release/abc123.json"
        self.assertTrue(manifest.exists())
        body = json.loads(manifest.read_text())
        self.assertEqual(body["tag"], "abc123")
        self.assertTrue(body["backendDigest"])

    def test_publish_pushes_and_records_registry_digests(self):
        result = subprocess.run(["bash", str(self.root / "deploy/build-push.sh"), "--publish"],
                                cwd=self.root, env=self.env, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        pushed = [call for call in self.calls() if call and call[0] == "push"]
        self.assertTrue(any("menu-backend:abc123" in " ".join(call) for call in pushed))
        self.assertTrue(any("menu-frontend:latest" in " ".join(call) for call in pushed))
        body = json.loads((self.root / "deploy/release/abc123.json").read_text())
        self.assertIn("@sha256:", body["backendDigest"])


class CurrentReleaseTests(unittest.TestCase):
    def run_config_server(self, root, env=None):
        script = ('source "$1"; APP_DIR="$2"; config_server; printenv IMAGE_TAG',
                  str(ROOT / "deploy/config.sh"), str(root))
        return subprocess.run(
            ["bash", "-eu", "-c", script[0], "test", script[1], script[2]],
            env={"PATH": os.environ["PATH"], **(env or {})},
            capture_output=True, text=True)

    def test_saved_release_supplies_pinned_tag_with_priority(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "server.conf").write_text(
                "DOCKERHUB_USER=example\nPOSTGRES_PASSWORD=secret\nJWT_SECRET=secret\n")
            (root / "current-release").write_text("IMAGE_TAG=pinned999\n")

            saved = self.run_config_server(root)
            self.assertEqual((saved.returncode, saved.stdout.strip()), (0, "pinned999"))

            env_wins = self.run_config_server(root, {"IMAGE_TAG": "envtag"})
            self.assertEqual((env_wins.returncode, env_wins.stdout.strip()), (0, "envtag"))

            (root / "server.conf").write_text(
                "DOCKERHUB_USER=example\nPOSTGRES_PASSWORD=secret\nJWT_SECRET=secret\n"
                "IMAGE_TAG=filetag\n")
            file_wins = self.run_config_server(root)
            self.assertEqual((file_wins.returncode, file_wins.stdout.strip()), (0, "filetag"))


if __name__ == "__main__":
    unittest.main()
