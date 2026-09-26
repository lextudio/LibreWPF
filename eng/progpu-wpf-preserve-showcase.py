#!/usr/bin/env python3
"""Preserve a failed SDK job's exact Showcase files, never a qualified package."""

import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tarfile
import tempfile
import time


APP = "ProGPU.Wpf.ShowcaseApp"
MAX_FILES = 4096
MAX_BYTES = 256 * 1024 * 1024
MAX_ARCHIVE_BYTES = 272 * 1024 * 1024
CAPTURE_SECONDS = 90


def check_deadline(deadline):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise RuntimeError("Showcase archive capture exceeded its 90-second budget")
    return remaining


def checked_directory(repo, value):
    # Do not follow a symlink into caches or an unrelated developer directory.
    lexical = Path(os.path.abspath(value))
    relative = lexical.relative_to(repo)
    current = repo
    for part in relative.parts:
        current /= part
        if current.is_symlink():
            raise ValueError(f"Symlink input directory is not admitted: {current}")
    resolved = lexical.resolve(strict=True)
    if resolved == repo or not resolved.is_dir():
        raise ValueError("Expected a scoped directory below the repository")
    return resolved


def inventory_error(error):
    raise error


def inventory(roots, deadline):
    result = []
    total = 0
    for label, root in roots:
        for directory, dirs, files in os.walk(root, followlinks=False, onerror=inventory_error):
            check_deadline(deadline)
            for name in dirs + files:
                path = Path(directory) / name
                info = path.lstat()
                if stat.S_ISLNK(info.st_mode) or not (stat.S_ISDIR(info.st_mode) or stat.S_ISREG(info.st_mode)):
                    raise ValueError(f"Only regular files/directories may be archived: {path}")
                if stat.S_ISDIR(info.st_mode):
                    continue
                member = f"{label}/{path.relative_to(root).as_posix()}"
                if "\\" in member or any(ord(c) < 32 for c in member):
                    raise ValueError("Unsupported archive member name")
                total += info.st_size
                result.append((member, path, fingerprint(info)))
                if len(result) > MAX_FILES or total > MAX_BYTES:
                    raise ValueError("Showcase archive exceeds the file-count or 256 MiB payload budget")
    return sorted(result)


def fingerprint(info):
    return (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns, info.st_mode)


class HashReader:
    def __init__(self, stream, deadline):
        self.stream, self.deadline = stream, deadline
        self.digest = hashlib.sha256()
        self.size = 0

    def read(self, size):
        check_deadline(self.deadline)
        data = self.stream.read(size)
        self.digest.update(data)
        self.size += len(data)
        return data


class BoundedWriter:
    def __init__(self, stream, deadline):
        self.stream, self.deadline = stream, deadline
        self.size = 0

    def write(self, data):
        check_deadline(self.deadline)
        if self.size + len(data) > MAX_ARCHIVE_BYTES:
            raise ValueError("Showcase compressed archive exceeds 272 MiB")
        count = self.stream.write(data)
        if count != len(data):
            raise OSError("Short archive write")
        self.size += count
        return count

    def flush(self):
        self.stream.flush()


def sha256(path, deadline):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        while True:
            check_deadline(deadline)
            chunk = stream.read(1024 * 1024)
            if not chunk:
                return result.hexdigest()
            result.update(chunk)


