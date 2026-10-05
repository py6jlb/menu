"""Проверки тикета 41: откат с учётом совместимости схемы.

Поверх изолированного окружения ``test_backup_restore`` (docker/rclone подменены,
remote — локальный каталог) проверяются два явных пути:

* **code-only** — прежний образ при совместимой схеме; схема не откатывается;
* **recovery** — несовместимое изменение схемы: свежая recovery-точка привязана
  к обновлению, записи остановлены, данные/фото восстановлены из complete-набора,
  затем выбран прежний release и проверен пользовательский доступ.

Дополнительно: lock сериализует параллельные деплои/откаты, прерванная операция
оставляет диагностируемое состояние current/previous, а down-миграции не
используются как неявный откат. Реальный VPS, Docker daemon и БД не запускаются.
"""
import fcntl
from pathlib import Path
import json
import unittest

from test_backup_restore import BackupFixture, RCLONE_ADAPTER, ROOT


ROLLBACK_DOCKER = r'''#!/usr/bin/env python3
import json, os, pathlib, sys
args = sys.argv[1:]
with open(os.environ["CALLS"], "a") as stream:
    stream.write(json.dumps({"command": "docker", "args": args}) + "\n")
mode = os.environ.get("DOCKER_MODE", "ok")


def mapping(target):
    for i, a in enumerate(args):
        if a == "-v" and i + 1 < len(args):
            spec = args[i + 1].split(":")
            if len(spec) >= 2 and spec[1] == target:
                return spec[0]
    return None


def sql_output(sql):
    schema = os.environ.get("DOCKER_SCHEMA", "20260925134531_Initial")
    recipes = os.environ.get("DOCKER_RECIPES", "0")
    plans = os.environ.get("DOCKER_PLANS", "0")
    entries = os.environ.get("DOCKER_ENTRIES", "0")
    counts = "|".join([recipes, plans, entries])
    if "MigrationId" in sql and "Recipes" in sql:
        return schema + "|" + counts
    if "MigrationId" in sql:
        return schema
    if "PhotoPath" in sql:
        return os.environ.get("DOCKER_PHOTO_PATHS", "")
    if "left join" in sql:
        return os.environ.get("DOCKER_ORPHANS", "0")
    if "Recipes" in sql and "count" in sql:
        return counts
    return ""


def run_psql(rest):
    if "-c" in rest:
        out = sql_output(rest[rest.index("-c") + 1])
        if out:
            sys.stdout.write(out + "\n")
        sys.exit(0)
    sys.stdin.buffer.read()
    sys.exit(0)


if args and args[0] == "image" and "inspect" in args:
    sys.stdout.write(os.environ.get("IMAGE_DIGEST", "sha256:" + "a" * 64) + "\n")
    sys.exit(0)

if args and args[0] == "inspect":
    if mode == "no-photos-volume":
        sys.exit(0)
    sys.stdout.write("photosvol\n")
    sys.exit(0)

if args and args[0] == "network":
    sys.exit(0)

if args and args[0] == "rm":
    sys.exit(0)

if args and args[0] == "run":
    if "tar" in args and "czf" in args:
        import tarfile
        host = mapping("/backup")
        for a in args:
            if a.startswith("/backup/") and host:
                target = pathlib.Path(host, pathlib.Path(a).name)
                payload = target.parent / ("payload-" + target.name)
                payload.write_bytes(b"photo\n")
                with tarfile.open(target, "w:gz") as archive:
                    archive.add(payload, arcname="photo.txt")
        sys.exit(0)
    sys.exit(0)

if args and args[0] == "compose":
    rest = args[args.index("compose") + 1:]
    while rest and rest[0].startswith("-"):
        rest = rest[2:] if rest[0] in ("--env-file", "-f", "--profile") else rest[1:]
    head = rest[:1]
    if head in (["pull"], ["up"], ["stop"], ["start"], ["logs"]):
        sys.exit(0)
    if head == ["ps"]:
        if "backend" in rest:
            if mode == "backend-missing":
                sys.exit(0)
            sys.stdout.write("backendcid\n")
        sys.exit(0)
    if head == ["exec"]:
        rest = rest[1:]
        while rest and rest[0].startswith("-"):
            rest = rest[1:]
        container = rest[0] if rest else ""
        rest = rest[1:]
        if container == "caddy" and "wget" in rest:
            url = next((a for a in rest if a.startswith("http")), "")
            if "ready" in url:
                if mode == "ready-fail":
                    sys.stderr.write("  HTTP/1.1 503 Service Unavailable\n")
                    sys.exit(1)
                sys.stdout.write('{"status":"ready","service":"menu-planner-api"}\n')
                sys.exit(0)
            if "/api/recipes" in url:
                sys.stderr.write("  HTTP/1.1 401 Unauthorized\n")
                sys.exit(8)
            sys.exit(0)
        if container == "db" and "pg_dump" in rest:
            sys.stdout.write("-- dump\nCREATE TABLE t();\n")
            sys.exit(0)
        if container == "db" and "psql" in rest:
            run_psql(rest)
        sys.exit(0)
    sys.exit(0)

sys.exit(0)
'''


