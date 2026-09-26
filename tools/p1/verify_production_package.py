"""Check that a production MSIX contains the shell and no P0 probe UI or corpus."""

from pathlib import Path
import sys
from xml.etree import ElementTree
from zipfile import ZipFile


def verify(package_path: Path) -> None:
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
        identity = manifest.find("{http://schemas.microsoft.com/appx/manifest/foundation/windows10}Identity")
        if identity is None or identity.attrib.get("Name") != "DesktopGuides.Preview":
            raise ValueError("Production package identity is not isolated from P0.")

        for file_name in ("desktopguides.production.dll", "resources.pri"):
            if file_name not in entries:
                continue
            payload = package.read(entries[file_name])
            for marker in ("FixturePicker", "Desktop Guides — P0 reader probe"):
                if marker.encode("utf-8") in payload or marker.encode("utf-16le") in payload:
                    raise ValueError(f"P0 fixture control found in {file_name}.")

    print(f"Verified production package without P0 fixture controls: {package_path}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: verify_production_package.py <package.msix>")
    verify(Path(sys.argv[1]))
