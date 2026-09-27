"""Verify the production MSIX payload and its actual packed identity."""

import argparse
import json
from pathlib import Path
import re
from xml.etree import ElementTree
from zipfile import ZipFile


PACKAGE_NS = "{http://schemas.microsoft.com/appx/manifest/foundation/windows10}"
VERSION = re.compile(r"^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\."
                     r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$")


def checked_version(value: str) -> tuple[int, int, int, int]:
    if not VERSION.fullmatch(value):
        raise ValueError(f"Invalid four-part package version: {value!r}.")
    parts = tuple(int(part) for part in value.split("."))
    if any(part > 65535 for part in parts):
        raise ValueError(f"Package version component exceeds 65535: {value!r}.")
    return parts


def verify(
    package_path: Path,
    expected_name: str,
    expected_publisher: str,
    expected_version: str,
    expected_architecture: str,
    previous_package_path: Path | None = None,
) -> dict[str, str]:
    checked_version(expected_version)
    with ZipFile(package_path) as package:
        entries = {name.lower(): name for name in package.namelist()}
        if "desktopguides.production.dll" not in entries:
            raise ValueError("Production shell assembly is missing.")
        if "desktopguides.app.dll" in entries:
            raise ValueError("P0 diagnostic assembly is bundled.")
        if any(
            name.startswith("fixtures/")
            or "/fixtures/" in name
            or name.startswith("probes/")
            or "/probes/" in name
            for name in entries
        ):
            raise ValueError("P0 fixture or probe files are bundled.")

        manifest = ElementTree.fromstring(package.read(entries["appxmanifest.xml"]))
        identity = manifest.find(f"{PACKAGE_NS}Identity")
        if identity is None:
            raise ValueError("Production package has no Identity.")
        actual = {
            "name": identity.attrib.get("Name", ""),
            "publisher": identity.attrib.get("Publisher", ""),
            "version": identity.attrib.get("Version", ""),
            "architecture": identity.attrib.get("ProcessorArchitecture", "").lower(),
        }
        checked_version(actual["version"])
        expected = {
            "name": expected_name,
            "publisher": expected_publisher,
            "version": expected_version,
            "architecture": expected_architecture.lower(),
        }
        if actual != expected:
            raise ValueError(f"Packed identity {actual} differs from expected {expected}.")
        if actual["name"] == "DesktopGuides.App":
            raise ValueError("Production package uses the P0 diagnostic identity.")

        dependencies = manifest.find(f"{PACKAGE_NS}Dependencies")
        runtime = [] if dependencies is None else [
            dependency for dependency in dependencies.findall(
                f"{PACKAGE_NS}PackageDependency")
            if dependency.attrib.get("Name") == "Microsoft.WindowsAppRuntime.2"
        ]
        if len(runtime) != 1:
            raise ValueError("Expected one Windows App Runtime framework dependency.")
        minimum_runtime = runtime[0].attrib.get("MinVersion", "")
        checked_version(minimum_runtime)
        actual["windowsAppRuntimeMinimum"] = minimum_runtime

        for file_name in ("desktopguides.production.dll", "resources.pri"):
            if file_name not in entries:
                continue
            payload = package.read(entries[file_name])
            for marker in ("FixturePicker", "Desktop Guides — P0 reader probe"):
                if marker.encode("utf-8") in payload or marker.encode("utf-16le") in payload:
                    raise ValueError(f"P0 fixture control found in {file_name}.")

    if previous_package_path is not None:
        with ZipFile(previous_package_path) as previous_package:
            previous_manifest = ElementTree.fromstring(
                previous_package.read("AppxManifest.xml"))
        previous_identity = previous_manifest.find(f"{PACKAGE_NS}Identity")
        if previous_identity is None:
            raise ValueError("Previous package has no Identity.")
        previous = {
            "name": previous_identity.attrib.get("Name", ""),
            "publisher": previous_identity.attrib.get("Publisher", ""),
            "architecture": previous_identity.attrib.get(
                "ProcessorArchitecture", "").lower(),
        }
        if previous != {key: expected[key] for key in previous}:
            raise ValueError("Upgrade changes package name, publisher, or architecture.")
        previous_version = previous_identity.attrib.get("Version", "")
        if checked_version(previous_version) >= checked_version(actual["version"]):
            raise ValueError("Upgrade package version must increase.")
        actual["previousVersion"] = previous_version

    return actual


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--name", required=True)
    parser.add_argument("--publisher", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--architecture", required=True, choices=("x64", "ARM64"))
    parser.add_argument("--previous-package", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.package, args.name, args.publisher,
                            args.version, args.architecture,
                            args.previous_package), sort_keys=True))