def read_state(path, key):
    if not Path(path).exists():
        return ""
    for line in Path(path).read_text().splitlines():
        name, _, value = line.partition("=")
        if name == key:
            return value
    return ""


class RollbackHarness(BackupFixture):
    SCRIPTS = ("config.sh", "release.sh", "backup-lib.sh", "deploy-lib.sh",
               "backup.sh", "remote-deploy.sh", "rollback.sh")

    def setUp(self):
        super().setUp()
        self._adapter("docker", ROLLBACK_DOCKER)
        self._adapter("rclone", RCLONE_ADAPTER)
        self.env.update({"READY_TIMEOUT_SECONDS": "2", "ROLLBACK_READY_TIMEOUT_SECONDS": "2"})

    def deploy(self, tag, env=None, schema_change=False):
        args = (tag, "deadbeef")
        if schema_change:
            args = (tag, "deadbeef", "--schema-change")
        return self.run_script("remote-deploy.sh", env, args)

    def rollback(self, args=(), env=None):
        return self.run_script("rollback.sh", env, args)

    def current(self):
        return read_state(self.root / "current-release", "IMAGE_TAG")

    def previous(self):
        return read_state(self.root / "previous-release", "IMAGE_TAG")

    def reset_calls(self):
        self.calls_path.write_text("")

    def index_of(self, predicate):
        calls = self.calls_of("docker")
        return next(i for i, call in enumerate(calls) if predicate(call))


class CodeOnlyRollbackTests(RollbackHarness):
    def test_compatible_rollback_replaces_image_without_touching_schema(self):
        first = self.deploy("aaa111")
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        second = self.deploy("bbb222")
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        self.assertEqual(self.current(), "bbb222")
        self.assertEqual(self.previous(), "aaa111")

        result = self.rollback()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.current(), "aaa111")
        self.assertEqual(self.previous(), "bbb222")

        output = (result.stdout + result.stderr).lower()
        self.assertIn("code-only", output)
        self.assertIn("схем", output)

        # Никакого восстановления данных: набор не скачивается и не выгружается.
        copies = [call for call in self.calls_of("rclone") if call and call[0] == "copyto"]
        self.assertEqual(copies, [], "code-only откат трогал backup-набор")
        self.assertFalse(any("DROP SCHEMA" in a
                             for call in self.calls_of("docker") for a in call),
                         "code-only откат сбрасывал схему")

    def test_rollback_without_previous_release_is_rejected(self):
        (self.root / "current-release").write_text("IMAGE_TAG=bbb222\n")
        result = self.rollback()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("previous", result.stderr.lower())


