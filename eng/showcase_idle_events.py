"""Failure-only, bounded, read-only Application Error evidence for the owned CI child.

Never a crash oracle: absent/late/unsupported records leave the original exit intact.
No WER 1001 admission by filename alone (its usual schema lacks a faulting PID).
"""
from __future__ import annotations

import ctypes
from datetime import datetime, timezone
import json
import ntpath
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET


MAX_RECORDS = 16
MAX_XML_BYTES = 64 * 1024
MAX_QUERY_BYTES = 256 * 1024
MAX_RECEIPT_BYTES = 64 * 1024
QUERY_TIMEOUT_SECONDS = 5
NS = "{http://schemas.microsoft.com/win/2004/08/events/event}"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="microseconds").replace("+00:00", "Z")


def instant(value: str) -> tuple[datetime, int]:
    match = re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.(\d{1,7}))?Z", value) if isinstance(value, str) else None
    if match is None:
        raise ValueError("Expected UTC event boundary")
    # Event timestamps retain 100ns precision. datetime alone truncates the
    # seventh fractional digit and can admit an event just beyond a boundary.
    whole_second = datetime.fromisoformat(value[:-1] + "+00:00").replace(microsecond=0)
    return whole_second, int((match.group(1) or "").ljust(7, "0"))


def validate_identity(image: str, pid: int, start: str, end: str) -> None:
    if (not ntpath.isabs(image) or not re.fullmatch(r"ShowcaseIdle-[0-9a-f]{32}\.exe", ntpath.basename(image))
            or type(pid) is not int or not 0 < pid <= 0xffffffff or instant(end) < instant(start)):
        raise ValueError("Invalid owned child event identity")


def event_record(xml: str, image: str, pid: int, start: str, end: str) -> dict | None:
    """Admit only the faulting process fields, never the event logger's Execution PID."""
    if len(xml.encode("utf-16-le")) > MAX_XML_BYTES or "<!DOCTYPE" in xml or "<!ENTITY" in xml:
        raise ValueError("Unsupported event XML")
    root = ET.fromstring(xml)
    system = root.find(f"{NS}System")
    if root.tag != f"{NS}Event" or system is None:
        return None
    provider = system.find(f"{NS}Provider")
    created = system.find(f"{NS}TimeCreated")
    if (provider is None or provider.get("Name") != "Application Error"
            or system.findtext(f"{NS}EventID") != "1000"
            or system.findtext(f"{NS}Channel") != "Application" or created is None):
        return None
    timestamp = created.get("SystemTime", "")
    if not instant(start) <= instant(timestamp) <= instant(end):
        return None
    data = {}
    for entry in root.findall(f"{NS}EventData/{NS}Data"):
        name = entry.get("Name")
        if not name or name in data or len(entry.text or "") > 2048:
            raise ValueError("Unsupported or duplicate event field")
        data[name] = entry.text or ""
    fault_pid = data.get("ProcessId", "")
    if not re.fullmatch(r"(?:0x[0-9a-fA-F]+|[0-9]+)", fault_pid):
        return None
    if (int(fault_pid, 16 if fault_pid.startswith("0x") else 10) != pid
            or ntpath.normcase(data.get("AppPath", "")) != ntpath.normcase(image)
            or ntpath.normcase(data.get("AppName", "")) != ntpath.normcase(ntpath.basename(image))):
        return None
    return {"provider": "Application Error", "eventId": 1000,
            "recordId": system.findtext(f"{NS}EventRecordID"), "timeCreatedUtc": timestamp,
            "appPath": data["AppPath"], "processId": pid,
            **{name: data.get(name) for name in ("ModuleName", "ModulePath", "ExceptionCode",
                "FaultingOffset", "ProcessCreationTime", "IntegratorReportId")}}


