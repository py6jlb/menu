"""Структурные проверки production-запуска и ресурсного бюджета (тикет 44).

Тесты читают поставляемые артефакты (Compose dev/prod, Dockerfile, systemd-units,
deploy-скрипты) без запуска Docker daemon. Проверяются: loopback dev-портов,
минимум внешних портов prod, непривилегированный backend, capabilities и
no-new-privileges, ограничения backup/drill, непересечение reboot и копирования.
"""
import re
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


def compose_ports(text):
    """Публикуемые порты по сервисам (простой разбор отступов Compose)."""
    ports = {}
    service = None
    in_ports = False
    for raw in text.splitlines():
        line = raw.rstrip()
        if not line.strip() or line.strip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip())
        stripped = line.strip()
        if indent == 2 and stripped.endswith(":"):
            service = stripped[:-1]
            in_ports = False
        elif indent == 4 and stripped == "ports:":
            in_ports = True
            ports.setdefault(service, [])
        elif in_ports and indent >= 6 and stripped.startswith("- "):
            value = stripped[2:].strip().strip('"').strip("'")
            ports[service].append(value)
        elif in_ports and indent <= 4 and stripped != "ports:":
            in_ports = False
    return ports


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


class DevComposeLoopbackTests(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / "docker-compose.yml").read_text()

    def test_all_dev_ports_are_bound_to_loopback(self):
        ports = compose_ports(self.text)
        self.assertEqual(ports["db"], ["127.0.0.1:5432:5432"])
        self.assertEqual(ports["backend"], ["127.0.0.1:8080:8080"])
        self.assertEqual(ports["frontend"], ["127.0.0.1:8081:80"])
        for service, mappings in ports.items():
            for mapping in mappings:
                self.assertTrue(mapping.startswith("127.0.0.1:"),
                                f"{service}: порт {mapping} не привязан к loopback")

    def test_dev_backend_is_unprivileged_and_opt_in_lab(self):
        backend = service_block(self.text, "backend")
        self.assertIn("cap_drop:", backend)
        self.assertIn("ALL", backend)
        self.assertIn("no-new-privileges:true", backend)
        self.assertIn("DEPLOYMENT_MODE:", backend)
        self.assertIn("lab", backend)


class ProdComposeExternalPortsTests(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / "docker-compose.prod.yml").read_text()

    def test_only_caddy_publishes_expected_external_ports(self):
        ports = compose_ports(self.text)
        self.assertEqual(set(ports["caddy"]), {"80:80", "443:443", "443:443/udp"})
        for service, mappings in ports.items():
            if service != "caddy":
                self.assertEqual(mappings, [], f"{service}: неожиданная публикация портов")

    def test_services_carry_no_new_privileges(self):
        for service in ("caddy", "db", "otel-collector", "backend", "frontend"):
            with self.subTest(service=service):
                block = service_block(self.text, service)
                self.assertIn("no-new-privileges:true", block)

    def test_backend_and_caddy_have_minimal_capabilities(self):
        backend = service_block(self.text, "backend")
        self.assertIn("cap_drop:", backend)
        self.assertIn("ALL", backend)
        caddy = service_block(self.text, "caddy")
        self.assertIn("cap_drop:", caddy)
        self.assertIn("NET_BIND_SERVICE", caddy)

    def test_backend_can_reach_outbound_smtp(self):
        backend = service_block(self.text, "backend")
        self.assertNotIn("network_mode: none", backend)
        self.assertNotIn("internal: true", self.text)

    def test_backend_receives_production_mode_and_public_url(self):
        backend = service_block(self.text, "backend")
        self.assertIn("DEPLOYMENT_MODE:", backend)
        self.assertIn("PUBLIC_BASE_URL:", backend)


class BackendImageHardeningTests(unittest.TestCase):
    def setUp(self):
        self.dockerfile = (ROOT / "backend/Dockerfile").read_text()

    def test_runtime_runs_as_non_root_user(self):
        user_lines = re.findall(r"^USER (.+)$", self.dockerfile, re.M)
        self.assertTrue(user_lines, "в образе нет USER")
        self.assertNotEqual(user_lines[-1].strip(), "root")
        self.assertNotIn("USER 0", self.dockerfile)
        self.assertIn("USER app", self.dockerfile)

    def test_photos_dir_prepared_for_service_user(self):
        self.assertIn("/app/photos", self.dockerfile)
        self.assertRegex(self.dockerfile, r"chown[^\n]*/app/photos")
        self.assertRegex(self.dockerfile, r"COPY --from=build --chown=")


