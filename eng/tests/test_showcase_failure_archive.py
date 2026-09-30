#!/usr/bin/env python3
"""Offline byte fixtures only; no Showcase, renderer, SDK build, or desktop use."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import time
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("showcase_archive", ROOT / "eng/progpu-wpf-preserve-showcase.py")
HELPER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HELPER)


class ShowcaseFailureArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.repo = Path(self.temporary.name).resolve()
        for name in ("app", "native-build", "native-runtime", "evidence"):
            (self.repo / name).mkdir()
        self.args = argparse.Namespace(
            repository=self.repo, app_directory=self.repo / "app",
            native_build_directory=self.repo / "native-build", native_runtime_directory=self.repo / "native-runtime",
            evidence_parent=self.repo / "evidence", source_commit="a" * 40, run_id="123", run_attempt="2")
        for name in (HELPER.APP, f"{HELPER.APP}.dll"):
            (self.args.app_directory / name).write_bytes(b"offline placeholder, not an executable\n")
        for suffix in ("deps.json", "runtimeconfig.json"):
            (self.args.app_directory / f"{HELPER.APP}.{suffix}").write_text(
                json.dumps({"runtimeOptions": {"tfm": "net10.0"}}))
        (self.args.app_directory / HELPER.APP).chmod(0o755)
        (self.args.native_build_directory / "libprogpu_native.dylib").write_bytes(b"offline engine fixture")
        (self.args.native_runtime_directory / "libwgpu_native.dylib").write_bytes(b"offline provider fixture")
        (self.args.app_directory / "path with spaces").mkdir()
        (self.args.app_directory / "path with spaces/content.txt").write_bytes(b"original\x00bytes")

    def git(self, repo, *args, deadline):
        self.assertEqual(repo, self.repo)
        return "a" * 40 if args == ("rev-parse", "HEAD") else ""

    def capture(self, expected=0):
        before = set(self.args.evidence_parent.iterdir())
        with patch.object(HELPER, "git", side_effect=self.git):
            self.assertEqual(HELPER.capture(self.args), expected)
        added = set(self.args.evidence_parent.iterdir()) - before
        self.assertEqual(len(added), 1)
        run = added.pop()
        return run, json.loads((run / "receipt.json").read_text())

    def test_archive_bytes_hashes_modes_and_provenance_are_exact_and_unqualified(self):
        run, receipt = self.capture()
        self.assertFalse(receipt["qualified"])
        self.assertEqual(receipt["sourceCommit"], "a" * 40)
        self.assertEqual(receipt["workflowRunId"], "123")
        self.assertEqual(receipt["workflowRunAttempt"], "2")
        archive = run / receipt["archive"]["path"]
        self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), receipt["archive"]["sha256"])
        with tarfile.open(archive) as stream:
            self.assertEqual(len(stream.getmembers()), len(receipt["files"]))
            for record in receipt["files"]:
                member = stream.getmember(record["path"])
                self.assertTrue(member.isfile())
                self.assertEqual(member.mode, record["mode"])
                actual = stream.extractfile(member).read()
                self.assertEqual(len(actual), record["bytes"])
                self.assertEqual(hashlib.sha256(actual).hexdigest(), record["sha256"])
            self.assertEqual(stream.getmember(f"app/{HELPER.APP}").mode, 0o755)
        self.assertEqual((self.args.app_directory / "path with spaces/content.txt").read_bytes(), b"original\x00bytes")

    def test_each_capture_is_fresh_and_preserves_previous_evidence(self):
        self.capture()
        first = next(self.args.evidence_parent.iterdir())
        original = (first / "receipt.json").read_bytes()
        self.capture()
        self.assertEqual(len(list(self.args.evidence_parent.iterdir())), 2)
        self.assertEqual((first / "receipt.json").read_bytes(), original)

    def test_missing_app_or_native_input_has_explicit_incomplete_receipt(self):
        for path in (self.args.app_directory / f"{HELPER.APP}.dll",
                     self.args.native_runtime_directory / "libwgpu_native.dylib"):
            with self.subTest(path=path):
                original = path.read_bytes()
                path.unlink()
                _, receipt = self.capture(1)
                self.assertEqual(receipt["status"], "incomplete")
                self.assertNotIn("archive", receipt)
                path.write_bytes(original)

    def test_wrong_head_and_malformed_run_identity_fail_closed(self):
        for field, value in (("source_commit", "b" * 40), ("source_commit", "bad"),
                             ("run_id", "0"), ("run_attempt", "1\nforged")):
            with self.subTest(field=field, value=value):
                original = getattr(self.args, field)
                setattr(self.args, field, value)
                _, receipt = self.capture(1)
                self.assertNotIn("archive", receipt)
                setattr(self.args, field, original)

    def test_symlink_file_directory_and_outside_root_are_rejected(self):
        for target in (self.args.native_runtime_directory / "libwgpu_native.dylib",
                       self.args.native_runtime_directory):
            link = self.args.app_directory / "link"
            link.symlink_to(target)
            _, receipt = self.capture(1)
            self.assertIn("regular", receipt["error"])
            link.unlink()
        self.args.app_directory = self.repo.parent
        before = set(self.args.evidence_parent.iterdir())
        with self.assertRaisesRegex(ValueError, "must not be inside"):
            HELPER.capture(self.args)
        self.assertEqual(set(self.args.evidence_parent.iterdir()), before)

    def test_symlink_root_and_nested_evidence_are_rejected(self):
        link = self.repo / "app-link"
        link.symlink_to(self.args.app_directory)
        self.args.app_directory = link
        _, receipt = self.capture(1)
        self.assertIn("Symlink input directory", receipt["error"])
        self.args.app_directory = self.repo / "app"
        self.args.evidence_parent = self.args.app_directory / "evidence"
        self.args.evidence_parent.mkdir()
        with self.assertRaisesRegex(ValueError, "must not be inside"):
            HELPER.capture(self.args)
        self.assertEqual(list(self.args.evidence_parent.iterdir()), [])

    def test_required_metadata_symlink_is_rejected_before_reading_it(self):
        metadata = self.args.app_directory / f"{HELPER.APP}.runtimeconfig.json"
        metadata.unlink()
        metadata.symlink_to(self.args.native_runtime_directory / "libwgpu_native.dylib")
        _, receipt = self.capture(1)
        self.assertIn("regular files", receipt["error"])

    def test_byte_file_and_archive_budgets_are_enforced(self):
        for name, budget in (("MAX_BYTES", 1), ("MAX_FILES", 1), ("MAX_ARCHIVE_BYTES", 1)):
            with self.subTest(name=name), patch.object(HELPER, name, budget):
                _, receipt = self.capture(1)
                self.assertNotIn("archive", receipt)

    def test_expired_capture_fails_without_qualification(self):
        with patch.object(HELPER, "CAPTURE_SECONDS", -1):
            _, receipt = self.capture(1)
        self.assertIn("90-second budget", receipt["error"])

    def test_input_mutation_during_capture_is_not_a_complete_archive(self):
        original = HELPER.inventory
        count = 0

        def mutate(*args):
            nonlocal count
            count += 1
            if count == 3:
                (self.args.app_directory / HELPER.APP).write_bytes(b"mutated fixture")
            return original(*args)

        with patch.object(HELPER, "inventory", side_effect=mutate):
            _, receipt = self.capture(1)
        self.assertIn("changed", receipt["error"])

    def test_source_identity_mutation_is_not_a_complete_archive(self):
        count = 0

        def git(repo, *args, deadline):
            nonlocal count
            if args == ("rev-parse", "HEAD"):
                count += 1
                return ("a" if count == 1 else "b") * 40
            return ""

        with patch.object(self, "git", side_effect=git):
            _, receipt = self.capture(1)
        self.assertIn("Source identity changed", receipt["error"])

    def test_short_output_write_is_rejected(self):
        class ShortWriter:
            def write(self, data):
                return len(data) - 1

        with self.assertRaisesRegex(OSError, "Short archive write"):
            HELPER.BoundedWriter(ShortWriter(), time.monotonic() + 1).write(b"bytes")

    def test_git_uses_remaining_capture_budget_and_never_starts_after_expiry(self):
        with patch.object(HELPER.time, "monotonic", return_value=10), \
                patch.object(HELPER.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "head\n")) as run:
            self.assertEqual(HELPER.git(self.repo, "rev-parse", "HEAD", deadline=12), "head")
            self.assertEqual(run.call_args.kwargs["timeout"], 2)
            run.reset_mock()
            with self.assertRaisesRegex(RuntimeError, "budget"):
                HELPER.git(self.repo, "rev-parse", "HEAD", deadline=9)
            run.assert_not_called()
        with patch.object(HELPER.time, "monotonic", side_effect=[10, 13]), \
                patch.object(HELPER.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "head\n")):
            with self.assertRaisesRegex(RuntimeError, "budget"):
                HELPER.git(self.repo, "rev-parse", "HEAD", deadline=12)

    def test_unreadable_subtree_is_not_silently_omitted(self):
        def denied(root, *, followlinks, onerror):
            self.assertFalse(followlinks)
            onerror(PermissionError("controlled unreadable child directory"))
            return []

        with patch.object(HELPER.os, "walk", side_effect=denied):
            _, receipt = self.capture(1)
        self.assertIn("PermissionError", receipt["error"])
        self.assertNotIn("archive", receipt)

    def test_ci_failure_collection_is_separate_from_successful_package_upload(self):
        workflow = (ROOT / ".github/workflows/progpu-wpf-sdk.yml").read_text()
        gate = workflow.split("      - name: Run LibreWPF SDK gate\n", 1)[1]
        self.assertIn("id: sdk-gate", gate.split("      - name:", 1)[0])
        capture = gate.split("      - name: Preserve failed Showcase app closure\n", 1)[1].split("      - name:", 1)[0]
        self.assertIn("if: ${{ failure() && steps.sdk-gate.outcome == 'failure' }}", capture)
        self.assertIn("timeout-minutes: 2", capture)
        self.assertIn("progpu-wpf-preserve-showcase.py", capture)
        self.assertNotIn("continue-on-error", capture)
        upload = gate.split("      - name: Upload failed Showcase diagnostic closure\n", 1)[1].split("      - name:", 1)[0]
        self.assertIn("if: always()", upload)
        self.assertIn("showcase-failure-diagnostics-${{ env.PROGPU_WPF_QUALIFIED_COMMIT }}", upload)
        self.assertIn("artifacts/showcase-failure/**", upload)
        packages = gate.split("      - name: Upload CI package bundle\n", 1)[1].split("\n  canonical-sdk-consumer:", 1)[0]
        self.assertNotIn("always()", packages)
        self.assertNotIn("showcase-failure", packages)


if __name__ == "__main__":
    unittest.main()
