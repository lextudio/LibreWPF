#!/usr/bin/env python3
"""Offline controls only: never launch Toolkit or capture the current desktop."""

import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("toolkit_failure", ROOT / "eng/progpu-wpf-toolkit-failure.py")
HELPER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HELPER)


class ToolkitFailureDiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.log = self.directory / "app-log.fixture"
        self.status = self.directory / "app-status.fixture"
        self.log.write_bytes(b"original failed live log\n")
        self.status.write_bytes(b"SplitAction click entered\n")

    def capture(self, environment, platform="darwin", **run_options):
        with patch.dict(os.environ, environment, clear=True), patch.object(HELPER.sys, "platform", platform):
            with patch.object(HELPER.subprocess, "run", **run_options) as run:
                result = HELPER.record_failure(self.directory, 17, 1234)
        self.assertEqual(self.log.read_bytes(), b"original failed live log\n")
        self.assertEqual(self.status.read_bytes(), b"SplitAction click entered\n")
        self.assertEqual(result["validationExitCode"], 17)
        self.assertEqual(json.loads((self.directory / "failure.json").read_text()), result)
        return result, run

    @staticmethod
    def hosted():
        return {"PROGPU_WPF_TOOLKIT_FAILURE_SCREENSHOT": "1", "GITHUB_ACTIONS": "true",
                "RUNNER_ENVIRONMENT": "github-hosted"}

    def test_default_retains_original_bytes_without_screenshot(self):
        result, run = self.capture({})
        self.assertEqual(result["screenshot"], "disabled")
        run.assert_not_called()

    def test_local_and_self_hosted_desktops_are_not_captured(self):
        for environment in ({"PROGPU_WPF_TOOLKIT_FAILURE_SCREENSHOT": "1"},
                            dict(self.hosted(), RUNNER_ENVIRONMENT="self-hosted")):
            with self.subTest(environment=environment):
                (self.directory / "failure.json").unlink(missing_ok=True)
                result, run = self.capture(environment)
                self.assertEqual(result["screenshot"], "not-hosted-macos")
                run.assert_not_called()

    def test_non_macos_host_is_not_captured(self):
        result, run = self.capture(self.hosted(), platform="linux")
        self.assertEqual(result["screenshot"], "not-hosted-macos")
        run.assert_not_called()

    def test_hosted_capture_has_fixed_timeout_and_no_shell(self):
        def screenshot(command, **options):
            self.assertEqual(command[:3], ["/usr/sbin/screencapture", "-x", "-m"])
            self.assertEqual(options["timeout"], 3)
            self.assertNotIn("shell", options)
            Path(command[3]).write_bytes(b"controlled screenshot fixture")
            return subprocess.CompletedProcess(command, 0)

        result, run = self.capture(self.hosted(), side_effect=screenshot)
        self.assertEqual(result["screenshot"], "captured")
        run.assert_called_once()

    def test_timeout_is_diagnostic_not_a_new_validation_outcome(self):
        result, _ = self.capture(self.hosted(), side_effect=subprocess.TimeoutExpired("capture", 3))
        self.assertEqual(result["screenshot"], "timed-out")

    def test_nonzero_capture_is_recorded(self):
        result, _ = self.capture(self.hosted(), return_value=subprocess.CompletedProcess([], 9))
        self.assertEqual(result["screenshot"], "failed")
        self.assertEqual(result["screenshotExitCode"], 9)

    def test_missing_capture_tool_is_recorded(self):
        result, _ = self.capture(self.hosted(), side_effect=FileNotFoundError("capture missing"))
        self.assertEqual(result["screenshot"], "unavailable")

    def test_existing_report_is_not_overwritten(self):
        report = self.directory / "failure.json"
        report.write_bytes(b"original")
        with self.assertRaises(FileExistsError), patch.object(HELPER.subprocess, "run") as run:
            HELPER.record_failure(self.directory, 17, 1234)
        self.assertEqual(report.read_bytes(), b"original")
        run.assert_not_called()

    def test_existing_screenshot_is_not_overwritten(self):
        screenshot = self.directory / "failure.png"
        screenshot.write_bytes(b"original")
        result, run = self.capture(self.hosted())
        self.assertEqual(result["screenshot"], "existing-file-not-overwritten")
        self.assertEqual(screenshot.read_bytes(), b"original")
        run.assert_not_called()

    def test_runner_captures_before_cleanup_and_keeps_original_deadline(self):
        source = (ROOT / "eng/run-progpu-wpf-toolkit.sh").read_text()
        cleanup = source.split("cleanup_live_probe() {", 1)[1].split("trap ", 1)[0]
        self.assertLess(cleanup.index("progpu-wpf-toolkit-failure.py"), cleanup.index('kill "${apphost_pid}"'))
        self.assertNotIn("rm -f", cleanup)
        self.assertIn('return "${probe_exit}"', cleanup)
        self.assertIn("PROGPU_WPF_TOOLKIT_LIVE_VALIDATE_TIMEOUT_SECONDS:-180", source)

    def test_bash_exit_trap_keeps_failure_even_when_diagnostics_fail(self):
        source = (ROOT / "eng/run-progpu-wpf-toolkit.sh").read_text()
        cleanup = source.split("  cleanup_live_probe() {", 1)[1].split("\n  trap ", 1)[0]
        # Execute the actual cleanup function with inert command doubles: no
        # process is signalled and no desktop or real app is accessed.
        script = """set -euo pipefail
repo_root=/unused
live_directory=/unused
apphost_pid=1234
python3() { echo capture; return 9; }
kill() { echo "kill:$1"; return 0; }
sleep() { :; }
wait() { :; }
cleanup_live_probe() {""" + cleanup + """
trap 'cleanup_live_probe "$?"' EXIT
exit "$1"
"""
        for exit_code in (1, 17):
            with self.subTest(exit_code=exit_code):
                result = subprocess.run(["bash", "-c", script, "diagnostic-control", str(exit_code)],
                                        text=True, capture_output=True, timeout=5, check=False)
                self.assertEqual(result.returncode, exit_code, result.stderr)
                self.assertEqual(result.stdout.splitlines(),
                                 ["capture", "kill:-0", "kill:1234", "kill:-0", "kill:-9"])
                self.assertIn("original log/status retained", result.stderr)


if __name__ == "__main__":
    unittest.main()
