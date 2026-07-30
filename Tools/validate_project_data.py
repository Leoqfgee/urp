#!/usr/bin/env python3
"""Validate the formal BottleCleanCap runtime data without Unity."""

from __future__ import annotations

import hashlib
import json
import struct
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
MAGIC_V1 = b"URP3DM1\0"
MAGIC_V2 = b"URP3DM2\0"
RECORD_SIZE = 44


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read_points(path: Path) -> tuple[np.ndarray, int, str]:
    data = path.read_bytes()
    magic = data[:8]
    count = struct.unpack_from("<I", data, 8)[0]
    offsets: list[int] = []
    group_count = 1
    database_format = "URP3DM1"

    if magic == MAGIC_V1:
        if len(data) != 12 + count * RECORD_SIZE:
            raise ValueError(f"{path}: invalid V1 record count or length ({count})")
        offsets = [12 + index * RECORD_SIZE for index in range(count)]
    elif magic == MAGIC_V2:
        database_format = "URP3DM2"
        if len(data) < 16:
            raise ValueError(f"{path}: truncated V2 header")
        group_count = struct.unpack_from("<I", data, 12)[0]
        cursor = 16
        parsed_count = 0
        if group_count < 2:
            raise ValueError(f"{path}: grouped database has too few groups ({group_count})")
        for _ in range(group_count):
            if cursor + 8 > len(data):
                raise ValueError(f"{path}: truncated V2 group header")
            _, group_records = struct.unpack_from("<II", data, cursor)
            cursor += 8
            group_end = cursor + group_records * RECORD_SIZE
            if group_end > len(data):
                raise ValueError(f"{path}: truncated V2 group records")
            offsets.extend(
                cursor + index * RECORD_SIZE for index in range(group_records)
            )
            cursor = group_end
            parsed_count += group_records
        if cursor != len(data) or parsed_count != count:
            raise ValueError(
                f"{path}: V2 total mismatch ({parsed_count}/{count}, {cursor}/{len(data)})"
            )
    else:
        raise ValueError(f"{path}: invalid URP3DM database magic")

    if count < 1000:
        raise ValueError(f"{path}: insufficient records ({count})")
    points = np.asarray(
        [struct.unpack_from("<3f", data, offset) for offset in offsets],
        dtype=np.float32,
    )
    return points, group_count, database_format


def main() -> None:
    database = ROOT / "Assets/OrbModels/bottle_reference_b.bytes"
    manifest_path = ROOT / "Assets/OrbModels/bottle_reference_b_manifest.json"
    fbx = (
        ROOT
        / "Assets/Models/CleanBottleReconstruction/BottleCleanCapV26"
        / "bottle_no_cap_clean_cap_v26.fbx"
    )
    report_path = fbx.with_name("bottle_no_cap_clean_cap_v26_report.json")
    controller_path = ROOT / "Assets/Scripts/OrbImageTrackingController.cs"
    points, group_count, database_format = read_points(database)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    report = json.loads(report_path.read_text(encoding="utf-8"))
    controller = controller_path.read_text(encoding="utf-8")

    if manifest["version"] != "bottle-no-cap-grouped-multiview-v27":
        raise ValueError("ORB manifest is not the approved grouped B-only database")
    if manifest.get("database_format") != database_format:
        raise ValueError("ORB manifest database format does not match the binary")
    if manifest.get("view_group_count") != group_count:
        raise ValueError("ORB manifest view-group count does not match the binary")
    if manifest["database_sha256"] != sha256(database):
        raise ValueError("ORB manifest SHA256 does not match the database")
    if manifest["repair_c_excluded_from_matching"] is not True:
        raise ValueError("BottleCapC must be excluded from B feature generation")
    if manifest["source_provenance"]["rendered_mesh_descriptors_used"] is not False:
        raise ValueError("The production database must use real no-cap bottle observations")
    if manifest.get("device_overlay_verified") is not False:
        raise ValueError("Device overlay cannot be marked verified without evidence")
    if report["runtimeHierarchy"] != {
        "root": "BottleRepairRoot",
        "referenceB": "DamagedBottleB",
        "referenceNeckGuideB": "ReferenceNeckProxyB",
        "repairC": "BottleCapC",
    }:
        raise ValueError("Blender report hierarchy is invalid")
    if report.get("version") != "bottle-no-cap-clean-cap-v26":
        raise ValueError("Blender report is not the v26 clean-neck registration")
    seating = report.get("capSeating", {})
    if not seating.get("capOverlapsNeckAxially"):
        raise ValueError("BottleCapC is not seated over the cylindrical neck")
    if seating.get("neckMaximumDiameterMeters", 1.0) > 0.04:
        raise ValueError("ReferenceNeckProxyB is still an oversized funnel")
    if not report["rigidContract"]["cIsNeverPositionedIndependentlyAtRuntime"]:
        raise ValueError("Blender report does not preserve the rigid B/C relationship")

    prohibited = (
        "displayMatrix",
        "WorldToViewportPoint",
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
        "status": "BOTTLE_CLEAN_CAP_V27_DATA_OK",
        "fbx_sha256": sha256(fbx),
        "database_sha256": sha256(database),
        "database_records": len(points),
        "database_format": database_format,
        "database_view_groups": group_count,
        "database_bounds_min": points.min(axis=0).tolist(),
        "database_bounds_max": points.max(axis=0).tolist(),
        "repair_c_excluded_from_matching": True,
        "device_overlay_verified": False,
    }
    print(json.dumps(payload, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
