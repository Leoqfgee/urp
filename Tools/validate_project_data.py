#!/usr/bin/env python3
"""Validate the formal BottleFullAlignedV2 runtime data without Unity."""

from __future__ import annotations

import hashlib
import json
import struct
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
MAGIC = b"URP3DM1\0"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read_points(path: Path) -> np.ndarray:
    data = path.read_bytes()
    if data[:8] != MAGIC:
        raise ValueError(f"{path}: invalid URP3DM1 magic")
    count = struct.unpack_from("<I", data, 8)[0]
    if count < 1000 or len(data) != 12 + count * 44:
        raise ValueError(f"{path}: invalid record count or length ({count})")
    return np.asarray(
        [
            struct.unpack_from("<3f", data, 12 + index * 44)
            for index in range(count)
        ],
        dtype=np.float32,
    )


def main() -> None:
    database = ROOT / "Assets/OrbModels/bottle_reference_b.bytes"
    manifest_path = ROOT / "Assets/OrbModels/bottle_reference_b_manifest.json"
    fbx = (
        ROOT
        / "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2"
        / "bottle_full_aligned_v2.fbx"
    )
    report_path = fbx.with_name("bottle_full_aligned_v2_report.json")
    controller_path = ROOT / "Assets/Scripts/OrbImageTrackingController.cs"
    points = read_points(database)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    report = json.loads(report_path.read_text(encoding="utf-8"))
    controller = controller_path.read_text(encoding="utf-8")

    if manifest["version"] != "bottle-full-aligned-v2-reference-b-rendered-v1":
        raise ValueError("ORB manifest is not for BottleFullAlignedV2")
    if manifest["database_sha256"] != sha256(database):
        raise ValueError("ORB manifest SHA256 does not match the database")
    if manifest["repair_c_excluded_from_matching"] is not True:
        raise ValueError("BottleCapC must be excluded from B feature generation")
    if manifest.get("device_overlay_verified") is not False:
        raise ValueError("Device overlay cannot be marked verified without evidence")
    if report["runtimeHierarchy"] != {
        "root": "BottleRepairRoot",
        "referenceB": "DamagedBottleB",
        "repairC": "BottleCapC",
    }:
        raise ValueError("Blender report hierarchy is invalid")
    if not report["rigidRelationshipPreserved"]:
        raise ValueError("Blender report does not preserve the rigid B/C relationship")

    prohibited = (
        "displayMatrix",
        "WorldToViewportPoint",
        "ScreenPoint",
        "AlignmentOutline",
        "ARAnchor",
        "registeredRepairPart.localPosition",
        "registeredRepairPart.localRotation",
        "registeredRepairPart.localScale",
    )
    found = [token for token in prohibited if token in controller]
    if found:
        raise ValueError(f"Production tracker contains prohibited logic: {found}")

    payload = {
        "status": "BOTTLE_FULL_ALIGNED_V2_DATA_OK",
        "fbx_sha256": sha256(fbx),
        "database_sha256": sha256(database),
        "database_records": len(points),
        "database_bounds_min": points.min(axis=0).tolist(),
        "database_bounds_max": points.max(axis=0).tolist(),
        "repair_c_excluded_from_matching": True,
        "device_overlay_verified": False,
    }
    print(json.dumps(payload, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