class RecoveryPointTests(RollbackHarness):
    def test_risky_schema_change_creates_fresh_recovery_point(self):
        first = self.deploy("aaa111")
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)

        result = self.deploy("bbb222", schema_change=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

        point = self.root / "recovery-point"
        self.assertTrue(point.exists(), "рискованное обновление без recovery-точки")
        set_id = read_state(point, "SET")
        self.assertTrue(set_id, "recovery-точка без backup ID")
        self.assertEqual(read_state(point, "FOR_RELEASE"), "bbb222")
        self.assertEqual(read_state(point, "FROM_RELEASE"), "aaa111")
        self.assertEqual(read_state(point, "SCHEMA"), "20260925134531_Initial")

        # Выбранный backup ID действительно опубликован как complete-набор.
        self.assertTrue((self.remote / "complete" / set_id).exists())

        manifest = json.loads((self.root / "release.json").read_text())
        self.assertEqual(manifest.get("recoverySet"), set_id)

    def test_code_only_deploy_does_not_leave_recovery_point(self):
        first = self.deploy("aaa111")
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        result = self.deploy("bbb222")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse((self.root / "recovery-point").exists())
        # previous-release фиксирует последний успешный релиз — это цель отката.
        self.assertEqual(self.previous(), "aaa111")


class RecoveryRollbackTests(RollbackHarness):
    def prepare_risky_update(self):
        first = self.deploy("aaa111")
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        second = self.deploy("bbb222", schema_change=True)
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        return read_state(self.root / "recovery-point", "SET")

    def test_recovery_requires_explicit_confirmation_and_warns_about_data_loss(self):
        set_id = self.prepare_risky_update()

        result = self.rollback()
        self.assertNotEqual(result.returncode, 0)
        output = result.stdout + result.stderr
        self.assertIn(set_id, output)
        self.assertIn("потерян", output.lower())
        self.assertIn("--yes", output)
        # До подтверждения данные не трогаются и записи не останавливаются.
        self.assertEqual(self.current(), "bbb222")
        self.assertFalse(any("DROP SCHEMA" in a
                             for call in self.calls_of("docker") for a in call))

    def test_recovery_restores_data_photos_then_previous_release(self):
        set_id = self.prepare_risky_update()
        self.reset_calls()

        result = self.rollback(("--yes",))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.current(), "aaa111")
        self.assertEqual(self.previous(), "bbb222")

        # Изменения остановлены до восстановления и код переключён после него.
        stop = self.index_of(lambda call: "stop" in call and "backend" in call)
        drop = self.index_of(lambda call: any("DROP SCHEMA" in a for a in call))
        up = self.index_of(lambda call: "up" in call)
        photos = self.index_of(lambda call: call[:1] == ["run"] and "find" in " ".join(call))
        self.assertLess(stop, drop, "backend не остановлен до восстановления БД")
        self.assertLess(drop, photos, "фото восстановлены до БД")
        self.assertLess(photos, up, "release переключён до восстановления данных")

        # Скачивание именно привязанного набора.
        copies = [" ".join(call) for call in self.calls_of("rclone") if call and call[0] == "copyto"]
        self.assertTrue(any(f"db/{set_id}.sql.gz" in call for call in copies), copies)
        self.assertTrue(any(f"photos/{set_id}.tar.gz" in call for call in copies), copies)

        # Пользовательский доступ проверен (readiness + защищённый маршрут /api).
        urls = [a for call in self.calls_of("docker") for a in call if a.startswith("http")]
        self.assertTrue(any(url.endswith("/ready") for url in urls), urls)
        self.assertTrue(any("/api/recipes" in url for url in urls), urls)

        state = self.env["BACKUP_STATE_FILE"]
        self.assertEqual(read_state(state, "last_rollback_to"), "aaa111")
        self.assertEqual(read_state(state, "last_rollback_set"), set_id)
        self.assertTrue(read_state(state, "last_rollback_seconds").isdigit())
        self.assertFalse((self.root / "recovery-point").exists(),
                         "recovery-точка не снята после успешного восстановления")
        self.assertFalse((self.root / "rollback-state").exists())

    def test_code_only_is_forbidden_when_schema_incompatible(self):
        self.prepare_risky_update()
        result = self.rollback(("--yes", "--code-only"))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("recovery", (result.stdout + result.stderr).lower())
        self.assertEqual(self.current(), "bbb222")

    def test_interrupted_risky_deploy_still_requires_recovery(self):
        first = self.deploy("aaa111")
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        # Рискованный деплой падает на readiness уже после снятия recovery-точки:
        # current-release ещё aaa111, но схема могла уехать на bbb222.
        failed = self.deploy("bbb222", {"DOCKER_MODE": "ready-fail"}, schema_change=True)
        self.assertNotEqual(failed.returncode, 0)
        self.assertEqual(self.current(), "aaa111")
        set_id = read_state(self.root / "recovery-point", "SET")

        self.reset_calls()
        result = self.rollback(("--yes",))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        output = (result.stdout + result.stderr).lower()
        self.assertIn("recovery", output)
        self.assertIn(set_id, result.stdout + result.stderr)
        self.assertTrue(any("DROP SCHEMA" in a
                            for call in self.calls_of("docker") for a in call),
                        "прерванный рискованный деплой откатили без восстановления схемы")
        self.assertEqual(self.current(), "aaa111")


