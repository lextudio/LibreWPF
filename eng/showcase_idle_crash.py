"""CI-only, unique-image Windows WER minidumps. No native calls at import time.

Uses the documented LocalDumps per-image key, never AeDebug/global WER settings.
Raw dumps stay outside uploaded evidence until their header, PID, type and size
are checked. MiniDumpNormal contains stacks; it is not a full-memory/env capture.
"""

import ctypes
import hashlib
from pathlib import Path
import shutil
import struct
import tempfile
import uuid


MAX_DUMP_BYTES = 32 * 1024 * 1024
PREFIX = "SOFTWARE\\Microsoft\\Windows\\Windows Error Reporting\\LocalDumps\\"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_dump(path):
    size = path.stat().st_size
    if path.is_symlink() or not path.is_file() or not 32 <= size <= MAX_DUMP_BYTES:
        raise ValueError("Crash dump is not a bounded regular minidump")
    with path.open("rb") as stream:
        header = stream.read(32)
        signature, version, count, table, _, _, flags = struct.unpack("<IIIIIIQ", header)
        if signature != 0x504D444D or flags != 0 or not 1 <= count <= 128 or table < 32 or table + count * 12 > size:
            raise ValueError("Invalid/non-MiniDumpNormal crash dump")
        stream.seek(table)
        entries = [struct.unpack("<III", stream.read(12)) for _ in range(count)]
    if any(offset + length > size for _, length, offset in entries):
        raise ValueError("Truncated crash dump stream")
    if sum(kind == 6 and length >= 168 for kind, length, _ in entries) != 1:
        raise ValueError("Crash dump has no unique exception stream")
    if any(kind == 9 for kind, _, _ in entries):
        raise ValueError("Full-memory stream is not admitted")
    return dict(bytes=size, sha256=digest(path), dumpFlags=flags, streamCount=count)


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
        self.winreg.SetValueEx(handle, "DumpType", 0, self.winreg.REG_DWORD, 1)

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
        report.update(validate_dump(expected))
        destination = self.evidence / "native-crash.dmp"
        with expected.open("rb") as source, destination.open("xb") as output:
            shutil.copyfileobj(source, output)
        if digest(destination) != report["sha256"]:
            raise ValueError("Retained crash dump bytes changed")
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
