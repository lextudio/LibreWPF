"""CI-only, unique-image Windows WER minidumps. No native calls at import time.

Uses the documented LocalDumps per-image key, never AeDebug/global WER settings.
Raw dumps stay outside uploaded evidence until their header, PID, type and size
are checked. MiniDumpNormal contains stacks; it is not a full-memory/env capture.
"""

import ctypes
import hashlib
import os
from pathlib import Path
import struct
import tempfile
import uuid


MAX_DUMP_BYTES = 32 * 1024 * 1024
AVX_CONTEXT_FLAG = 0x00200000  # MiniDumpWithAvxXStateContext: CPU registers only.
PREFIX = "SOFTWARE\\Microsoft\\Windows\\Windows Error Reporting\\LocalDumps\\"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def bounded_dump_digest(path, expected_size):
    checksum = hashlib.sha256()
    count = 0
    with path.open("rb") as stream:
        while chunk := stream.read(min(1024 * 1024, expected_size - count + 1)):
            count += len(chunk)
            if count > expected_size or count > MAX_DUMP_BYTES:
                raise ValueError("Crash dump grew during validation")
            checksum.update(chunk)
    if count != expected_size:
        raise ValueError("Crash dump shrank during validation")
    return checksum.hexdigest()


def validate_dump(path, expected_pid, *, snapshot_machine=None):
    size = path.stat().st_size
    if path.is_symlink() or not path.is_file() or not 32 <= size <= MAX_DUMP_BYTES:
        raise ValueError("Crash dump is not a bounded regular minidump")
    with path.open("rb") as stream:
        header = stream.read(32)
        signature, version, count, table, _, _, flags = struct.unpack("<IIIIIIQ", header)
        if signature != 0x504D444D or version & 0xFFFF != 0xA793 or flags not in (0, AVX_CONTEXT_FLAG) or not 1 <= count <= 128 or table < 32 or table + count * 12 > size:
            # Fixed-size structural evidence only; never log process memory.
            raise ValueError(f"Invalid/non-MiniDumpNormal crash dump: signature=0x{signature:08x}, "
                f"version=0x{version:08x}, flags=0x{flags:016x}, streams={count}, table={table}, bytes={size}")
        stream.seek(table)
        entries = [struct.unpack("<III", stream.read(12)) for _ in range(count)]
    if any(offset + length > size for _, length, offset in entries):
        raise ValueError("Truncated crash dump stream")
    exceptions = [length for kind, length, _ in entries if kind == 6]
    if snapshot_machine is None:
        if len(exceptions) != 1 or exceptions[0] < 168:
            raise ValueError("Crash dump has no unique exception stream")
    elif exceptions or snapshot_machine not in (0xAA64, 0x8664):
        raise ValueError("Live snapshot must be exception-free and architecture-qualified")
    else:
        def unique(kind, minimum):
            found = [(length, offset) for entry_kind, length, offset in entries if entry_kind == kind]
            if len(found) != 1 or found[0][0] < minimum or found[0][1] < table + count * 12:
                raise ValueError("Live snapshot is missing a complete unique structural stream")
            return found[0]
        _, system_offset = unique(7, 56)
        with path.open("rb") as stream:
            stream.seek(system_offset)
            architecture, = struct.unpack("<H", stream.read(2))
            if architecture != {0xAA64: 12, 0x8664: 9}[snapshot_machine]:
                raise ValueError("Live snapshot intrinsic architecture does not match its child")
            for kind, record_size in ((3, 48), (4, 108)):
                length, offset = unique(kind, 4)
                stream.seek(offset)
                records, = struct.unpack("<I", stream.read(4))
                if not 1 <= records <= 4096 or 4 + records * record_size > length:
                    raise ValueError("Live snapshot thread/module table is incomplete")
                if kind == 3:
                    for _ in range(records):
                        record = stream.read(record_size)
                        context_size, context_offset = struct.unpack_from("<II", record, 40)
                        if context_size == 0 or context_offset < table + count * 12 or context_offset + context_size > size:
                            raise ValueError("Live snapshot contains an invalid thread context")
    if any(kind == 9 for kind, _, _ in entries):
        raise ValueError("Full-memory stream is not admitted")
    if flags == AVX_CONTEXT_FLAG:
        # Windows x64 DbgHelp can add register-state metadata even when the
        # writer requests MiniDumpNormal. Prove the intrinsic architecture;
        # never admit additional memory flags or infer it from a filename.
        systems = [(length, offset) for kind, length, offset in entries if kind == 7]
        if len(systems) != 1 or systems[0][0] < 56 or systems[0][1] < table + count * 12:
            raise ValueError("AVX register dump requires one complete system-info stream")
        with path.open("rb") as stream:
            stream.seek(systems[0][1])
            architecture, = struct.unpack("<H", stream.read(2))
        if architecture != 9:  # PROCESSOR_ARCHITECTURE_AMD64, not a PE machine ID.
            raise ValueError("AVX register dump requires intrinsic x64 architecture")
    identity = [(length, offset) for kind, length, offset in entries if kind == 15]
    if len(identity) != 1 or identity[0][0] < 24:
        raise ValueError("Crash dump has no unique process identity stream")
    length, offset = identity[0]
    with path.open("rb") as stream:
        stream.seek(offset)
        info_size, valid, pid = struct.unpack("<III", stream.read(12))
    if not 24 <= info_size <= length or not valid & 1 or pid != expected_pid:
        raise ValueError("Crash dump process identity does not match the owned child")
    return dict(bytes=size, sha256=bounded_dump_digest(path, size), dumpFlags=flags,
                streamCount=count, processId=pid)