def archive_inputs(roots, destination, deadline):
    before = inventory(roots, deadline)
    records = []
    with destination.open("xb") as output:
        with gzip.GzipFile(filename="", mode="wb", fileobj=BoundedWriter(output, deadline),
                           compresslevel=1, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w|", format=tarfile.PAX_FORMAT) as archive:
                for member, path, identity in before:
                    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
                    with os.fdopen(descriptor, "rb") as source:
                        if fingerprint(os.fstat(source.fileno())) != identity:
                            raise ValueError(f"Archive input changed before capture: {member}")
                        reader = HashReader(source, deadline)
                        info = tarfile.TarInfo(member)
                        info.size = identity[2]
                        info.mode = stat.S_IMODE(identity[5]) & 0o777
                        archive.addfile(info, reader)
                        if reader.size != info.size or fingerprint(os.fstat(source.fileno())) != identity:
                            raise ValueError(f"Archive input changed during capture: {member}")
                        records.append({"path": member, "bytes": reader.size, "mode": info.mode,
                                        "sha256": reader.digest.hexdigest()})
    if inventory(roots, deadline) != before:
        raise ValueError("Archive input inventory changed during capture")
    # Hash the retained source bytes again, including files archived before a
    # later file changed. The manifest hashes always describe actual tar bytes.
    for record, (_, path, _) in zip(records, before):
        if sha256(path, deadline) != record["sha256"]:
            raise ValueError(f"Archive input bytes changed: {record['path']}")
    return records


def git(repo, *arguments, deadline):
    remaining = check_deadline(deadline)
    result = subprocess.run(["git", "-C", str(repo), *arguments], check=True,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            text=True, timeout=min(10, remaining))
    check_deadline(deadline)
    return result.stdout.strip()


def capture(args):
    repo = args.repository.resolve(strict=True)
    parent = checked_directory(repo, args.evidence_parent)
    # Reject an unsafe output boundary before creating anything inside an input.
    for value in (args.app_directory, args.native_build_directory, args.native_runtime_directory):
        lexical = Path(os.path.abspath(value))
        if parent == lexical or parent.is_relative_to(lexical):
            raise ValueError("Evidence directory must not be inside an archived input")
    run = Path(tempfile.mkdtemp(prefix="run-", dir=parent))
    report = {"schema": "librewpf-showcase-failure-diagnostic-v1", "qualified": False,
              "status": "incomplete", "sourceCommit": args.source_commit,
              "workflowRunId": args.run_id, "workflowRunAttempt": args.run_attempt,
              "limitations": ["Failed producer: diagnostic evidence only, never package qualification.",
                              "Native files are staged inputs, not proof of loaded-module identity.",
                              "The .NET runtime and operating-system dependencies are not bundled."]}
    deadline = time.monotonic() + CAPTURE_SECONDS
    try:
        if not re.fullmatch(r"[0-9a-f]{40}", args.source_commit):
            raise ValueError("Expected a full lowercase source commit SHA")
        if not re.fullmatch(r"[1-9][0-9]*", args.run_id) or not re.fullmatch(r"[1-9][0-9]*", args.run_attempt):
            raise ValueError("Expected positive workflow run ID and attempt")
        head = git(repo, "rev-parse", "HEAD", deadline=deadline)
        if head != args.source_commit:
            raise ValueError("Expected source commit does not match actual checkout")
        source_state = {"head": head, "trackedStatus": git(repo, "status", "--porcelain", "--untracked-files=no", deadline=deadline),
                        "recursiveSubmoduleStatus": git(repo, "submodule", "status", "--recursive", deadline=deadline)}
        report["sourceState"] = source_state
        roots = [("app", checked_directory(repo, args.app_directory)),
                 ("native-build", checked_directory(repo, args.native_build_directory)),
                 ("native-runtime", checked_directory(repo, args.native_runtime_directory))]
        for label, root in roots:
            if parent == root or parent.is_relative_to(root):
                raise ValueError("Evidence directory must not be inside an archived input")
            report.setdefault("inputDirectories", {})[label] = str(root.relative_to(repo))
        # Validate links and sizes before opening even the JSON metadata.
        inventory(roots, deadline)
        required = [APP, f"{APP}.dll", f"{APP}.deps.json", f"{APP}.runtimeconfig.json"]
        for name in required:
            path = roots[0][1] / name
            if not path.is_file() or path.stat().st_size == 0:
                raise ValueError(f"Missing required macOS Showcase file: {name}")
        for root, name in [(roots[1][1], "libprogpu_native.dylib"),
                           (roots[2][1], "libwgpu_native.dylib")]:
            if not (root / name).is_file() or (root / name).stat().st_size == 0:
                raise ValueError(f"Missing required staged native file: {name}")
        for suffix in ("deps.json", "runtimeconfig.json"):
            with (roots[0][1] / f"{APP}.{suffix}").open(encoding="utf-8") as stream:
                metadata = json.load(stream)
            if not isinstance(metadata, dict):
                raise ValueError(f"Expected an object in {suffix}")
            if suffix == "runtimeconfig.json":
                report["runtimeOptions"] = metadata.get("runtimeOptions")
        archive = run / "showcase-diagnostic.tar.gz"
        records = archive_inputs(roots, archive, deadline)
        after_state = {"head": git(repo, "rev-parse", "HEAD", deadline=deadline),
                       "trackedStatus": git(repo, "status", "--porcelain", "--untracked-files=no", deadline=deadline),
                       "recursiveSubmoduleStatus": git(repo, "submodule", "status", "--recursive", deadline=deadline)}
        if source_state != after_state:
            raise ValueError("Source identity changed during diagnostic capture")
        report.update(status="captured", files=records, payloadBytes=sum(r["bytes"] for r in records),
                      archive={"path": archive.name, "bytes": archive.stat().st_size,
                               "sha256": sha256(archive, deadline)})
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        with (run / "receipt.json").open("x", encoding="utf-8") as stream:
            json.dump(report, stream, indent=2)
            stream.write("\n")
    print(f"Showcase diagnostic archive: {report['status']}; receipt={run / 'receipt.json'}")
    return 0 if report["status"] == "captured" else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("repository", "app-directory", "native-build-directory", "native-runtime-directory", "evidence-parent"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    for name in ("source-commit", "run-id", "run-attempt"):
        parser.add_argument(f"--{name}", required=True)
    return capture(parser.parse_args())


if __name__ == "__main__":
    sys.exit(main())
