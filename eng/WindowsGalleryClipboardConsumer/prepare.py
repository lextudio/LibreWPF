#!/usr/bin/env python3
"""Prepare or compile the original Gallery case; never run an app or use the clipboard."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
OUTPUT_RELATIVE = Path("bin/Release/net10.0-windows")
BUILD_JOBS = {
    "Linux headless multi-window render device smoke",
    "Retained layout-clip invalidation contracts",
    "Windows native crash capture controls (x64)",
    "Portable MessageBox, layout-clip and popup source contracts",
    "Windows native crash capture controls (arm64)",
    "Windows managed runtime payload",
    "Canonical System.Windows.Forms source integration",
    "SDK package and no-source-change smoke",
    "Linux Wayland-session XWayland input and popup smoke",
    "Windows ARM64 native MIL package Showcase",
    "Windows x64 native MIL package Showcase",
    "Canonical LibreWPF SDK package consumer",
    "Windows AnyCPU package launch",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    require(path.is_file() and not path.is_symlink(), f"missing or linked receipt: {path}")
    require(path.stat().st_size <= 2 * 1024 * 1024, "receipt exceeds bound")
    return json.loads(path.read_text(encoding="utf-8"))


def succeeded(value):
    return str(value.get("status", "")).lower() == "completed" and str(value.get("conclusion", "")).lower() == "success"


def validate_producer(build, docs, artifact, head, build_id, docs_id):
    require(re.fullmatch(r"[0-9a-f]{40}", head), "explicit full producer head required")
    for run, run_id, name in ((build, build_id, "LibreWPF Build"), (docs, docs_id, "LibreWPF Docs")):
        require(run.get("databaseId") == run_id and run.get("headSha") == head and run.get("name") == name,
                "producer run/head/workflow mismatch")
        require(succeeded(run), "whole exact producer run must have succeeded")
    jobs = build.get("jobs", [])
    require(len(jobs) == len({j.get("name") for j in jobs}) and BUILD_JOBS.issubset({j.get("name") for j in jobs}),
            "complete original Build job inventory required")
    require(all(succeeded(j) for j in jobs), "every original Build job must succeed")
    doc_jobs = docs.get("jobs", [])
    require(len(doc_jobs) == 1 and doc_jobs[0].get("name") == "Verify release documentation" and succeeded(doc_jobs[0]),
            "complete successful Docs job required")
    producer = next(j for j in jobs if j["name"] == "SDK package and no-source-change smoke")
    uploads = [s for s in producer.get("steps", []) if s.get("name") == "Upload CI package bundle"]
    require(len(uploads) == 1 and succeeded(uploads[0]), "exact producer upload must succeed")
    require(artifact.get("name") == "librewpf-ci-packages-" + head and artifact.get("expired") is False,
            "exact unexpired package artifact required")
    run = artifact.get("workflow_run", {})
    require(run.get("id") == build_id and run.get("head_sha") == head, "artifact producer mismatch")
    require(re.fullmatch(r"sha256:[0-9a-f]{64}", artifact.get("digest", "")), "artifact SHA256 missing")


def validate_sources(source):
    manifest = read_json(ROOT / "source-manifest.json")
    require(manifest.get("repository") == "microsoft/WPF-Samples"
            and manifest.get("commit") == "811d01e95c8c929e68539d698d0a0609e94fd185"
            and len(manifest.get("files", [])) == 12, "original Gallery manifest mismatch")
    for entry in manifest["files"]:
        relative = PurePosixPath(entry["path"])
        require(not relative.is_absolute() and ".." not in relative.parts, "invalid original path")
        path = source / relative
        require(path.is_file() and not any(p.is_symlink() for p in (path, *path.parents)), "original source must be regular, unlinked files")
        data = path.read_bytes()
        require(len(data) == entry["bytes"] and hashlib.sha256(data).hexdigest() == entry["sha256"]
                and hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest() == entry["gitBlob"],
                f"original source changed: {relative}")
    return manifest


def package_identity(path):
    with zipfile.ZipFile(path) as archive:
        entries = [i for i in archive.infolist() if i.filename.endswith(".nuspec") and "/" not in i.filename]
        require(len(entries) == 1 and entries[0].file_size <= 1024 * 1024, "one bounded package manifest required")
        root = ET.fromstring(archive.read(entries[0]))
        metadata = next((e for e in root if e.tag.rsplit("}", 1)[-1] == "metadata"), None)
        require(metadata is not None, "package metadata missing")
        values = {e.tag.rsplit("}", 1)[-1]: e for e in metadata}
        package_id, version = values["id"].text, values["version"].text
        require(package_id and re.fullmatch(r"[A-Za-z0-9.+-]+", version or ""), "invalid package identity")
        return package_id, version, values.get("repository")


def stage_packages(archive_path, feed, head):
    inventory = {}
    with zipfile.ZipFile(archive_path) as archive:
        entries = [i for i in archive.infolist() if i.filename.lower().endswith(".nupkg")]
        require(0 < len(entries) <= 128 and sum(i.file_size for i in entries) <= 2 * 1024**3, "package archive inventory exceeds bound")
        for entry in entries:
            relative = PurePosixPath(entry.filename)
            require(not relative.is_absolute() and ".." not in relative.parts
                    and "\\" not in entry.filename and ":" not in entry.filename, "unsafe package archive member")
            name = relative.name
            destination = feed / name
            with archive.open(entry) as source, destination.open("xb") as output:
                shutil.copyfileobj(source, output)
            package_id, version, repository = package_identity(destination)
            require(package_id.casefold() not in {i.casefold() for i in inventory}, "duplicate package identity")
            if package_id in ("LibreWPF.Sdk", "LibreWPF.Transport", "LibreWPF.ProGPU"):
                require(repository is not None and repository.get("commit") == head, f"{package_id} source commit mismatch")
            inventory[package_id] = {"file": name, "version": version, "sha256": sha(destination)}
    for name in ("LibreWPF.Sdk", "LibreWPF.Transport", "LibreWPF.ProGPU", "LibreWPF.Interop", "ProGPU.Backend.Native"):
        require(name in inventory, f"required installed package missing: {name}")
    sdk = inventory["LibreWPF.Sdk"]["version"]
    engine = inventory["ProGPU.Backend.Native"]["version"]
    require(inventory["LibreWPF.Transport"]["version"] == sdk and inventory["LibreWPF.ProGPU"]["version"] == sdk,
            "WPF package version mismatch")
    require(inventory["LibreWPF.Interop"]["version"] == engine, "engine interop version mismatch")
    return inventory, sdk, engine


def seed_sdk(package, packages, version):
    destination = packages / "librewpf.sdk" / version
    destination.mkdir(parents=True)
    with zipfile.ZipFile(package) as archive:
        entries = archive.infolist()
        require(len(entries) <= 10000 and sum(i.file_size for i in entries) <= 512 * 1024**2, "SDK extraction exceeds bound")
        for entry in entries:
            path = PurePosixPath(entry.filename)
            require(not path.is_absolute() and ".." not in path.parts and "\\" not in entry.filename
                    and ":" not in entry.filename and (entry.external_attr >> 16) & 0o170000 != 0o120000, "unsafe SDK archive member")
            target = destination / path
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry) as source, target.open("xb") as output:
                    shutil.copyfileobj(source, output)
    # Same private-cache admission as Install-ExactSdkPackage in the Windows gate.
    cached = destination / f"librewpf.sdk.{version}.nupkg"
    with package.open("rb") as source, cached.open("xb") as output:
        shutil.copyfileobj(source, output)
    digest = base64.b64encode(hashlib.sha512(package.read_bytes()).digest()).decode()
    (destination / (cached.name + ".sha512")).write_text(digest)
    (destination / ".nupkg.metadata").write_text(json.dumps({"version": 2, "contentHash": digest, "source": str(package.parent)}))


def verify_output(output, inventory, architecture):
    directory = output / OUTPUT_RELATIVE
    rid = "win-" + architecture
    assets = {
        "PresentationCore.dll": ("LibreWPF.Transport", f"runtimes/{rid}/lib/net10.0/PresentationCore.dll"),
        "PresentationFramework.dll": ("LibreWPF.Transport", "lib/net10.0/PresentationFramework.dll"),
        "WindowsBase.dll": ("LibreWPF.Transport", "lib/net10.0/WindowsBase.dll"),
        "System.Private.Windows.Core.dll": ("LibreWPF.Transport", "lib/net10.0/System.Private.Windows.Core.dll"),
        "PresentationNative_cor3.dll": ("LibreWPF.Transport", f"runtimes/{rid}/native/PresentationNative_cor3.dll"),
        "ProGPU.Wpf.dll": ("LibreWPF.ProGPU", "lib/net10.0/ProGPU.Wpf.dll"),
        "ProGPU.Wpf.Interop.dll": ("LibreWPF.Interop", "lib/net10.0/ProGPU.Wpf.Interop.dll"),
        "progpu_native.dll": ("ProGPU.Backend.Native", f"runtimes/{rid}/native/progpu_native.dll"),
    }
    hashes = {}
    for name, (package, member) in assets.items():
        with zipfile.ZipFile(output / "feed" / inventory[package]["file"]) as archive:
            with archive.open(member) as stream:
                expected = hashlib.file_digest(stream, "sha256").hexdigest()
        actual = sha(directory / name)
        require(actual == expected, f"compiled consumer substituted package asset: {name}")
        hashes[name] = actual
    apphost = directory / "GalleryClipboardApp.exe"
    data = apphost.read_bytes()
    require(len(data) >= 64 and data[:2] == b"MZ", "missing native apphost PE")
    offset = int.from_bytes(data[60:64], "little")
    require(offset + 6 <= len(data) and data[offset:offset + 4] == b"PE\0\0"
            and int.from_bytes(data[offset + 4:offset + 6], "little") == (0xAA64 if architecture == "arm64" else 0x8664),
            "apphost architecture mismatch")
    runtime = read_json(directory / "GalleryClipboardApp.runtimeconfig.json")
    require(runtime["runtimeOptions"]["configProperties"].get("LibreWPF.RequestedRendererMode") == "NativeMilWgpu",
            "consumer did not request native MIL")
    hashes[apphost.name] = sha(apphost)
    hashes["GalleryClipboardApp.dll"] = sha(directory / "GalleryClipboardApp.dll")
    return {"directory": str(directory), "sha256": hashes, "executed": False}


def build_command(dotnet, project, architecture, config, cache):
    return [str(dotnet.resolve()) if dotnet else "<explicit-dotnet>", "build", str(project),
            "-c", "Release", "-r", "win-" + architecture, "-m:1", "-nodeReuse:false",
            "-p:UseSharedCompilation=false", "-p:PlatformTarget=" + architecture,
            "-p:AppendRuntimeIdentifierToOutputPath=false",
            "-p:RestoreConfigFile=" + str(config), "-p:RestorePackagesPath=" + str(cache),
            "-p:RestoreFallbackFolders=", "-p:RestoreAdditionalProjectSources="]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("original-source", "build-receipt", "docs-receipt", "artifact-receipt", "package-archive", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--expected-head", required=True)
    parser.add_argument("--expected-build", required=True, type=int)
    parser.add_argument("--expected-docs", required=True, type=int)
    parser.add_argument("--architecture", required=True, choices=("arm64", "x64"))
    parser.add_argument("--build-only", action="store_true")
    parser.add_argument("--dotnet", type=Path)
    args = parser.parse_args()
    require(not args.build_only or args.dotnet is not None and args.dotnet.is_file(), "build-only requires an explicit installed dotnet executable")
    build, docs, artifact = (read_json(p) for p in (args.build_receipt, args.docs_receipt, args.artifact_receipt))
    validate_producer(build, docs, artifact, args.expected_head, args.expected_build, args.expected_docs)
    require(args.package_archive.is_file() and not args.package_archive.is_symlink(), "missing regular package archive")
    require(args.package_archive.stat().st_size == artifact["size_in_bytes"]
            and sha(args.package_archive) == artifact["digest"].removeprefix("sha256:"), "package archive bytes differ from producer digest")
    manifest = validate_sources(args.original_source)
    output = args.output.resolve()
    output.mkdir()  # Fresh task-owned root; never overwrite an attempt.
    receipt = {"schema": "gallery-clipboard-preparation-v1", "head": args.expected_head,
               "build": args.expected_build, "docs": args.expected_docs, "compiled": False,
               "qualified": False, "applicationExecuted": False, "clipboardAccessed": False}
    try:
        feed, cache = output / "feed", output / "packages"
        feed.mkdir(); cache.mkdir()
        inventory, sdk, engine = stage_packages(args.package_archive, feed, args.expected_head)
        receipt["packages"] = inventory
        for source, name in ((args.build_receipt, "producer-build.json"), (args.docs_receipt, "producer-docs.json"), (args.artifact_receipt, "producer-artifact.json")):
            shutil.copyfile(source, output / name)
        receipt["archiveSha256"] = sha(args.package_archive)
        for entry in manifest["files"]:
            target = output / "original" / entry["path"]
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(args.original_source / entry["path"], target)
        validate_sources(output / "original")
        for name in ("GalleryClipboardApp.cs", "source-manifest.json", "run-windows.py"):
            shutil.copyfile(ROOT / name, output / name)
        project = output / "GalleryClipboardApp.csproj"
        project.write_text((ROOT / "GalleryClipboardApp.csproj.template").read_text().replace("@SDK_VERSION@", sdk).replace("@ENGINE_VERSION@", engine))
        compiler_sdk = read_json(ROOT.parent.parent / "global.json")["sdk"]["version"]
        (output / "global.json").write_text(json.dumps({"sdk": {"version": compiler_sdk, "rollForward": "disable", "allowPrerelease": True}}))
        receipt["compilerSdk"] = compiler_sdk
        seed_sdk(feed / inventory["LibreWPF.Sdk"]["file"], cache, sdk)
        config = ET.parse(ROOT.parent.parent / "samples/ProGPU.Wpf.ShowcaseApp/NuGet.config")
        config.find("./config/add[@key='globalPackagesFolder']").set("value", str(cache))
        config.find("./packageSources/add[@key='ProGPUWpfLocalArtifacts']").set("value", str(feed))
        mapping = ET.SubElement(config.getroot(), "packageSourceMapping")
        for source in config.findall("./packageSources/add"):
            item = ET.SubElement(mapping, "packageSource", key=source.get("key"))
            patterns = ("LibreWPF*", "LibreWinForms*", "ProGPU*") if source.get("key") == "ProGPUWpfLocalArtifacts" else ("*",)
            for pattern in patterns:
                ET.SubElement(item, "package", pattern=pattern)
        config.write(output / "NuGet.config", encoding="utf-8", xml_declaration=True)
        # SDK resolver, restore, compiler outputs and temporary files all stay private.
        environment = os.environ.copy()
        for name, relative in (("NUGET_PACKAGES", "packages"), ("DOTNET_CLI_HOME", "cli-home"), ("TEMP", "temp"), ("TMP", "temp"), ("TMPDIR", "temp")):
            directory = output / relative
            directory.mkdir(exist_ok=True)
            environment[name] = str(directory)
        command = build_command(args.dotnet, project, args.architecture, output / "NuGet.config", cache)
        receipt["buildCommand"] = command
        receipt["inputs"] = {name: sha(output / name) for name in ("GalleryClipboardApp.cs", "GalleryClipboardApp.csproj", "source-manifest.json", "run-windows.py")}
        if args.build_only:
            with (output / "build.log").open("xb") as log:
                result = subprocess.run(command, cwd=output, env=environment, stdout=log, stderr=subprocess.STDOUT, timeout=300, check=False)
            receipt["buildExitCode"] = result.returncode
            require(result.returncode == 0, "Gallery installed-consumer compilation failed; original build.log retained")
            receipt["output"] = verify_output(output, inventory, args.architecture)
            receipt["compiled"] = True
        print(output)
    except BaseException as error:
        receipt["error"] = str(error)
        raise
    finally:
        with (output / "preparation.json").open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2)


if __name__ == "__main__":
    main()