def publish_dump(source, destination, expected_pid, report, *, snapshot_machine=None):
    # Stage outside the artifact tree, bounding actual reads, not only stat.
    # Link publication is atomic and fails if destination exists or volumes differ.
    staged = source.with_name(f"validated-{uuid.uuid4().hex}.dmp")
    owned = False
    try:
        with source.open("rb") as incoming, staged.open("xb") as output:
            owned = True
            copied = 0
            while chunk := incoming.read(min(1024 * 1024, MAX_DUMP_BYTES - copied + 1)):
                copied += len(chunk)
                if copied > MAX_DUMP_BYTES or copied > report["bytes"]:
                    raise ValueError("Crash dump grew beyond its validated byte budget")
                output.write(chunk)
        if validate_dump(staged, expected_pid, snapshot_machine=snapshot_machine) != report:
            raise ValueError("Retained crash dump bytes changed")
        os.link(staged, destination)  # Never overwrite caller-owned evidence.
    finally:
        if owned:
            staged.unlink()


class WindowsRegistry:
    def __init__(self):
        import winreg
        from ctypes import wintypes
        self.winreg = winreg
        self.api = ctypes.WinDLL("advapi32", use_last_error=True)
        self.api.RegCreateKeyExW.argtypes = [wintypes.HANDLE, wintypes.LPCWSTR, wintypes.DWORD,
            wintypes.LPWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
            ctypes.POINTER(wintypes.HANDLE), ctypes.POINTER(wintypes.DWORD)]
        self.api.RegCreateKeyExW.restype = wintypes.LONG

    def create_new(self, name):
        from ctypes import wintypes
        handle, disposition = wintypes.HANDLE(), wintypes.DWORD()
        status = self.api.RegCreateKeyExW(wintypes.HANDLE(-2147483646), name, 0, None, 0,
            self.winreg.KEY_SET_VALUE | self.winreg.KEY_QUERY_VALUE | self.winreg.KEY_WOW64_64KEY,
            None, ctypes.byref(handle), ctypes.byref(disposition))
        if status:
            raise ctypes.WinError(status)
        if disposition.value != 1:
            self.winreg.CloseKey(handle.value)
            raise FileExistsError("Refusing existing per-image WER configuration")
        return handle.value

    def configure(self, handle, directory):
        self.winreg.SetValueEx(handle, "DumpFolder", 0, self.winreg.REG_EXPAND_SZ, str(directory))
        self.winreg.SetValueEx(handle, "DumpCount", 0, self.winreg.REG_DWORD, 1)
        # Explicit MiniDumpNormal; DumpType=1 does not document exact flags.
        self.winreg.SetValueEx(handle, "DumpType", 0, self.winreg.REG_DWORD, 0)
        self.winreg.SetValueEx(handle, "CustomDumpFlags", 0, self.winreg.REG_DWORD, 0)

    def close(self, handle):
        self.winreg.CloseKey(handle)

    def remove(self, name):
        self.winreg.DeleteKeyEx(self.winreg.HKEY_LOCAL_MACHINE, name, self.winreg.KEY_WOW64_64KEY)


