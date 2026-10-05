"""Изолированные проверки публичного чтения конфигурации и entrypoints."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
LIBRARY = ROOT / "deploy/config.sh"


class RepositoryTests(unittest.TestCase):
    def test_python_bytecache_is_ignored(self):
        paths = ["deploy/tests/__pycache__/test_config.cpython-313.pyc",
                 "deploy/tests/test_config.pyc", "deploy/tests/test_config.pyo"]
        result = subprocess.run(["git", "check-ignore", "--no-index", "--stdin"],
                                cwd=ROOT, input="\n".join(paths) + "\n",
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.splitlines(), paths)


class ConfigTests(unittest.TestCase):
    def test_template_and_literal_specials(self):
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory) / "server.conf"
            marker = Path(directory) / "executed"
            literal = f'Меню для домохозяек $HOME # "двойные" \'одинарные\' \\ $(touch {marker}) `touch {marker}`'
            config.write_text("SMTP_FROM_NAME=" + literal + "\n", encoding="utf-8")
            script = 'source "$1"; config_load "$2" server'
            result = subprocess.run(
                ["bash", "-eu", "-c", script + '; printf "%s" "$SMTP_FROM_NAME"',
                 "test", str(LIBRARY), str(config)],
                env={"PATH": os.environ["PATH"]}, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(result.stdout, literal)
            self.assertFalse(marker.exists(), "Конфигурация исполнила команду")

    def read_config(self, path, body, env=None, scope="server"):
        return subprocess.run(
            ["bash", "-eu", "-c", 'source "$1"; config_load "$2" "$3"; ' + body,
             "test", str(LIBRARY), str(path), scope],
            env={"PATH": os.environ["PATH"], **(env or {})},
            capture_output=True, text=True)

    def test_optional_file_environment_precedence_and_export(self):
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory) / "server.conf"
            result = self.read_config(config, 'printenv SMTP_FROM_NAME',
                                      {"SMTP_FROM_NAME": "Только окружение"})
            self.assertEqual((result.returncode, result.stdout), (0, "Только окружение\n"))
            config.write_text("SMTP_FROM_NAME=Из файла\nSMTP_PASSWORD=file-secret\n")
            result = self.read_config(config, 'printenv SMTP_FROM_NAME SMTP_PASSWORD',
                                      {"SMTP_FROM_NAME": "Из окружения", "SMTP_PASSWORD": ""})
            self.assertEqual((result.returncode, result.stdout), (0, "Из окружения\n\n"))
            result = self.read_config(config, 'printenv SMTP_FROM_NAME SMTP_PASSWORD')
            self.assertEqual((result.returncode, result.stdout), (0, "Из файла\nfile-secret\n"))

    def test_malformed_config_is_nonzero_without_secrets(self):
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory) / "server.conf"
            for content in (b"SMTP_PASSWORD=secret\x00hidden\n", b"SMTP_PASSWORD=secret\r\n",
                            b"SMTP_PASSWORD=secret\tvalue\n", b"SMTP_PASSWORD=secret\nSMTP_PASSWORD=other\n",
                            b"secret-without-equals\n", b"PATH=secret\n", b"BASH_ENV=secret\n",
                            b"SHELLOPTS=secret\n", b"UNKNOWN=secret\n", b"VPS_HOST=secret\n",
                            b"COMPOSE_ENV_FILES=secret\n", b"export SMTP_PASSWORD=secret\n",
                            b"SMTP_PASSWORD[secret]=payload\n"):
                with self.subTest(content=content):
                    config.write_bytes(content)
                    result = self.read_config(config, ':')
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn("Конфигурация:", result.stderr)
                    self.assertNotIn("secret", result.stdout + result.stderr)

    def test_shipped_templates_and_format_edges(self):
        result = self.read_config(ROOT / "deploy/server.conf.example", 'printenv SMTP_FROM_NAME')
        self.assertEqual((result.returncode, result.stdout), (0, "Меню для домохозяек\n"))
        result = self.read_config(ROOT / "deploy/local.conf.example", 'printenv DOCKERHUB_USER', scope="local")
        self.assertEqual((result.returncode, result.stdout), (0, "your-dockerhub-login\n"))
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory) / "server.conf"
            config.write_text('# Комментарий\n\nSMTP_FROM_NAME=  "буквальные кавычки" # не комментарий  ')
            result = self.read_config(config, 'printenv SMTP_FROM_NAME')
            self.assertEqual((result.returncode, result.stdout),
                             (0, '  "буквальные кавычки" # не комментарий  \n'))
            config.write_text("SMTP_PASSWORD=secret\n")
            result = self.read_config(config, ':', scope="local")
            self.assertNotEqual(result.returncode, 0)
            self.assertNotIn("secret", result.stderr)


class EntrypointTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        # Только публичные артефакты: никогда не копируем реальный local.conf/.env.
        for filename in ("config.sh", "compose.sh", "remote-deploy.sh", "build-push.sh",
                         "deploy.sh", "backup.sh", "restore-drill.sh", "Caddyfile",
                         "otel-collector.yaml", "systemd/menu-backup.service", "systemd/menu-backup.timer"):
            destination = self.root / "deploy" / filename
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(ROOT / "deploy" / filename, destination)
        shutil.copy(ROOT / "docker-compose.prod.yml", self.root)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.calls = self.root / "calls.jsonl"
        for command in ("docker", "scp", "ssh", "rclone", "npm"):
            stub = self.bin / command
            stub.write_text(
                '#!/usr/bin/python3\nimport json, os, sys\n'
                'with open(os.environ["CALLS"], "a") as stream:\n'
                ' stream.write(json.dumps({"command": os.path.basename(sys.argv[0]), '
                '"args": sys.argv[1:], "env": dict(os.environ)}) + "\\n")\n'
                'sys.exit(91)\n')
            stub.chmod(0o755)
        self.env = {"PATH": f'{self.bin}:{os.environ["PATH"]}', "CALLS": str(self.calls)}

    def run_script(self, script, env=None, args=()):
        return subprocess.run(["bash", str(self.root / "deploy" / script), *args],
                              cwd=self.root, env={**self.env, **(env or {})},
                              capture_output=True, text=True)

    def test_missing_required_values_before_external_actions(self):
        server = {"DOCKERHUB_USER": "example", "POSTGRES_PASSWORD": "test-secret",
                  "JWT_SECRET": "test-jwt", "BACKUP_REMOTE": "test:bucket"}
        cases = [("build-push.sh", {"DOCKERHUB_USER": "example"}, "DOCKERHUB_USER", ()),
                 ("deploy.sh", {"VPS_HOST": "example.test"}, "VPS_HOST", ("abc123",)),
                 ("restore-drill.sh", {"BACKUP_REMOTE": "test:bucket"}, "BACKUP_REMOTE", ("",))]
        for script in ("remote-deploy.sh", "compose.sh", "backup.sh"):
            keys = ("DOCKERHUB_USER", "POSTGRES_PASSWORD", "JWT_SECRET")
            if script == "backup.sh":
                keys += ("BACKUP_REMOTE",)
            cases.extend((script, server, key, ("abc123",) if script == "remote-deploy.sh" else ())
                         for key in keys)
        for script, values, missing, args in cases:
            for empty in (False, True):
                with self.subTest(script=script, missing=missing, empty=empty):
                    self.calls.unlink(missing_ok=True)
                    env = dict(values)
                    env.pop(missing)
                    if empty:
                        env[missing] = ""
                    result = self.run_script(script, env, args)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn(missing, result.stderr)
                    self.assertNotIn("test-secret", result.stdout + result.stderr)
                    self.assertFalse(self.calls.exists(), "Внешнее действие до проверки конфигурации")

    def test_env_only_entrypoints_reach_external_boundary(self):
        cases = [("build-push.sh", {"DOCKERHUB_USER": "example", "IMAGE_TAG": "abc123"}, ()),
                 ("deploy.sh", {"VPS_HOST": "example.test"}, ("abc123",)),
                 ("remote-deploy.sh", {"DOCKERHUB_USER": "example", "POSTGRES_PASSWORD": "literal$#",
                                        "JWT_SECRET": "jwt-test"}, ("abc123",)),
                 ("compose.sh", {"DOCKERHUB_USER": "example", "POSTGRES_PASSWORD": "literal$#",
                                  "JWT_SECRET": "jwt-test"}, ("config",)),
                 ("backup.sh", {"DOCKERHUB_USER": "example", "POSTGRES_PASSWORD": "literal$#",
                                 "JWT_SECRET": "jwt-test", "BACKUP_REMOTE": "test:bucket"}, ()),
                 ("restore-drill.sh", {"BACKUP_REMOTE": "test:bucket"}, ("",))]
        for script, env, args in cases:
            with self.subTest(script=script):
                self.calls.unlink(missing_ok=True)
                result = self.run_script(script, env, args)
                self.assertEqual(result.returncode, 1 if script == "restore-drill.sh" else 91,
                                 result.stdout + result.stderr)
                self.assertTrue(self.calls.exists())

    def test_file_config_reaches_each_entrypoint_and_empty_env_wins(self):
        literal = '  секрет $HOME # "quote" \'quote\' \\ $(touch should-not-exist)  '
        (self.root / "deploy/local.conf").write_text(
            "DOCKERHUB_USER=example\nIMAGE_TAG=abc123\nVPS_HOST=example.test\n")
        (self.root / "server.conf").write_text(
            f"DOCKERHUB_USER=example\nPOSTGRES_PASSWORD={literal}\nJWT_SECRET={literal}\n"
            f"SMTP_FROM_NAME={literal}\nBACKUP_REMOTE=test:bucket\n")
        cases = [("build-push.sh", (), "DOCKERHUB_USER"),
                 ("deploy.sh", ("abc123",), "VPS_HOST"),
                 ("remote-deploy.sh", ("abc123",), "JWT_SECRET"),
                 ("compose.sh", ("config",), "POSTGRES_PASSWORD"),
                 ("backup.sh", (), "BACKUP_REMOTE"),
                 ("restore-drill.sh", ("",), "BACKUP_REMOTE")]
        for script, args, required in cases:
            with self.subTest(script=script):
                self.calls.unlink(missing_ok=True)
                result = self.run_script(script, args=args)
                self.assertEqual(result.returncode, 1 if script == "restore-drill.sh" else 91,
                                 result.stdout + result.stderr)
                call = json.loads(self.calls.read_text().splitlines()[0])
                if script not in ("build-push.sh", "deploy.sh"):
                    for key in ("POSTGRES_PASSWORD", "JWT_SECRET", "SMTP_FROM_NAME"):
                        self.assertEqual(call["env"][key], literal)
                self.assertFalse((self.root / "should-not-exist").exists())
                self.assertNotIn(literal, result.stdout + result.stderr)
                self.calls.unlink()
                result = self.run_script(script, {required: ""}, args)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(required, result.stderr)
                self.assertFalse(self.calls.exists())

    def test_ssh_control_payload_is_rejected_before_transport(self):
        marker = self.root / "executed"
        for key in ("VPS_HOST", "VPS_USER", "VPS_SSH_PORT", "APP_DIR"):
            with self.subTest(key=key):
                (self.root / "deploy/local.conf").write_text(f"{key}=$(touch {marker})\n")
                env = {} if key == "VPS_HOST" else {"VPS_HOST": "example.test"}
                result = self.run_script("deploy.sh", env, ("abc123",))
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("Конфигурация:", result.stderr)
                self.assertFalse(self.calls.exists())
                self.assertFalse(marker.exists())

    def test_missing_tag_and_explicitly_empty_build_tag_fail_before_external_actions(self):
        for script, env in (("deploy.sh", {"VPS_HOST": "example.test"}),
                            ("remote-deploy.sh", {}),
                            ("build-push.sh", {"DOCKERHUB_USER": "example", "IMAGE_TAG": ""})):
            with self.subTest(script=script):
                result = self.run_script(script, env)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("IMAGE_TAG" if script == "build-push.sh" else "Использование", result.stderr)
                self.assertFalse(self.calls.exists())

    def test_docker_tag_boundaries_before_external_actions(self):
        values = {"DOCKERHUB_USER": "example", "VPS_HOST": "example.test",
                  "POSTGRES_PASSWORD": "test-password", "JWT_SECRET": "test-jwt",
                  "BACKUP_REMOTE": "test:bucket"}
        for script in ("build-push.sh", "deploy.sh", "remote-deploy.sh", "compose.sh", "backup.sh"):
            for tag in ("-bad", ".bad", "a" * 129, "", "_" + "a" * 127):
                with self.subTest(script=script, tag=tag):
                    self.calls.unlink(missing_ok=True)
                    args = (tag,) if script in ("deploy.sh", "remote-deploy.sh") else ()
                    result = self.run_script(script, {**values, "IMAGE_TAG": tag}, args)
                    if len(tag) == 128:
                        self.assertEqual(result.returncode, 91, result.stdout + result.stderr)
                        self.assertTrue(self.calls.exists())
                    else:
                        self.assertNotEqual(result.returncode, 0)
                        self.assertTrue(result.stderr)
                        self.assertFalse(self.calls.exists(), "Некорректный тег дошёл до транспорта")

    def test_real_compose_keeps_literals_and_ignores_dotenv(self):
        marker = self.root / "executed"
        literal = f'  Меню $HOME ${{SMTP_HOST}} $$ # "quote" \'quote\' \\ $(touch {marker}) `touch {marker}`  '
        values = {"DOCKERHUB_USER": "example", "POSTGRES_PASSWORD": literal,
                  "JWT_SECRET": literal, "SMTP_PASSWORD": literal, "SMTP_FROM_NAME": literal,
                  "SMTP_USER": literal, "SMTP_FROM": literal, "SHARE_BASE_URL": literal,
                  "POSTGRES_USER": literal, "POSTGRES_DB": literal}
        (self.root / "server.conf").write_text(
            "\n".join(f"{key}={value}" for key, value in values.items()) + "\n")
        poison = self.root / ".env"
        poison.write_text("SMTP_HOST=poison\nJWT_AUDIENCE=poison\nSMTP_PORT=999\n")
        for overrides in ({}, {"SMTP_FROM_NAME": "", "SMTP_PORT": "", "JWT_ISSUER": ""}):
            with self.subTest(overrides=overrides):
                result = self.run_script("compose.sh", {
                    "PATH": os.environ["PATH"], "COMPOSE_ENV_FILES": str(poison), **overrides},
                    ("config", "--format", "json"))
                self.assertEqual(result.returncode, 0, result.stderr)
                # Compose config экранирует каждый $ как $$ при сериализации
                # (cmd/compose/config.go: runConfig), даже в JSON. Это транспорт,
                # не повторная интерполяция значения. Ниже проверяем и raw env.
                model = json.loads(result.stdout.replace("$$", "$"))
                backend = model["services"]["backend"]["environment"]
                for key in ("JWT_SECRET", "SMTP_PASSWORD", "SMTP_FROM_NAME", "SMTP_USER",
                            "SMTP_FROM", "SHARE_BASE_URL"):
                    self.assertEqual(backend[key], overrides.get(key, literal))
                self.assertEqual(backend["SMTP_PORT"], overrides.get("SMTP_PORT", "587"))
                self.assertEqual(backend["JWT_ISSUER"], overrides.get("JWT_ISSUER", "menu-planner"))
                self.assertEqual(backend["JWT_AUDIENCE"], "menu-planner-client")
                self.assertEqual(backend["SMTP_HOST"], "")
                self.assertIn("ConnectionStrings__Default", backend)
                self.assertEqual(backend["DB_HOST"], "db")
                self.assertEqual(backend["DB_PORT"], "5432")
                for key in ("DB_NAME", "DB_USER", "DB_PASSWORD"):
                    self.assertEqual(backend[key], literal)
                self.assertEqual(model["services"]["db"]["environment"]["POSTGRES_PASSWORD"], literal)
                self.assertEqual(model["services"]["db"]["healthcheck"]["test"],
                                 ["CMD", "pg_isready", "-U", literal, "-d", literal])
                self.assertFalse(marker.exists())
                self.assertFalse(self.calls.exists(), "Настоящий Compose должен выполнять только config")
                environment = self.run_script("compose.sh", {
                    "PATH": os.environ["PATH"], "COMPOSE_ENV_FILES": str(poison), **overrides},
                    ("config", "--environment"))
                self.assertEqual(environment.returncode, 0, environment.stderr)
                for key, value in {**values, **overrides}.items():
                    self.assertIn(f"{key}={value}", environment.stdout.splitlines())
                # Реальный Compose передаёт backend ровно derived-строку из
                # Docker env; Npgsql reparsing этой границы проверяет backend suite.
                self.assertIn("ConnectionStrings__Default=" + backend["ConnectionStrings__Default"],
                              environment.stdout.splitlines())

    def test_real_compose_with_template_and_env_only(self):
        config = self.root / "server.conf"
        shutil.copy(ROOT / "deploy/server.conf.example", config)
        for env_only in (False, True):
            with self.subTest(env_only=env_only):
                if env_only:
                    config.unlink()
                result = self.run_script("compose.sh", {
                    "PATH": os.environ["PATH"], "DOCKERHUB_USER": "example",
                    "POSTGRES_PASSWORD": "test-password", "JWT_SECRET": "test-jwt"},
                    ("config", "--format", "json"))
                self.assertEqual(result.returncode, 0, result.stderr)
                model = json.loads(result.stdout)
                backend = model["services"]["backend"]["environment"]
                self.assertEqual(backend["SMTP_FROM_NAME"], "Меню для домохозяек")
                self.assertEqual(backend["SMTP_PORT"], "587")
                self.assertEqual(backend["JWT_SECRET"], "test-jwt")
                self.assertEqual(backend["DB_PASSWORD"], "test-password")
                self.assertEqual(backend["ConnectionStrings__Default"],
                                 'Host="db";Port=5432;Database="menu_planner";Username="menu";Password="test-password"')
                self.assertEqual(model["services"]["db"]["environment"]["POSTGRES_PASSWORD"],
                                 "test-password")

    def test_malformed_files_and_arithmetic_payload_fail_before_external_actions(self):
        marker = self.root / "executed"
        server = {"DOCKERHUB_USER": "example", "POSTGRES_PASSWORD": "secret",
                  "JWT_SECRET": "secret", "BACKUP_REMOTE": "test:bucket"}
        cases = [("build-push.sh", "deploy/local.conf", ()),
                 ("deploy.sh", "deploy/local.conf", ("abc123",)),
                 ("compose.sh", "server.conf", ("config",)),
                 ("remote-deploy.sh", "server.conf", ("abc123",)),
                 ("backup.sh", "server.conf", ()),
                 ("restore-drill.sh", "server.conf", ("",))]
        for script, filename, args in cases:
            config = self.root / filename
            config.write_text("PATH=secret\n")
            result = self.run_script(script, {**server, "VPS_HOST": "example.test"}, args)
            with self.subTest(script=script):
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("Конфигурация:", result.stderr)
                self.assertNotIn("secret", result.stdout + result.stderr)
                self.assertFalse(self.calls.exists())
            config.unlink()
        for key in ("BACKUP_KEEP_DAILY", "BACKUP_KEEP_WEEKLY"):
            for payload in (f'a[$(touch {marker})]', "", "0", "08", "99999999999999999999"):
                with self.subTest(key=key, payload=payload):
                    self.calls.unlink(missing_ok=True)
                    (self.root / "server.conf").write_text(f"{key}={payload}\n")
                    result = self.run_script("backup.sh", server)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn(key, result.stderr)
                    if payload:
                        self.assertNotIn(payload, result.stderr)
                    self.assertFalse(marker.exists())
                    self.assertFalse(self.calls.exists())

    def test_local_deploy_delivers_server_library_but_not_secrets(self):
        remote = self.root / "server"
        remote.mkdir()
        literal = 'Меню $HOME # "quote" \\ $(touch should-not-exist)'
        (remote / "server.conf").write_text(
            f"DOCKERHUB_USER=example\nPOSTGRES_PASSWORD=remote-secret\nJWT_SECRET=remote-jwt\n"
            f"SMTP_FROM_NAME={literal}\nIMAGE_TAG=old-tag\n")
        (self.root / "server.conf").write_text("POSTGRES_PASSWORD=local-secret-must-not-travel\n")
        (self.root / "deploy/local.conf").write_text(
            f"VPS_HOST=example.test\nAPP_DIR={remote}\n")
        # Изолированный transport adapter: scp копирует в временный «сервер»,
        # ssh запускает доставленный entrypoint локально. Сети и daemon нет.
        transport = '''#!/usr/bin/python3
import json, os, pathlib, shutil, subprocess, sys
command = pathlib.Path(sys.argv[0]).name
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps({"command": command, "args": sys.argv[1:]}) + "\\n")
if command == "scp":
    destination = sys.argv[-1].split(":", 1)[1]
    for source in sys.argv[3:-1]:
        shutil.copy(source, destination)
    sys.exit(0)
sys.exit(subprocess.run(["bash", "-c", sys.argv[-1]]).returncode)
'''
        for command in ("ssh", "scp"):
            (self.bin / command).write_text(transport)
        result = self.run_script("deploy.sh", args=("abc123",))
        self.assertEqual(result.returncode, 91, result.stdout + result.stderr)
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()]
        docker = calls[-1]
        self.assertEqual(docker["command"], "docker")
        self.assertEqual(docker["env"]["POSTGRES_PASSWORD"], "remote-secret")
        self.assertEqual(docker["env"]["SMTP_FROM_NAME"], literal)
        self.assertEqual(docker["env"]["IMAGE_TAG"], "abc123")
        self.assertEqual(docker["args"], ["compose", "--env-file", "/dev/null", "-f",
                                          "docker-compose.prod.yml", "pull"])
        delivered = [argument for call in calls if call["command"] == "scp"
                     for argument in call["args"][2:-1]]
        for filename in ("deploy/config.sh", "deploy/compose.sh", "deploy/remote-deploy.sh",
                         "deploy/backup.sh", "deploy/restore-drill.sh"):
            self.assertIn(filename, delivered)
            self.assertTrue((remote / filename).exists())
        self.assertFalse(any(".conf" in path or ".env" in path for path in delivered))
        self.assertNotIn("remote-secret", result.stdout + result.stderr)
        self.assertFalse((remote / "should-not-exist").exists())


if __name__ == "__main__":
    unittest.main()
