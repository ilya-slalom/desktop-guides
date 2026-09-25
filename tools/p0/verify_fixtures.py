"""Verify every P0 fixture against the checked-in hash manifest."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2] / "tests" / "fixtures" / "p0"


def main() -> None:
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    if manifest["schemaVersion"] != 1:
        raise ValueError("Unsupported fixture manifest")

    ids: set[str] = set()
    for entry in manifest["fixtures"]:
        if entry["id"] in ids:
            raise ValueError(f"Duplicate fixture ID: {entry['id']}")
        ids.add(entry["id"])
        path = (ROOT / entry["path"]).resolve()
        if not path.is_relative_to(ROOT.resolve()) or not path.is_file():
            raise ValueError(f"Unsafe or missing fixture: {entry['id']}")
        data = path.read_bytes()
        if len(data) != entry["bytes"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
            raise ValueError(f"Fixture hash mismatch: {entry['id']}")

    print(f"Verified {len(ids)} P0 fixtures")


if __name__ == "__main__":
    main()