class Capture:
    def __init__(self, app, evidence, registry):
        self.app, self.evidence, self.registry = app, evidence, registry
        self.image = app.with_name(f"ShowcaseIdle-{uuid.uuid4().hex}.exe")
        self.key = PREFIX + self.image.name
        self.dump_directory = None
        self.key_owned = False
        self.image_owned = False
        self.original_hash = digest(app)

    def prepare(self):
        # SDK apphost embeds its managed entrypoint. Renaming is permitted only
        # with that original binding present and the entire PE bytes unchanged.
        data = self.app.read_bytes()
        if not data.startswith(b"MZ") or data.count(b"ProGPU.Wpf.ShowcaseApp.dll\0") != 1:
            raise ValueError("Cannot prove the original Showcase apphost DLL binding")
        with self.image.open("xb") as stream:
            self.image_owned = True
            stream.write(data)
        if digest(self.image) != self.original_hash:
            raise ValueError("Unique crash image differs from the original apphost")
        # Never place unreviewed/raw dumps under the workflow upload glob.
        self.dump_directory = Path(tempfile.mkdtemp(prefix="showcase-idle-raw-dump-"))
        handle = self.registry.create_new(self.key)
        self.key_owned = True
        try:
            self.registry.configure(handle, self.dump_directory)
        finally:
            self.registry.close(handle)
        return self.image

    def collect(self, pid, exit_code, timed_out):
        report = dict(kind="WER-MiniDumpNormal", apphostSha256=self.original_hash,
                      imageName=self.image.name, rawDirectory=str(self.dump_directory), captured=False)
        if digest(self.app) != self.original_hash or digest(self.image) != self.original_hash:
            raise ValueError("Crash apphost bytes changed")
        if timed_out or not exit_code or pid is None:
            return report
        files = list(self.dump_directory.iterdir())
        expected = self.dump_directory / f"{self.image.name}.{pid}.dmp"
        if not files:
            report["reason"] = "No WER dump was produced; crash stack remains unavailable"
            return report
        if files != [expected] or not expected.is_file():
            raise ValueError("Unexpected files/PID in the task-owned raw dump directory")
        validated = validate_dump(expected, pid)
        destination = self.evidence / "native-crash.dmp"
        publish_dump(expected, destination, pid, validated)
        report.update(validated)
        report.update(captured=True, path=destination.name, processId=pid)
        return report

    def close(self):
        errors = []
        if self.key_owned:
            try:
                self.registry.remove(self.key)
                self.key_owned = False
            except OSError as error:
                errors.append(f"Owned WER key cleanup: {error}")
        if self.image_owned:
            try:
                self.image.unlink()
                self.image_owned = False
            except OSError as error:
                errors.append(f"Owned crash apphost cleanup: {error}")
        # Raw files remain outside the artifact tree; only validated stacks are
        # copied. No recursive removal or unrelated registry/image cleanup.
        return errors