class EventLog:
    """Local winevt API only. All handles stay on this thread and are closed."""
    def __init__(self):
        if os.name != "nt":
            raise OSError("Windows event API unavailable")
        self.api = ctypes.WinDLL("wevtapi", use_last_error=True)
        handle, dword, boolean = ctypes.c_void_p, ctypes.c_uint32, ctypes.c_int32
        self.api.EvtQuery.argtypes = [handle, ctypes.c_wchar_p, ctypes.c_wchar_p, dword]
        self.api.EvtQuery.restype = handle
        self.api.EvtNext.argtypes = [handle, dword, ctypes.POINTER(handle), dword, dword, ctypes.POINTER(dword)]
        self.api.EvtNext.restype = boolean
        self.api.EvtRender.argtypes = [handle, handle, dword, dword, handle, ctypes.POINTER(dword), ctypes.POINTER(dword)]
        self.api.EvtRender.restype = boolean
        self.api.EvtClose.argtypes = [handle]
        self.api.EvtClose.restype = boolean

    def close(self, handle):
        if not self.api.EvtClose(handle):
            raise ctypes.WinError(ctypes.get_last_error())

    def render(self, handle) -> str:
        used, count = ctypes.c_uint32(), ctypes.c_uint32()
        if self.api.EvtRender(None, handle, 1, 0, None, ctypes.byref(used), ctypes.byref(count)):
            raise ValueError("Unexpected empty event rendering")
        if ctypes.get_last_error() != 122:  # ERROR_INSUFFICIENT_BUFFER
            raise ctypes.WinError(ctypes.get_last_error())
        if used.value < 2 or used.value > MAX_XML_BYTES or used.value % 2:
            raise ValueError("Event exceeds XML byte budget")
        buffer = ctypes.create_string_buffer(used.value)
        if not self.api.EvtRender(None, handle, 1, len(buffer), buffer, ctypes.byref(used), ctypes.byref(count)):
            raise ctypes.WinError(ctypes.get_last_error())
        if used.value < 2 or used.value > len(buffer) or used.value % 2 or buffer.raw[used.value-2:used.value] != b"\0\0":
            raise ValueError("Malformed event string boundary")
        return buffer.raw[:used.value-2].decode("utf-16-le", errors="strict")

    def query(self, image: str, start: str, end: str):
        # Identity and timestamps are validated before constructing this XPath.
        expression = ("*[System[Provider[@Name='Application Error'] and EventID=1000 and "
            f"TimeCreated[@SystemTime>='{start}' and @SystemTime<='{end}']] and "
            f"EventData[Data[@Name='AppName']='{ntpath.basename(image)}']]")
        query = self.api.EvtQuery(None, "Application", expression, 0x201)  # Channel + reverse direction.
        if not query:
            raise ctypes.WinError(ctypes.get_last_error())
        total = 0
        try:
            for _ in range(MAX_RECORDS):
                event = ctypes.c_void_p()
                returned = ctypes.c_uint32()
                if not self.api.EvtNext(query, 1, ctypes.byref(event), 100, 0, ctypes.byref(returned)):
                    if ctypes.get_last_error() == 259:  # ERROR_NO_MORE_ITEMS
                        return
                    raise ctypes.WinError(ctypes.get_last_error())
                try:
                    if returned.value != 1 or not event.value:
                        raise ValueError("Invalid event handle count")
                    xml = self.render(event)
                    total += len(xml.encode("utf-16-le"))
                    if total > MAX_QUERY_BYTES:
                        raise ValueError("Query exceeds XML byte budget")
                    yield xml
                finally:
                    if event.value:
                        self.close(event)
        finally:
            self.close(query)


def query_records(image: str, pid: int, start: str, end: str, api=None) -> dict:
    validate_identity(image, pid, start, end)
    records, examined, rejected = [], 0, 0
    source = (api or EventLog()).query(image, start, end)
    try:
        for xml in source:
            examined += 1
            try:
                record = event_record(xml, image, pid, start, end)
            except (ValueError, ET.ParseError):
                record = None
            if record is not None:
                records.append(record)
            else:
                rejected += 1
    finally:
        source.close()
    return {"schemaVersion": 1, "records": records, "examined": examined, "rejected": rejected,
            "recordLimitReached": examined == MAX_RECORDS,
            "reason": None if records else "No currently available, exactly correlated Application Error record",
            "wer1001": "Not collected: filename-only WER records do not prove the owned faulting PID"}


def collect(image: str, outcome: dict, directory: Path) -> dict:
    """No query on success, timeout, or missing owned identity. Never changes child exit."""
    if outcome.get("childExitCode") in (None, 0) or outcome.get("timedOut"):
        return {"collected": False, "reason": "No non-timeout child failure"}
    pid, start, end = outcome.get("childProcessId"), outcome.get("childLaunchUtc"), utc_now()
    validate_identity(image, pid, start, end)
    path = directory / "windows-application-events.json"
    # The known helper emits no stdout/stderr, spawns no descendants and caps its
    # one fresh receipt. A separate process also bounds a blocked local EvtQuery.
    command = [sys.executable, str(Path(__file__).resolve()), image, str(pid), start, end, str(path)]
    try:
        completed = subprocess.run(command, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL, timeout=QUERY_TIMEOUT_SECONDS, check=False)
    except subprocess.TimeoutExpired:
        return {"collected": False, "reason": "Read-only event query exceeded five seconds", "queryEndUtc": end}
    if completed.returncode != 0 or not path.is_file() or path.is_symlink():
        return {"collected": False, "reason": "Read-only event query failed", "helperExitCode": completed.returncode,
                "queryEndUtc": end}
    with path.open("rb") as stream:
        encoded = stream.read(MAX_RECEIPT_BYTES + 1)
    if len(encoded) > MAX_RECEIPT_BYTES:
        raise ValueError("Event receipt exceeds byte budget")
    receipt = json.loads(encoded)
    return {"collected": bool(receipt["records"]), "path": path.name, "queryStartUtc": start,
            "queryEndUtc": end, "details": receipt}


def main() -> int:
    try:
        image, pid, start, end, output = sys.argv[1:]
        result = query_records(image, int(pid), start, end)
        encoded = json.dumps(result, allow_nan=False).encode("utf-8")
        if len(encoded) > MAX_RECEIPT_BYTES:
            return 1
        with Path(output).open("xb") as stream:
            stream.write(encoded)
        return 0
    except Exception:
        # No raw/unmatched event bodies, machine names, or unrelated paths leak
        # through diagnostic errors. Parent records absence and exact helper exit.
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
