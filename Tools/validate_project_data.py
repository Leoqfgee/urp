#!/usr/bin/env python3
"""Validate the BottleFullAlignedV2 v35 runtime contract without Unity."""

from __future__ import annotations

import hashlib
import json
import struct
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
MAGIC_V1 = b"URP3DM1\0"
RECORD_SIZE = 44


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read_points(path: Path) -> np.ndarray:
    data = path.read_bytes()
    if data[:8] != MAGIC_V1:
        raise ValueError(f"{path}: expected device-proven URP3DM1 database")
    count = struct.unpack_from("<I", data, 8)[0]
    if count != 4100 or len(data) != 12 + count * RECORD_SIZE:
        raise ValueError(
            f"{path}: expected 4100 records, got {count} ({len(data)} bytes)"
        )
    return np.asarray(
        [
            struct.unpack_from("<3f", data, 12 + index * RECORD_SIZE)
            for index in range(count)
        ],
        dtype=np.float32,
    )


def assert_identity(item: dict, label: str) -> None:
    if item.get("localPosition") != [0.0, 0.0, 0.0]:
        raise ValueError(f"{label} local position is not identity")
    if item.get("localRotationRadians") != [0.0, 0.0, 0.0]:
        raise ValueError(f"{label} local rotation is not identity")
    if item.get("localScale") != [1.0, 1.0, 1.0]:
        raise ValueError(f"{label} local scale is not identity")


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
    native_path = ROOT / "Native/UrpOrbNative/src/urp_orb_native.cpp"
    setup_path = ROOT / "Assets/Editor/UrpArProjectSetup.cs"

    points = read_points(database)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    report = json.loads(report_path.read_text(encoding="utf-8"))
    controller = controller_path.read_text(encoding="utf-8")
    native = native_path.read_text(encoding="utf-8")
    setup = setup_path.read_text(encoding="utf-8")

    expected_version = "bottle-full-aligned-v2-reference-b-real-observations-v32"
    if manifest.get("version") != expected_version:
        raise ValueError("ORB manifest is not the device-proven v33 B-only baseline")
    if manifest.get("database_format") != "URP3DM1":
        raise ValueError("ORB manifest format is not URP3DM1")
    if manifest.get("records") != len(points):
        raise ValueError("ORB manifest record count does not match the binary")
    if manifest.get("database_sha256") != sha256(database):
        raise ValueError("ORB manifest SHA256 does not match the database")
    if manifest.get("repair_c_excluded_from_matching") is not True:
        raise ValueError("BottleCapC must be excluded from B feature generation")
    provenance = manifest.get("source_provenance", {})
    if provenance.get("rendered_mesh_descriptors_used") is not False:
        raise ValueError("The production database must use real bottle observations")
    if provenance.get("complete_bottle_or_cap_images_used") is not False:
        raise ValueError("The B database must exclude complete-bottle/cap images")
    if manifest.get("device_overlay_verified") is not False:
        raise ValueError("Device overlay cannot be marked verified without device evidence")

    if report.get("version") != "bottle-full-aligned-v2-rigid-neck-cap-v33":
        raise ValueError("Blender report is not the v33 rigid registration")
    if report.get("runtimeHierarchy") != {
        "root": "BottleRepairRoot",
        "referenceB": "DamagedBottleB",
        "referenceNeckB": "ReferenceNeckProxyB",
        "repairC": "BottleCapC",
    }:
        raise ValueError("Blender report hierarchy is invalid")
    if report.get("rigidRelationshipPreserved") is not True:
        raise ValueError("Blender report does not preserve B+C rigidity")
    assert_identity(report["referenceB"], "DamagedBottleB")
    assert_identity(report["referenceNeckB"], "ReferenceNeckProxyB")
    assert_identity(report["repairC"], "BottleCapC")
    registration = report.get("registration", {})
    if abs(registration.get("mouthPlaneModelY", 0.0) - 0.0588235294) > 1e-6:
        raise ValueError("The physical mouth is not 10 mm above the scan cut")
    if abs(registration.get("neckHeightMetersFromBounds", 0.0) - 0.010) > 0.0001:
        raise ValueError("ReferenceNeckProxyB is not the photographed 10 mm neck")
    if abs(registration.get("capHeightMetersFromBounds", 0.0) - 0.01012) > 0.0002:
        raise ValueError("BottleCapC height changed")
    if registration.get("capOverlapsNeckAxially") is not True:
        raise ValueError("BottleCapC no longer seats over the B neck")
    if report["repairC"]["boundsMin"][1] <= 0.0:
        raise ValueError("BottleCapC is still embedded below the damaged scan cut")
    if registration.get("bToCLocalPosition") != [0.0, 0.0, 0.0]:
        raise ValueError("B-to-C local position changed")

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
    for token in (
        "RestoreProfileCoordinateAlignment",
        "SetRenderersEnabled(referenceNeckRenderers, false)",
        "ShowRepairPresentation",
        "worldPositionDeadbandMeters",
    ):
        if token not in controller:
            raise ValueError(f"Restored managed tracker is missing {token}")
    for token in (
        "SetPosePrior",
        "guidedMatches",
        "strictSolution",
        "guidedSolution",
        "SOLVEPNP_SQPNP",
        "SampleReferenceHsv",
    ):
        if token not in native:
            raise ValueError(f"Restored native tracker is missing {token}")
    if "BottleFullAlignedV2" not in setup or "BottleCleanCapV31" in setup:
        raise ValueError("Scene generator does not bind only BottleFullAlignedV2")

    payload = {
        "status": "BOTTLE_FULL_ALIGNED_V35_DATA_OK",
        "fbx_sha256": sha256(fbx),
        "database_sha256": sha256(database),
        "database_records": len(points),
        "database_format": "URP3DM1",
        "database_view_groups": 1,
        "database_bounds_min": points.min(axis=0).tolist(),
        "database_bounds_max": points.max(axis=0).tolist(),
        "repair_c_excluded_from_matching": True,
        "reference_neck_height_meters": 0.010,
        "cap_seated_over_neck": True,
        "device_overlay_verified": False,
    }
    print(json.dumps(payload, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
