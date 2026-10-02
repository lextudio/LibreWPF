#!/usr/bin/env python3
"""Check the selected preview feed's internal nuspec dependency closure, offline.

Only metadata is read. This does not restore packages, load managed/native code,
qualify payload provenance or replace the real SDK/package consumer gates.
"""

import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
import zipfile


MAX_NUSPEC_BYTES = 1024 * 1024


def read_package(path):
    with zipfile.ZipFile(path) as package:
        specs = [entry for entry in package.infolist()
                 if entry.filename.casefold().endswith(".nuspec")]
        if len(specs) != 1 or specs[0].file_size > MAX_NUSPEC_BYTES:
            raise ValueError(f"{path.name}: expected one bounded nuspec")
        metadata = ET.fromstring(package.read(specs[0])).find("{*}metadata")
    if metadata is None:
        raise ValueError(f"{path.name}: missing package metadata")
    package_id = metadata.findtext("{*}id", "").strip()
    version = metadata.findtext("{*}version", "").strip()
    if not package_id or not version or path.name != f"{package_id}.{version}.nupkg":
        raise ValueError(f"{path.name}: package identity does not match selected file")
    dependencies = []
    for entry in metadata.findall("{*}dependencies//{*}dependency"):
        dependency = entry.get("id", "").strip()
        constraint = entry.get("version", "").strip()
        if not dependency:
            raise ValueError(f"{path.name}: dependency without an id")
        if dependency.casefold().startswith(("progpu.", "librewpf.")):
            dependencies.append((dependency, constraint))
    return package_id, version, dependencies


def audit_packages(paths):
    if not paths:
        raise ValueError("No selected preview packages supplied")
    packages = {}
    for path in paths:
        package_id, version, dependencies = read_package(Path(path))
        key = package_id.casefold()
        if key in packages:
            raise ValueError(f"Duplicate selected package id {package_id}")
        packages[key] = (package_id, version, dependencies)
    for package_id, _, dependencies in packages.values():
        for dependency, constraint in dependencies:
            selected = packages.get(dependency.casefold())
            if selected is None:
                raise ValueError(f"{package_id} requires {dependency} {constraint}, "
                                 "which is absent from the selected preview feed")
            # NuGet pack normally emits a bare minimum version; also admit an
            # exact singleton range. In either case this feed must contain the
            # declared version itself, never a later/older substitute from cache
            # or a public source. General internal version ranges need an
            # explicit release contract rather than approximate semver matching.
            version = selected[1]
            if constraint not in (version, f"[{version}]"):
                raise ValueError(f"{package_id} requires {dependency} {constraint}, "
                                 f"but the selected preview feed contains {version}")
    return len(packages)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", nargs="+", type=Path)
    args = parser.parse_args()
    try:
        count = audit_packages(args.packages)
    except (ValueError, OSError, ET.ParseError, zipfile.BadZipFile, RuntimeError) as error:
        print(f"Preview package dependency closure failed: {error}", file=sys.stderr)
        return 1
    print(f"Preview package dependency closure verified for {count} selected packages.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
