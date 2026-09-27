"""Offline event/XML/native-ABI controls; never query a real Windows event log."""
import ctypes
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

import showcase_idle_events as events


IMAGE = r"C:\owned\ShowcaseIdle-0123456789abcdef0123456789abcdef.exe"
START, END = "2026-09-27T10:00:00.000000Z", "2026-09-27T10:01:00.000000Z"
XML = f'''<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
<System><Provider Name="Application Error"/><EventID>1000</EventID>
<TimeCreated SystemTime="2026-09-27T10:00:30.1234567Z"/><EventRecordID>42</EventRecordID>
<Execution ProcessID="999"/><Channel>Application</Channel><Computer>do-not-retain</Computer></System>
<EventData><Data Name="AppName">{events.ntpath.basename(IMAGE)}</Data>
<Data Name="AppPath">{IMAGE}</Data><Data Name="ProcessId">0x1ab0</Data>
<Data Name="ModuleName">fault.dll</Data><Data Name="FaultingOffset">0000000000012345</Data>
<Data Name="ExceptionCode">c0000409</Data><Data Name="Unrelated">do-not-retain</Data></EventData></Event>'''
PID = 0x1ab0


class EventControls(unittest.TestCase):
    def read(self, xml=XML):
        return events.event_record(xml, IMAGE, PID, START, END)

    def test_exact_faulting_identity_and_module_fields_only(self):
        result = self.read()
        self.assertEqual(PID, result["processId"])
        self.assertEqual("fault.dll", result["ModuleName"])
        self.assertEqual("0000000000012345", result["FaultingOffset"])
        self.assertEqual("c0000409", result["ExceptionCode"])
        self.assertNotIn("do-not-retain", json.dumps(result))

    def test_unrelated_provider_channel_pid_path_name_and_time_rejected(self):
        for old, new in (("Application Error", "Windows Error Reporting"), (">1000<", ">1001<"),
                (">Application<", ">System<"), ("0x1ab0", "999"), ("C:\\owned", "C:\\other"),
                (f'>{events.ntpath.basename(IMAGE)}<', '>other.exe<'),
                ("10:00:30.1234567Z", "09:59:59.9999999Z"), ("10:00:30.1234567Z", "10:01:00.000001Z")):
            with self.subTest(old=old):
                self.assertIsNone(self.read(XML.replace(old, new)))

    def test_execution_pid_is_not_faulting_process_identity(self):
        self.assertIsNone(self.read(XML.replace('Name="ProcessId"', 'Name="OtherId"').replace('ProcessID="999"', f'ProcessID="{PID}"')))

    def test_duplicate_oversized_entities_and_malformed_xml_rejected(self):
        for xml in (XML.replace("</EventData>", '<Data Name="ProcessId">0x1ab0</Data></EventData>'),
                XML.replace("fault.dll", "a" * 2049), "<!DOCTYPE Event>" + XML,
                "<!ENTITY x 'value'>" + XML, "a" * events.MAX_XML_BYTES, XML[:-8]):
            with self.subTest(length=len(xml)):
                with self.assertRaises((ValueError, events.ET.ParseError)):
                    self.read(xml)

    def test_query_retains_no_unmatched_raw_content(self):
        api = mock.Mock()
        api.query.side_effect = lambda *args: iter_generator([XML.replace("0x1ab0", "2"), "<broken>", XML])
        result = events.query_records(IMAGE, PID, START, END, api)
        self.assertEqual((3, 2, 1), (result["examined"], result["rejected"], len(result["records"])))
        self.assertNotIn("do-not-retain", json.dumps(result))
        self.assertIn("filename-only", result["wer1001"])

    def test_invalid_identity_never_queries(self):
        for image, pid, start, end in (("caller.exe", PID, START, END), (IMAGE, True, START, END),
                (IMAGE, 0, START, END), (IMAGE, PID, END, START), (IMAGE, PID, START.replace("Z", ""), END),
                (IMAGE, PID, START.replace("T", "'"), END)):
            api = mock.Mock()
            with self.assertRaises(ValueError):
                events.query_records(image, pid, start, end, api)
            api.query.assert_not_called()

    def test_unavailable_records_explicit_and_no_wer_filename_fallback(self):
        api = mock.Mock()
        api.query.side_effect = lambda *args: iter_generator([])
        result = events.query_records(IMAGE, PID, START, END, api)
        self.assertEqual([], result["records"])
        self.assertIn("No currently available", result["reason"])

    def test_ctypes_native_signatures_use_pointer_handles_and_32bit_dwords(self):
        api = mock.Mock()
        with mock.patch.object(events.os, "name", "nt"), mock.patch.object(ctypes, "WinDLL", return_value=api, create=True):
            events.EventLog()
        self.assertEqual(ctypes.c_void_p, api.EvtQuery.restype)
        self.assertEqual([ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint32], api.EvtQuery.argtypes)
        self.assertEqual(ctypes.c_uint32, api.EvtNext.argtypes[1])
        self.assertEqual(ctypes.c_uint32, api.EvtRender.argtypes[3])
        self.assertEqual([ctypes.c_void_p], api.EvtClose.argtypes)

    def test_query_closes_owned_handles_after_render_failure(self):
        reader = object.__new__(events.EventLog)
        reader.api = mock.Mock()
        reader.api.EvtQuery.return_value = 10
        def next_event(query, count, event, timeout, flags, returned):
            self.assertEqual((10, 1, 100, 0), (query, count, timeout, flags))
            event._obj.value, returned._obj.value = 11, 1
            return 1
        reader.api.EvtNext.side_effect = next_event
        reader.render = mock.Mock(side_effect=ValueError("render failure"))
        with self.assertRaises(ValueError):
            list(reader.query(IMAGE, START, END))
        self.assertEqual([11, 10], [c.args[0].value if hasattr(c.args[0], "value") else c.args[0]
                                   for c in reader.api.EvtClose.call_args_list])
        args = reader.api.EvtQuery.call_args.args
        self.assertEqual((None, "Application", 0x201), (args[0], args[1], args[3]))
        for value in (START, END, events.ntpath.basename(IMAGE), "EventID=1000"):
            self.assertIn(value, args[2])

    def test_query_bounded_records_and_close(self):
        reader = object.__new__(events.EventLog)
        reader.api = mock.Mock()
        reader.api.EvtQuery.return_value = 10
        def next_event(query, count, event, timeout, flags, returned):
            event._obj.value, returned._obj.value = 11, 1
            return 1
        reader.api.EvtNext.side_effect = next_event
        reader.render = mock.Mock(return_value=XML)
        result = events.query_records(IMAGE, PID, START, END, reader)
        self.assertTrue(result["recordLimitReached"])
        self.assertEqual(events.MAX_RECORDS, reader.api.EvtNext.call_count)
        self.assertEqual(events.MAX_RECORDS + 1, reader.api.EvtClose.call_count)

    def test_render_checks_bytes_before_allocation(self):
        reader = object.__new__(events.EventLog)
        reader.api = mock.Mock()
        for length in (0, 3, events.MAX_XML_BYTES + 2):
            def render(context, handle, flags, size, buffer, used, count):
                used._obj.value = length
                return 0
            reader.api.EvtRender.side_effect = render
            with mock.patch.object(ctypes, "get_last_error", return_value=122, create=True), \
                    mock.patch.object(ctypes, "create_string_buffer") as allocate:
                with self.assertRaises(ValueError):
                    reader.render(11)
                allocate.assert_not_called()

    def test_render_decodes_exact_utf16_buffer(self):
        reader = object.__new__(events.EventLog)
        reader.api = mock.Mock()
        encoded = XML.encode("utf-16-le") + b"\0\0"
        def render(context, handle, flags, size, buffer, used, count):
            used._obj.value = len(encoded)
            if not size:
                return 0
            ctypes.memmove(buffer, encoded, len(encoded))
            return 1
        reader.api.EvtRender.side_effect = render
        with mock.patch.object(ctypes, "get_last_error", return_value=122, create=True):
            self.assertEqual(XML, reader.render(11))

    def test_success_timeout_and_missing_child_do_not_launch_query(self):
        for code, timed_out in ((0, False), (None, False), (123, True)):
            with mock.patch.object(subprocess, "run") as run:
                result = events.collect(IMAGE, {"childExitCode": code, "timedOut": timed_out}, Path("unused"))
            self.assertFalse(result["collected"])
            run.assert_not_called()

    def test_event_helper_timeout_is_bounded_and_diagnostic_only(self):
        outcome = {"childExitCode": 3221226505, "childProcessId": PID, "childLaunchUtc": START}
        before = dict(outcome)
        with mock.patch.object(events, "utc_now", return_value=END), \
                mock.patch.object(subprocess, "run", side_effect=subprocess.TimeoutExpired("helper", 5)) as run:
            result = events.collect(IMAGE, outcome, Path("unused"))
        self.assertEqual(before, outcome)
        self.assertFalse(result["collected"])
        self.assertEqual(5, run.call_args.kwargs["timeout"])
        self.assertEqual(subprocess.DEVNULL, run.call_args.kwargs["stdout"])
        self.assertIn("five seconds", result["reason"])

    def test_matched_helper_result_is_retained_and_boundaries_recorded(self):
        outcome = {"childExitCode": 3221226505, "childProcessId": PID, "childLaunchUtc": START}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "windows-application-events.json").write_text(json.dumps({"records": [self.read()]}))
            with mock.patch.object(events, "utc_now", return_value=END), \
                    mock.patch.object(subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
                result = events.collect(IMAGE, outcome, root)
            self.assertTrue(result["collected"])
            self.assertEqual((START, END), (result["queryStartUtc"], result["queryEndUtc"]))

    def test_resize_checkpoints_surround_original_calls_outside_interval(self):
        root = Path(__file__).resolve().parents[1]
        source = (root / "samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs").read_text()
        previous = -1
        for token in ('journal?.Write("native-resize-request")', 'journal?.Write("native-resize-callback-entered")',
                'host.SetClientSize(resizedWidth, resizedHeight);', 'journal?.Write("native-resize-setter-returned")',
                'WakeLiveRenderHost(host);', 'journal?.Write("native-resize-wake-returned")',
                'await WaitForLiveNativeResizeAsync(host, (uint)resizedWidth',
                'journal?.Write("native-resize-geometry-observed")', 'var resized = await ObserveIdlePhaseAsync('):
            current = source.find(token, previous + 1)
            self.assertGreater(current, previous, token)
            previous = current
        start = source.index("IdleSourceState before = await ReadIdleBoundaryAsync(")
        end = source.index("receipt.Phases.Add(phase)", start)
        self.assertNotIn("journal", source[start:end])
        self.assertIn("TimeSpan.FromSeconds(30)", source)
        self.assertEqual(2, source.count("requestRenderWhileObserving: false"))
        # The original helper is still the same two actions used above.
        shared = (root / "samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs").read_text()
        start = shared.index("private static void SetLiveNativeWindowSize(")
        body = shared[start:shared.index("\n    }", start)]
        self.assertIn("liveHost.SetClientSize(width, height);\n        WakeLiveRenderHost(liveHost);", body)


def iter_generator(values):
    yield from values


if __name__ == "__main__":
    unittest.main()
