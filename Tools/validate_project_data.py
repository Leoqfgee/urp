#!/usr/bin/env python3
"""Validate the v41 measured same-reconstruction B+C contract."""

from __future__ import annotations

import hashlib
import json
import struct
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
MAGIC = b"URP3DM1\0"
RECORD_SIZE = 44


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def points(path: Path, expected_count: int) -> np.ndarray:
    data = path.read_bytes()
    if data[:8] != MAGIC:
        raise ValueError("invalid ORB database")
    count = struct.unpack_from("<I", data, 8)[0]
    if count != expected_count or len(data) != 12 + count * RECORD_SIZE:
        raise ValueError(f"expected the manifest-declared {expected_count}-record ORB database")
    return np.asarray(
        [struct.unpack_from("<3f", data, 12 + i * RECORD_SIZE) for i in range(count)]
    )


def main() -> None:
    orb = ROOT / "Assets/OrbModels/bottle_reference_b.bytes"
    manifest = json.loads(
        (ROOT / "Assets/OrbModels/bottle_reference_b_manifest.json").read_text()
    )
    asset_root = ROOT / "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2"
    fbx = asset_root / "bottle_full_aligned_v2.fbx"
    report = json.loads((asset_root / "bottle_full_aligned_v2_report.json").read_text())
    artifact = json.loads(
        (ROOT / "Assets/Calibration/bottle_orb_to_b_registration.json").read_text()
    )
    contract = json.loads(
        (ROOT / "Assets/Calibration/bottle_orb_frame_contract.json").read_text()
    )
    controller = (ROOT / "Assets/Scripts/OrbImageTrackingController.cs").read_text(
        encoding="utf-8"
    )
    native = (ROOT / "Native/UrpOrbNative/src/urp_orb_native.cpp").read_text(
        encoding="utf-8"
    )
    setup = (ROOT / "Assets/Editor/UrpArProjectSetup.cs").read_text(
        encoding="utf-8"
    )
    app_controller = (ROOT / "Assets/Scripts/UrpAppController.cs").read_text(
        encoding="utf-8"
    )
    build_identity = (ROOT / "Assets/Generated/BuildIdentity.cs").read_text(
        encoding="utf-8"
    )
    calibration = (
        ROOT / "Assets/Calibration/CoconutBottleRepairCalibration.asset"
    ).read_text(encoding="utf-8")
    cloud = points(orb, int(manifest["records"]))

    if manifest.get("database_sha256") != sha256(orb):
        raise ValueError("ORB binary changed or manifest hash is stale")
    if manifest.get("repair_c_excluded_from_matching") is not True:
        raise ValueError("C entered the ORB database")
    if manifest.get("records_before_surface_support_filter") != 4100:
        raise ValueError("v41 surface-support filter provenance is missing")
    if manifest.get("matching_and_pnp_thresholds_modified") is not False:
        raise ValueError("v41 must not change matching/PnP thresholds")
    if report.get("version") != "bottle-orb-same-reconstruction-rigid-pair-v41":
        raise ValueError("runtime B is not the v41 same-reconstruction rigid asset")
    if "bottle_damaged" not in report["referenceB"]["sourceReconstruction"]:
        raise ValueError("same-reconstruction B provenance is missing")
    if report.get("rigidRelationshipPreserved") is not True:
        raise ValueError("B+C rigid contract missing")
    expected_identity = np.eye(4).reshape(-1).tolist()
    if report["rigidContract"]["bLocalMatrix"] != expected_identity:
        raise ValueError("B local matrix is not identity")
    if report["rigidContract"]["cLocalMatrix"] != expected_identity:
        raise ValueError("C local matrix changed")
    if report["repairC"]["geometrySha256Before"] != report["repairC"]["geometrySha256After"]:
        raise ValueError("C geometry changed during v41 packaging")

    if artifact.get("source_orb_sha256") != sha256(orb):
        raise ValueError("registration artifact ORB hash mismatch")
    if artifact.get("target_b_mesh_sha256") != sha256(fbx):
        raise ValueError("registration artifact runtime FBX hash mismatch")
    if artifact.get("independent_model_registration_verified") is not True:
        raise ValueError("real model registration is not verified")
    if artifact.get("device_verified") is not False:
        raise ValueError("offline artifact cannot claim device verification")
    matrix = np.asarray(artifact.get("T_ORB_FROM_B"), dtype=np.float64).reshape(4, 4)
    if np.allclose(matrix, np.eye(4), atol=1e-7):
        raise ValueError("raw production B was not baked into the measured canonical frame")
    if abs(np.linalg.det(matrix[:3, :3]) - artifact["determinant"]) > 1e-6:
        raise ValueError("registration determinant does not match T_ORB_FROM_B")
    stats = artifact["orb_point_to_b_surface_mm"]
    if not all(artifact.get(name) is True for name in (
        "mouth_center_independently_measured",
        "base_center_independently_measured",
        "front_semantics_independently_measured",
    )):
        raise ValueError("independent mouth/base/front evidence is missing")
    if (artifact["landmark_rms_mm"] > 1.0
            or artifact["mouth_center_error_mm"] > 2.0
            or artifact["base_center_error_mm"] > 3.0
            or stats["median_mm"] > 2.5
            or stats["p95_mm"] > 5.0):
        raise ValueError("strict landmark/surface registration exceeds contract")
    if artifact["up_axis_error_deg"] > 1.5 or artifact["front_axis_error_deg"] > 1.5:
        raise ValueError("up/front angular contract failed")
    if contract["orb_database_sha256"] != sha256(orb):
        raise ValueError("frame contract ORB hash mismatch")
    if contract["coordinate_frame_origin"] != artifact["orb_origin_definition"]:
        raise ValueError("ORB origin definitions conflict")

    if "hasAuthoredBLandmarks: 0" not in calibration:
        raise ValueError("copied authored B landmarks were not disabled")
    if "mouthCenterInModel: {x: 0, y: 0, z: 0}" not in calibration:
        raise ValueError("calibration still uses the false shoulder-cut origin")
    prohibited = (
        "registeredRepairPart.localPosition",
        "registeredRepairPart.localRotation",
        "registeredRepairPart.localScale",
        "maximumWorldPositionCorrectionMetersPerSecond",
        "maximumWorldRotationCorrectionDegreesPerSecond",
        "BHierarchy:",
    )
    found = [token for token in prohibited if token in controller]
    if found:
        raise ValueError(f"runtime contains prohibited pre-v41 logic: {found}")
    for token in (
        "ModelRegistrationEvidence.TryParse",
        "ConfidenceWeightedPoseFusion.Step",
        "hierarchyTransformRoundTripVerified",
        "AssertStartPoseUnchanged",
        "ShowRepairPresentation",
        "SetReferenceHierarchyVisible(false)",
    ):
        if token not in controller:
            raise ValueError(f"managed v41 contract missing {token}")
    for token in ("SetPosePrior", "SOLVEPNP_SQPNP", "urp_orb_get_last_inliers"):
        if token not in native:
            raise ValueError(f"native baseline changed: missing {token}")
    if "[URP_CAMERA_SYNC_DIAG]" not in controller:
        raise ValueError("camera timestamp synchronization diagnostics are missing")
    if "BottleRepairAR_v41.apk" not in setup:
        raise ValueError("setup does not build v41")
    if "v41" not in app_controller:
        raise ValueError("tracking screen does not display v41")
    for token in (
        "orb-tracking-v41-same-reconstruction-adaptive-se3",
        "coconut-same-reconstruction-measured-v41",
        "bottle-orb-same-reconstruction-reference-b-v41",
    ):
        if token not in build_identity:
            raise ValueError(f"build identity is stale: {token}")

    print(json.dumps({
        "status": "BOTTLE_ORB_SAME_RECONSTRUCTION_V41_DATA_OK",
        "database_sha256": sha256(orb),
        "fbx_sha256": sha256(fbx),
        "records": len(cloud),
        "orb_bounds_min": cloud.min(axis=0).tolist(),
        "orb_bounds_max": cloud.max(axis=0).tolist(),
        "mouth_center_orb": artifact["mouth_center_orb"],
        "base_center_orb": artifact["base_center_orb"],
        "mouth_error_mm": artifact["mouth_center_error_mm"],
        "base_error_mm": artifact["base_center_error_mm"],
        "up_axis_error_deg": artifact["up_axis_error_deg"],
        "front_axis_error_deg": artifact["front_axis_error_deg"],
        "landmark_rms_mm": artifact["landmark_rms_mm"],
        "surface_mm": stats,
        "T_ORB_FROM_B": artifact["T_ORB_FROM_B"],
        "device_overlay_verified": False,
    }, indent=2))


if __name__ == "__main__":
    main()