class SerializationTests(RollbackHarness):
    def test_parallel_operation_is_blocked_by_lock(self):
        lock_path = self.root / "deploy.lock"
        handle = open(lock_path, "w")
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            result = self.deploy("aaa111", {"DEPLOY_LOCK_TIMEOUT": "0"})
        finally:
            fcntl.flock(handle, fcntl.LOCK_UN)
            handle.close()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("занят", result.stderr)
        self.assertEqual(self.calls_of("docker"), [], "операция дошла до Docker под чужим lock")

    def test_interrupted_deploy_leaves_diagnosable_state(self):
        first = self.deploy("aaa111")
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        failed = self.deploy("bbb222", {"DOCKER_MODE": "ready-fail"})
        self.assertNotEqual(failed.returncode, 0)

        # Текущий релиз не переключён, намерение и previous сохранены для разбора.
        self.assertEqual(self.current(), "aaa111")
        intent = self.root / "deploy-intent"
        self.assertTrue(intent.exists(), "прерванная операция без маркера намерения")
        self.assertEqual(read_state(intent, "TAG"), "bbb222")
        self.assertEqual(read_state(intent, "MODE"), "deploy")


class ForwardOnlyDocumentationTests(unittest.TestCase):
    def test_down_migrations_are_not_used_as_rollback(self):
        for script in ("rollback.sh", "remote-deploy.sh", "deploy.sh"):
            text = (ROOT / "deploy" / script).read_text()
            self.assertNotIn("migrations remove", text)
            self.assertNotIn("database update", text)
            self.assertNotIn("ef database", text)


class RollbackDocumentationTests(unittest.TestCase):
    def test_readme_documents_both_paths_lock_and_manual_check(self):
        readme = (ROOT / "deploy/README.md").read_text()
        for needle in ("code-only", "recovery", "rollback.sh", "recovery-point",
                       "deploy.lock", "forward-only", "стенд"):
            self.assertIn(needle, readme, f"README не описывает {needle!r}")
        self.assertIn("--schema-change", readme)

    def test_adr_notes_down_migrations_are_not_implicit_rollback(self):
        adr = (ROOT / "docs/adr/0007-ef-core-migrations.md").read_text()
        self.assertIn("down-миграции", adr)
        self.assertIn("code-only", adr)

    def test_deploy_delivers_rollback_scripts(self):
        deploy = (ROOT / "deploy/deploy.sh").read_text()
        self.assertIn("rollback.sh", deploy)
        self.assertIn("deploy-lib.sh", deploy)

    def test_release_config_covers_rollback_scripts(self):
        release = (ROOT / "deploy/release.sh").read_text()
        self.assertIn("rollback.sh", release)
        self.assertIn("deploy-lib.sh", release)


if __name__ == "__main__":
    unittest.main()
