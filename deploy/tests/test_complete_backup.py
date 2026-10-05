"""Проверки тикета 40: согласованный complete-набор и полное восстановление.

Поверх изолированного окружения ``test_backup_restore`` (docker/rclone
подменены, remote — локальный каталог) проверяются: уникальность набора за
запуск, публикация complete только после доставки всех частей, ротация целыми
наборами с защитой последней проверенной точки, состояние последнего backup/drill
и drill — checksum, схема/контрольные рецепты, каждый путь фото, закреплённый
релиз. Prod-данные не трогаются: только временные контейнеры и каталоги.
"""
import gzip
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import tarfile
import unittest

from test_backup_restore import BackupFixture


def set_id_days_ago(days):
    stamp = datetime.now(timezone.utc) - timedelta(days=days)
    return stamp.strftime("%Y-%m-%dT%H%M%S") + "000000000Z"


def read_state(path, key):
    if not Path(path).exists():
        return ""
    for line in Path(path).read_text().splitlines():
        name, _, value = line.partition("=")
        if name == key:
            return value
    return ""


class CompleteSetTests(BackupFixture):
    def seed_set(self, set_id, *, complete=True, schema="20260925134531_Initial",
                 recipes=0, plans=0, entries=0, photo_paths=("photo.txt",),
                 archive_names=None):
        db_bytes = gzip.compress(b"-- dump " + set_id.encode())
        photos_path = self.root / f"seed-{set_id}.tar.gz"
        payload = self.root / f"seed-payload-{set_id}.txt"
        payload.write_text("photo\n")
        with tarfile.open(photos_path, "w:gz") as archive:
            for name in (archive_names if archive_names is not None else photo_paths):
                archive.add(payload, arcname=name)
        photos_bytes = photos_path.read_bytes()
        (self.remote / "db" / f"{set_id}.sql.gz").write_bytes(db_bytes)
        (self.remote / "photos" / f"{set_id}.tar.gz").write_bytes(photos_bytes)
        manifest = {
            "id": set_id,
            "createdAt": "2026-01-01T00:00:00Z",
            "release": "abc123",
            "schema": schema,
            "dbName": f"{set_id}.sql.gz",
            "dbSha256": hashlib.sha256(db_bytes).hexdigest(),
            "photosName": f"{set_id}.tar.gz",
            "photosSha256": hashlib.sha256(photos_bytes).hexdigest(),
            "recipes": recipes,
            "weekPlans": plans,
            "planEntries": entries,
        }
        (self.remote / "manifests" / f"{set_id}.json").write_text(
            json.dumps(manifest, indent=2) + "\n")
        if complete:
            (self.remote / "complete" / set_id).write_text(set_id + "\n")

    def complete_ids(self):
        return sorted(path.name for path in (self.remote / "complete").iterdir())

    def test_each_run_creates_unique_set_and_manifest(self):
        first = self.run_script("backup.sh")
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        second = self.run_script("backup.sh")
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)

        ids = self.complete_ids()
        self.assertEqual(len(ids), 2, "повторный запуск перезаписал набор того же дня")
        for set_id in ids:
            self.assertTrue(set_id.startswith(datetime.now(timezone.utc).strftime("%Y-%m-%d")))
            manifest = json.loads((self.remote / "manifests" / f"{set_id}.json").read_text())
            self.assertTrue(manifest["createdAt"])
            self.assertEqual(manifest["release"], "latest")
            self.assertEqual(manifest["schema"], "20260925134531_Initial")
            db = (self.remote / "db" / f"{set_id}.sql.gz").read_bytes()
            self.assertEqual(manifest["dbSha256"], hashlib.sha256(db).hexdigest())
            photos = (self.remote / "photos" / f"{set_id}.tar.gz").read_bytes()
            self.assertEqual(manifest["photosSha256"], hashlib.sha256(photos).hexdigest())

    def test_complete_marker_only_after_all_parts_delivered(self):
        result = self.run_script("backup.sh", {"RCLONE_COPYTO_NODATA": "1"})
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("Готово", result.stdout)
        self.assertEqual(self.complete_ids(), [], "появился ложный complete без объектов")

    def test_interrupted_run_does_not_create_false_complete(self):
        good = self.run_script("backup.sh")
        self.assertEqual(good.returncode, 0, good.stdout + good.stderr)
        before = self.complete_ids()
        self.assertEqual(len(before), 1)

        bad = self.run_script("backup.sh", {"RCLONE_COPYTO_NODATA": "1"})
        self.assertNotEqual(bad.returncode, 0)
        self.assertEqual(self.complete_ids(), before, "сбойный запуск добавил complete")

    def test_selection_ignores_incomplete_latest(self):
        old = set_id_days_ago(1)
        self.seed_set(old)
        self.env.update({"DOCKER_SCHEMA": "20260925134531_Initial",
                         "DOCKER_RECIPES": "0", "DOCKER_PLANS": "0",
                         "DOCKER_ENTRIES": "0", "DOCKER_PHOTO_PATHS": ""})
        newer = set_id_days_ago(0)
        self.seed_set(newer, complete=False)
        self.assertGreater(newer, old)

        result = self.run_script("restore-drill.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(f"Набор: {old}", result.stdout)
        self.assertNotIn(newer, result.stdout)

    def test_rotation_handles_whole_sets_and_protects_verified(self):
        verified = set_id_days_ago(8)
        old_a = set_id_days_ago(12)
        old_b = set_id_days_ago(10)
        self.seed_set(verified)
        self.seed_set(old_a)
        self.seed_set(old_b)
        state = Path(self.env["BACKUP_STATE_FILE"])
        state.write_text(f"last_drill_ok_set={verified}\nlast_drill_result=ok\n")

        result = self.run_script("backup.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

        for name in ("db", "photos", "manifests", "complete"):
            suffix = ".sql.gz" if name == "db" else (".tar.gz" if name == "photos" else (".json" if name == "manifests" else ""))
            self.assertTrue((self.remote / name / f"{verified}{suffix}").exists(),
                            f"проверенная точка {verified} удалена из {name}")
            for gone in (old_a, old_b):
                self.assertFalse((self.remote / name / f"{gone}{suffix}").exists(),
                                 f"старый набор {gone} не удалён целиком из {name}")

    def test_backup_state_records_last_full_backup(self):
        result = self.run_script("backup.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        state = self.env["BACKUP_STATE_FILE"]
        created = self.complete_ids()[0]
        self.assertEqual(read_state(state, "last_full_backup"), created)
        self.assertTrue(read_state(state, "last_full_backup_at").startswith(
            datetime.now(timezone.utc).strftime("%Y-%m-%d")))

    def test_snapshot_quiesces_writer_around_copy(self):
        result = self.run_script("backup.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        calls = self.calls_of("docker")

        def index_of(predicate):
            return next(i for i, call in enumerate(calls) if predicate(call))

        stop = index_of(lambda call: "stop" in call and call[-1] == "backend")
        start = index_of(lambda call: "start" in call and call[-1] == "backend")
        dump = index_of(lambda call: "pg_dump" in call)
        archive = index_of(lambda call: call[:1] == ["run"] and "tar" in call)
        self.assertLess(stop, dump, "backend не остановлен до дампа")
        self.assertLess(stop, archive, "backend не остановлен до архива фото")
        self.assertLess(archive, start, "backend запущен до конца архивации")

    def test_sunday_creates_complete_weekly_set(self):
        date_adapter = r'''#!/usr/bin/env python3
import os, subprocess, sys
args = sys.argv[1:]
if args == ["+%u"] and os.environ.get("FAKE_WEEKDAY"):
    print(os.environ["FAKE_WEEKDAY"])
    sys.exit(0)
sys.exit(subprocess.call(["/usr/bin/date", *args]))
'''
        path = self.bin / "date"
        path.write_text(date_adapter)
        path.chmod(0o755)
        result = self.run_script("backup.sh", {"FAKE_WEEKDAY": "7"})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        set_id = self.complete_ids()[0]
        for name, suffix in (("db", ".sql.gz"), ("photos", ".tar.gz"),
                             ("manifests", ".json"), ("complete", "")):
            self.assertTrue((self.remote / "weekly" / name / f"{set_id}{suffix}").exists(),
                            f"недельный набор неполный: {name}")


class CompleteDrillTests(BackupFixture):
    def test_drill_rejects_checksum_mismatch_before_containers(self):
        self.write_complete_set(db_sha_override="0" * 64)
        result = self.run_script("restore-drill.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("сумм", result.stderr)
        starts = [call for call in self.calls_of("docker") if call[:1] == ["run"]]
        self.assertEqual(starts, [])

    def test_drill_rejects_incompatible_schema(self):
        self.write_complete_set(manifest_schema="9999_Incompatible")
        result = self.run_script("restore-drill.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("не совпадает", result.stderr)
        self.assertNotIn("Drill успешен", result.stdout)

    def test_drill_rejects_mismatched_reference_counts(self):
        self.write_complete_set(recipes=5)
        self.env["DOCKER_RECIPES"] = "0"
        result = self.run_script("restore-drill.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("рецепт", result.stderr)
        self.assertNotIn("Drill успешен", result.stdout)

    def test_drill_resolves_every_photo_path(self):
        self.write_complete_set(photo_paths=("ghost.jpg",))
        (self.root / "real.jpg").write_text("real\n")
        # В архиве нет ghost.jpg: восстановленная ссылка на потерянный файл.
        archive_names = ("real.jpg",)
        set_id = "2026-01-01T000000000000000Z"
        photos_path = self.root / "x.tar.gz"
        payload = self.root / "real.jpg"
        with tarfile.open(photos_path, "w:gz") as archive:
            archive.add(payload, arcname=archive_names[0])
        (self.remote / "photos" / f"{set_id}.tar.gz").write_bytes(photos_path.read_bytes())
        manifest = json.loads((self.remote / "manifests" / f"{set_id}.json").read_text())
        manifest["photosSha256"] = hashlib.sha256(photos_path.read_bytes()).hexdigest()
        (self.remote / "manifests" / f"{set_id}.json").write_text(
            json.dumps(manifest, indent=2) + "\n")

        result = self.run_script("restore-drill.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("ghost.jpg", result.stderr)
        self.assertNotIn("Drill успешен", result.stdout)

    def test_drill_runs_pinned_release_and_records_result(self):
        self.write_complete_set()
        result = self.run_script("restore-drill.sh")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Drill успешен", result.stdout)

        release_runs = [call for call in self.calls_of("docker")
                        if call[:1] == ["run"] and any("menu-backend:abc123" in arg for arg in call)]
        self.assertEqual(len(release_runs), 1, "закреплённый релиз не запущен")
        self.assertIn("--network", release_runs[0])

        state = self.env["BACKUP_STATE_FILE"]
        self.assertEqual(read_state(state, "last_drill_result"), "ok")
        self.assertEqual(read_state(state, "last_drill_set"), "2026-01-01T000000000000000Z")
        self.assertEqual(read_state(state, "last_drill_ok_set"), "2026-01-01T000000000000000Z")
        self.assertTrue(read_state(state, "last_drill_seconds").isdigit())

    def test_drill_failure_keeps_last_verified_point_protected(self):
        state = Path(self.env["BACKUP_STATE_FILE"])
        state.write_text("last_drill_ok_set=2025-12-01T000000000000000Z\n")
        self.write_complete_set()
        result = self.run_script("restore-drill.sh", {"DOCKER_MODE": "sql-fail"})
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(read_state(state, "last_drill_result"), "fail")
        self.assertEqual(read_state(state, "last_drill_set"), "2026-01-01T000000000000000Z")
        self.assertEqual(read_state(state, "last_drill_ok_set"),
                         "2025-12-01T000000000000000Z",
                         "провал drill снял защиту прежней проверенной точки")

    def test_drill_without_complete_set_fails(self):
        result = self.run_script("restore-drill.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("complete", result.stderr)


if __name__ == "__main__":
    unittest.main()
