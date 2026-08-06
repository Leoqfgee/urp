#!/usr/bin/env python3
"""Validate the BottleFullAlignedV2 v38 runtime contract without Unity."""

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
    calibration_path = ROOT / "Assets/Calibration/CoconutBottleRepairCalibration.asset"

    points = read_points(database)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    report = json.loads(report_path.read_text(encoding="utf-8"))
    controller = controller_path.read_text(encoding="utf-8")
    native = native_path.read_text(encoding="utf-8")
    setup = setup_path.read_text(encoding="utf-8")
    calibration = calibration_path.read_text(encoding="utf-8")

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
    manifest_scale = float(manifest.get("meters_per_model_unit", 0.0))
    report_scale = float(report.get("coordinateFrame", {}).get("metersPerModelUnit", 0.0))
    if abs(manifest_scale - 0.17) > 1e-6 or abs(report_scale - manifest_scale) > 1e-6:
        raise ValueError("ORB and Blender B do not share the same physical scale")
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
    body_min = np.asarray(report["referenceB"]["boundsMin"], dtype=np.float32)
    body_max = np.asarray(report["referenceB"]["boundsMax"], dtype=np.float32)
    point_min = points.min(axis=0)
    point_max = points.max(axis=0)
    coordinate_margin = 0.03
    if np.any(point_min < body_min - coordinate_margin) or np.any(
        point_max > body_max + coordinate_margin
    ):
        raise ValueError(
            "ORB 3D points fall outside Blender B bounds; canonical frames diverged"
        )
    body_y_span = float(body_max[1] - body_min[1])
    point_y_span = float(point_max[1] - point_min[1])
    if body_y_span <= 0.0 or point_y_span / body_y_span < 0.95:
        raise ValueError("ORB and Blender B vertical axes/scales do not agree")
    if "orbToModelLocalEulerAngles: {x: 0, y: 0, z: 0}" not in calibration:
        raise ValueError("Profile still contains a hand-authored Euler correction")
    if "CanonicalFrameRegistration.TryDerive" not in controller:
        raise ValueError("Runtime does not derive ORB-to-B alignment from landmarks")
    if "new Vector3(90f, 0f, 0f)" in setup:
        raise ValueError("Scene setup still hard-codes the v37 Euler correction")

    prohibited = (
        "displayMatrix",
        "WorldToViewportPoint",
        "AlignmentOutline",
        "ARAnchor",
        "registeredRepairPart.localPosition",
        "registeredRepairPart.localRotation",
        "registeredRepairPart.localScale",
        "activeProfile.referenceDepthOcclusionMaterial",
        "hasReadyPoseCandidate",
        "readyCandidatePosition",
        "readyCandidateRotation",
        "readyCandidateTime",
    )
    found = [token for token in prohibited if token in controller]
    if found:
        raise ValueError(f"Production tracker contains prohibited logic: {found}")
    for token in (
        "RestoreProfileCoordinateAlignment",
        "TryApplyReliablePose",
        "TrackingState.ReadyForRepair",
        "AssertStartPoseUnchanged",
        "SetReferenceHierarchyVisible(false)",
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
        "urp_orb_get_last_inliers",
    ):
        if token not in native:
            raise ValueError(f"Restored native tracker is missing {token}")
    if "BottleFullAlignedV2" not in setup or "BottleCleanCapV31" in setup:
        raise ValueError("Scene generator does not bind only BottleFullAlignedV2")

    payload = {
        "status": "BOTTLE_FULL_ALIGNED_V38_DATA_OK",
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
        "orb_points_within_blender_b_bounds": True,
        "orb_blender_scale_meters_per_unit": manifest_scale,
        "profile_euler_correction_degrees": [0.0, 0.0, 0.0],
        "alignment_source": "runtime_landmark_similarity_fit",
    }
    print(json.dumps(payload, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