class ResourceBudgetTests(unittest.TestCase):
    def test_backup_service_bounds_auxiliary_operation(self):
        service = (ROOT / "deploy/systemd/menu-backup.service").read_text()
        self.assertIn("MemoryMax=", service)
        self.assertIn("CPUQuota=", service)
        self.assertIn("Nice=", service)
        self.assertIn("IOSchedulingClass=", service)

    def test_backup_and_drill_bound_memory_and_require_free_space(self):
        backup = (ROOT / "deploy/backup.sh").read_text()
        self.assertIn("BACKUP_MIN_FREE_MB", backup)
        self.assertIn("BACKUP_MEMORY_LIMIT", backup)
        self.assertIn('--memory "$BACKUP_MEMORY_LIMIT"', backup)
        self.assertIn("backup_require_free_space", backup)

        drill = (ROOT / "deploy/restore-drill.sh").read_text()
        self.assertIn("DRILL_MIN_FREE_MB", drill)
        self.assertIn("DRILL_MEMORY_LIMIT", drill)
        self.assertGreaterEqual(drill.count('--memory "$DRILL_MEMORY_LIMIT"'), 2)
        self.assertIn("backup_require_free_space", drill)

    def test_backup_checks_space_before_quiescing_writer(self):
        backup = (ROOT / "deploy/backup.sh").read_text()
        check = backup.index("backup_require_free_space")
        invocation = re.search(r"^snapshot$", backup, re.M)
        assert invocation is not None
        self.assertLess(check, invocation.start())

    def test_drill_checks_space_before_downloading_set(self):
        drill = (ROOT / "deploy/restore-drill.sh").read_text()
        self.assertLess(drill.index("backup_require_free_space"),
                        drill.index("rclone copyto"))

    def test_reboot_does_not_intersect_backup_window(self):
        timer = (ROOT / "deploy/systemd/menu-backup.timer").read_text()
        bootstrap = (ROOT / "deploy/bootstrap.sh").read_text()

        start = re.search(r"OnCalendar=\*-\*-\* (\d{2}):(\d{2}):\d{2}", timer)
        delay = re.search(r"RandomizedDelaySec=(\d+)m", timer)
        reboot = re.search(r'Automatic-Reboot-Time "(\d{2}):(\d{2})"', bootstrap)
        assert start and delay and reboot

        start_minutes = int(start.group(1)) * 60 + int(start.group(2))
        end_minutes = start_minutes + int(delay.group(1))
        reboot_minutes = int(reboot.group(1)) * 60 + int(reboot.group(2))
        self.assertLess(end_minutes, reboot_minutes,
                        "окно backup пересекается с плановым reboot")


class ProxyTrustTests(unittest.TestCase):
    def test_prod_trusts_only_the_caddy_edge(self):
        prod = (ROOT / "docker-compose.prod.yml").read_text()
        backend = service_block(prod, "backend")
        caddy = service_block(prod, "caddy")
        self.assertIn('TRUSTED_PROXY_ADDRESSES: "172.29.0.10"', backend)
        self.assertIn("ipv4_address: 172.29.0.10", caddy)
        self.assertIn("subnet: 172.29.0.0/24", prod)

    def test_dev_trusts_only_the_nginx_entry(self):
        dev = (ROOT / "docker-compose.yml").read_text()
        backend = service_block(dev, "backend")
        frontend = service_block(dev, "frontend")
        self.assertIn('TRUSTED_PROXY_ADDRESSES: "172.28.0.10"', backend)
        self.assertIn("ipv4_address: 172.28.0.10", frontend)
        self.assertIn("subnet: 172.28.0.0/24", dev)


class DocumentationTests(unittest.TestCase):
    def test_readme_documents_budget_and_access(self):
        readme = (ROOT / "deploy/README.md").read_text()
        for marker in ("Ресурсный бюджет", "127.0.0.1", "DEPLOYMENT_MODE"):
            with self.subTest(marker=marker):
                self.assertIn(marker, readme)

    def test_readme_documents_rate_limit_proxy_trust_and_single_replica(self):
        readme = (ROOT / "deploy/README.md").read_text()
        for marker in (
            "TRUSTED_PROXY_ADDRESSES",
            "Caddy",
            "одну реплику",
            "Retry-After",
            "Redis",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, readme)


if __name__ == "__main__":
    unittest.main()
