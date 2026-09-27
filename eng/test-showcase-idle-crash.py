"""Offline policy/ownership controls; synthetic PE/dumps are never executed."""
from pathlib import Path
import struct
import tempfile
import unittest
from unittest import mock

import showcase_idle_crash as crash


class Registry:
    def __init__(self):
        self.keys = set()
        self.removed = []
        self.closed = []
        self.configured = []

    def create_new(self, name):
        if name in self.keys:
            raise FileExistsError("existing caller key")
        self.keys.add(name)
        return name

    def configure(self, handle, directory):
        self.configured.append((handle, directory))

    def close(self, handle):
        self.closed.append(handle)

    def remove(self, name):
        self.keys.remove(name)
        self.removed.append(name)


def dump(flags=0, kind=6, length=168, pid=42, valid=1, version=0xA793, identity_kind=15):
    return (struct.pack("<IIIIIIQ", 0x504D444D, version, 2, 32, 0, 0, flags)
        + struct.pack("<III", kind, length, 56) + struct.pack("<III", identity_kind, 24, 224)
        + bytes(168) + struct.pack("<IIIIII", 24, valid, pid, 0, 0, 0))


class CrashControls(unittest.TestCase):
    def test_exact_owned_registry_values_request_only_normal_stacks(self):
        registry = object.__new__(crash.WindowsRegistry)  # No DLL or registry access.
        registry.winreg = mock.Mock(REG_EXPAND_SZ=2, REG_DWORD=4)
        registry.configure(123, Path("owned-raw-directory"))
        self.assertEqual(registry.winreg.SetValueEx.call_args_list, [
            mock.call(123, "DumpFolder", 0, 2, "owned-raw-directory"),
            mock.call(123, "DumpCount", 0, 4, 1),
            mock.call(123, "DumpType", 0, 4, 0),
            mock.call(123, "CustomDumpFlags", 0, 4, 0),
        ])

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.app = self.root / "ProGPU.Wpf.ShowcaseApp.exe"
        self.app.write_bytes(b"MZsynthetic-not-executable-ProGPU.Wpf.ShowcaseApp.dll\0")
        self.evidence = self.root / "evidence"
        self.evidence.mkdir()
        self.registry = Registry()
        self.capture = crash.Capture(self.app, self.evidence, self.registry)
        self.addCleanup(self.capture.close)
        self.patch = mock.patch.object(crash.tempfile, "mkdtemp", return_value=str(self.root / "raw"))
        self.patch.start()
        self.addCleanup(self.patch.stop)
        (self.root / "raw").mkdir()

    def test_unique_image_keeps_original_bytes_binding_and_only_owned_cleanup(self):
        original = self.app.read_bytes()
        image = self.capture.prepare()
        self.assertEqual(image.read_bytes(), original)
        self.assertNotEqual(image.name, self.app.name)
        self.assertEqual(self.registry.configured, [(self.capture.key, self.root / "raw")])
        self.assertEqual(self.capture.close(), [])
        self.assertEqual(self.registry.removed, [self.capture.key])
        self.assertEqual(self.app.read_bytes(), original)
        self.assertFalse(image.exists())

    def test_existing_key_is_not_written_or_deleted(self):
        self.registry.keys.add(self.capture.key)
        with self.assertRaises(FileExistsError):
            self.capture.prepare()
        self.capture.close()
        self.assertEqual(self.registry.keys, {self.capture.key})
        self.assertEqual(self.registry.configured, [])
        self.assertEqual(self.registry.removed, [])

    def test_configuration_failure_closes_handle_and_removes_owned_key(self):
        with mock.patch.object(self.registry, "configure", side_effect=OSError("denied")):
            with self.assertRaises(OSError):
                self.capture.prepare()
        self.capture.close()
        self.assertEqual(self.registry.closed, [self.capture.key])
        self.assertEqual(self.registry.removed, [self.capture.key])

    def test_unknown_embedded_binding_fails_before_key_or_copy(self):
        self.app.write_bytes(b"MZanother-entrypoint.dll\0")
        with self.assertRaisesRegex(ValueError, "DLL binding"):
            self.capture.prepare()
        self.assertEqual(self.registry.keys, set())
        self.assertFalse(self.capture.image.exists())

    def test_only_matching_pid_minidump_is_admitted(self):
        self.capture.prepare()
        path = self.root / "raw" / f"{self.capture.image.name}.42.dmp"
        path.write_bytes(dump())
        report = self.capture.collect(42, 0xC0000005, False)
        self.assertTrue(report["captured"])
        self.assertEqual(report["sha256"], crash.digest(path))
        self.assertEqual((self.evidence / "native-crash.dmp").read_bytes(), path.read_bytes())

    def test_wrong_pid_and_extra_files_are_not_uploaded(self):
        self.capture.prepare()
        (self.root / "raw" / "another.exe.99.dmp").write_bytes(dump())
        with self.assertRaisesRegex(ValueError, "files/PID"):
            self.capture.collect(42, 7, False)
        self.assertEqual(list(self.evidence.iterdir()), [])

    def test_full_memory_missing_exception_truncation_and_oversize_reject(self):
        path = self.root / "bad.dmp"
        duplicate_exception = (struct.pack("<IIIIIIQ", 0x504D444D, 0xA793, 3, 32, 0, 0, 0)
            + struct.pack("<III", 6, 168, 68) + struct.pack("<III", 15, 24, 236)
            + struct.pack("<III", 6, 0, 260) + bytes(168)
            + struct.pack("<IIIIII", 24, 1, 42, 0, 0, 0))
        for data in (dump(flags=2), dump(flags=1), dump(kind=9), dump(length=1000), b"MDMP", duplicate_exception):
            with self.subTest(data=data[:32]):
                path.write_bytes(data)
                with self.assertRaises(ValueError):
                    crash.validate_dump(path, 42)
        path.write_bytes(dump())
        with mock.patch.object(crash, "MAX_DUMP_BYTES", 64):
            with self.assertRaises(ValueError):
                crash.validate_dump(path, 42)

    def test_intrinsic_pid_validity_and_format_version_are_required(self):
        path = self.root / "identity.dmp"
        for data in (dump(pid=99), dump(valid=0), dump(version=0xA794),
                     dump(identity_kind=0), dump(kind=15)):
            with self.subTest(data=data[:56]):
                path.write_bytes(data)
                with self.assertRaises(ValueError):
                    crash.validate_dump(path, 42)
        path.write_bytes(dump(version=0x1234A793))
        self.assertEqual(42, crash.validate_dump(path, 42)["processId"])

    def test_rejected_header_reports_only_bounded_structure(self):
        path = self.root / "bad-header.dmp"
        path.write_bytes(dump(flags=2) + b"PRIVATE-PROCESS-MEMORY")
        with self.assertRaises(ValueError) as error:
            crash.validate_dump(path, 42)
        message = str(error.exception)
        self.assertIn("flags=0x0000000000000002", message)
        self.assertIn("signature=0x504d444d", message)
        self.assertIn("streams=2, table=32", message)
        self.assertNotIn("PRIVATE", message)
        self.assertLess(len(message), 256)

    def test_dump_hash_reads_are_bounded_and_require_unchanged_length(self):
        path = self.root / "raw" / "hash.dmp"
        path.write_bytes(dump())
        size = path.stat().st_size
        self.assertEqual(crash.digest(path), crash.bounded_dump_digest(path, size))
        for wrong in (size - 1, size + 1):
            with self.assertRaises(ValueError):
                crash.bounded_dump_digest(path, wrong)

    def test_growing_or_changed_copy_never_enters_uploaded_evidence(self):
        path = self.root / "raw" / "source.dmp"
        path.write_bytes(dump())
        report = crash.validate_dump(path, 42)
        for data in (dump() + b"growth", dump(pid=99)):
            path.write_bytes(data)
            with self.assertRaises(ValueError):
                crash.publish_dump(path, self.evidence / "native-crash.dmp", 42, report)
            self.assertEqual(list(self.evidence.iterdir()), [])
            self.assertEqual(list((self.root / "raw").iterdir()), [path])

    def test_publication_failure_or_existing_destination_is_preserved(self):
        path = self.root / "raw" / "source.dmp"
        path.write_bytes(dump())
        report = crash.validate_dump(path, 42)
        destination = self.evidence / "native-crash.dmp"
        with mock.patch.object(crash.os, "link", side_effect=OSError("cross-device")):
            with self.assertRaises(OSError):
                crash.publish_dump(path, destination, 42, report)
        self.assertFalse(destination.exists())
        destination.write_bytes(b"caller-owned")
        with self.assertRaises(FileExistsError):
            crash.publish_dump(path, destination, 42, report)
        self.assertEqual(destination.read_bytes(), b"caller-owned")
        self.assertEqual(list((self.root / "raw").iterdir()), [path])

    def test_success_timeout_or_missing_pid_do_not_admit_a_dump(self):
        self.capture.prepare()
        for pid, code, timeout in ((42, 0, False), (42, 7, True), (None, 7, False)):
            self.assertFalse(self.capture.collect(pid, code, timeout)["captured"])
        self.assertEqual(list(self.evidence.iterdir()), [])

    def test_no_dump_remains_explicitly_unqualified(self):
        self.capture.prepare()
        report = self.capture.collect(42, 0xC0000005, False)
        self.assertFalse(report["captured"])
        self.assertIn("unavailable", report["reason"])

    def test_cleanup_failure_is_reported_and_not_replaced_by_broad_deletion(self):
        self.capture.prepare()
        with mock.patch.object(self.registry, "remove", side_effect=OSError("owned-key-denied")):
            errors = self.capture.close()
        self.assertEqual(len(errors), 1)
        self.assertIn("owned-key-denied", errors[0])
        self.assertIn(self.capture.key, self.registry.keys)
        self.assertFalse(self.capture.image.exists())


if __name__ == "__main__":
    unittest.main()
