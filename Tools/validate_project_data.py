#!/usr/bin/env python3
"""Validate the v42 proven-observation acquisition and rigid B+C contract."""

from __future__ import annotations

import hashlib
import json
import struct
import subprocess
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
    bridge = json.loads(
        (ROOT / "Assets/Calibration/bottle_v42_v41b_to_v40orb_frame_bridge.json").read_text()
    )
    acquisition = json.loads(
        (ROOT / "Assets/Docs/tracking_acquisition_regression_v42.json").read_text()
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
    if manifest.get("records") != 4100:
        raise ValueError("v42 did not restore all 4100 device-proven observations")
    if manifest.get("runtime_surface_support_filter_applied") is not False:
        raise ValueError("the v41 5 mm filter still damages runtime acquisition")
    if manifest.get("descriptor_stream_byte_identical_to_v40") is not True:
        raise ValueError("v42 does not declare the proven v40 descriptor stream")
    baseline = subprocess.check_output([
        "git", "show", "aeb5a36:Assets/OrbModels/bottle_reference_b.bytes"
    ], cwd=ROOT)
    if orb.read_bytes() != baseline:
        raise ValueError("v42 ORB database is not byte-for-byte identical to aeb5a36/v40")
    if manifest.get("matching_and_pnp_thresholds_modified") is not False:
        raise ValueError("v42 must not change matching/PnP thresholds")
    if report.get("version") != "bottle-v42-v41-geometry-in-proven-v40-orb-frame":
        raise ValueError("runtime B is not the v41 geometry bridged to proven v40 ORB")
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
        raise ValueError("C geometry changed; it must remain in the proven v40 ORB frame")

    if artifact.get("source_orb_sha256") != "32913A73152D61CC9312132A1F9D565FC316F811C3D8F4E83E7C9236D5CD9122":
        raise ValueError("v41 same-reconstruction measurement provenance changed")
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
    if bridge["runtime_orb_database_sha256"] != sha256(orb):
        raise ValueError("v41-B to v40-ORB frame bridge hash is stale")
    bridge_matrix = np.asarray(
        bridge["T_V40_ORB_FROM_V41_B"], dtype=np.float64
    ).reshape(4, 4)
    if not np.allclose(bridge_matrix[:3, 3], 0.0, atol=1e-9):
        raise ValueError("v42 frame bridge moved the shared mouth origin")
    bridge_scale = np.cbrt(np.linalg.det(bridge_matrix[:3, :3]))
    if abs(bridge_scale - bridge["uniform_scale"]) > 1e-9:
        raise ValueError("v42 frame bridge scale is internally inconsistent")
    if bridge.get("rigid_relationship_preserved") is not True:
        raise ValueError("v42 does not preserve the B/C rigid relationship")
    if (acquisition.get("database_bytes_identical") is not True
            or acquisition.get("descriptor_bytes_identical") is not True
            or acquisition.get("thresholds_modified") is not False):
        raise ValueError("v40-v42 acquisition regression identity failed")
    for view in ("front", "left_oblique", "right_oblique"):
        if acquisition["views"][view]["v42_result"]["poseValid"] is not True:
            raise ValueError(f"required v42 global acquisition view failed: {view}")

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
        raise ValueError(f"runtime contains prohibited tracking logic: {found}")
    for token in (
        "ModelRegistrationEvidence.TryParse",
        "ConfidenceWeightedPoseFusion.Step",
        "hierarchyTransformRoundTripVerified",
        "AssertStartPoseUnchanged",
        "ShowRepairPresentation",
        "SetReferenceHierarchyVisible(false)",
        "SetReliableTrackedPosePrior",
        "tracker.ClearPosePrior()",
        "PreAlignFront: +Z calibration",
    ):
        if token not in controller:
            raise ValueError(f"managed v42 contract missing {token}")
    for token in ("SetPosePrior", "SOLVEPNP_SQPNP", "urp_orb_get_last_inliers"):
        if token not in native:
            raise ValueError(f"native baseline changed: missing {token}")
    if "[URP_CAMERA_SYNC_DIAG]" not in controller:
        raise ValueError("camera timestamp synchronization diagnostics are missing")
    if "BottleRepairAR_v42.apk" not in setup:
        raise ValueError("setup does not build v42")
    if "v42" not in app_controller:
        raise ValueError("tracking screen does not display v42")
    for token in (
        "orb-tracking-v42-proven-global-acquisition",
        "coconut-v41-geometry-v40-orb-frame-v42",
        "bottle-orb-device-proven-observations-v42",
    ):
        if token not in build_identity:
            raise ValueError(f"build identity is stale: {token}")

    print(json.dumps({
        "status": "BOTTLE_ACQUISITION_AND_REGISTRATION_V42_DATA_OK",
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
        "T_V40_ORB_FROM_V41_B": bridge["T_V40_ORB_FROM_V41_B"],
        "descriptor_stream_byte_identical_to_v40": True,
        "first_acquisition_prior": "NONE",
        "device_overlay_verified": False,
    }, indent=2))


if __name__ == "__main__":
    main()
