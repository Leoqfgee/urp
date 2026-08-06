#!/usr/bin/env python3
"""Validate the v40 measured cross-reconstruction B+C contract."""

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


def points(path: Path) -> np.ndarray:
    data = path.read_bytes()
    if data[:8] != MAGIC:
        raise ValueError("invalid ORB database")
    count = struct.unpack_from("<I", data, 8)[0]
    if count != 4100 or len(data) != 12 + count * RECORD_SIZE:
        raise ValueError("expected the unchanged 4100-record ORB database")
    return np.asarray(
        [struct.unpack_from("<3f", data, 12 + i * RECORD_SIZE) for i in range(count)]
    )


def identity(item: dict, label: str) -> None:
    if item.get("localPosition") != [0.0, 0.0, 0.0]:
        raise ValueError(f"{label} local position changed")
    if item.get("localRotationRadians") != [0.0, 0.0, 0.0]:
        raise ValueError(f"{label} local rotation changed")
    if item.get("localScale") != [1.0, 1.0, 1.0]:
        raise ValueError(f"{label} local scale changed")


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
    cloud = points(orb)

    if manifest.get("database_sha256") != sha256(orb):
        raise ValueError("ORB binary changed or manifest hash is stale")
    if manifest.get("repair_c_excluded_from_matching") is not True:
        raise ValueError("C entered the ORB database")
    if report.get("version") != "bottle-orb-cross-reconstruction-rigid-pair-v40":
        raise ValueError("runtime B is not the v40 cross-registered rigid asset")
    if "bottle_full_clean_v2" not in report["referenceB"]["sourceReconstruction"]:
        raise ValueError("B source reconstruction provenance is missing")
    if report.get("rigidRelationshipPreserved") is not True:
        raise ValueError("B+C rigid contract missing")
    identity(report["referenceB"], "B")
    identity(report["repairC"], "C")
    if report["coordinateFrame"]["physicalMouthCentreModel"] != [0.0, 0.0, 0.0]:
        raise ValueError("ORB origin is not the physical mouth centre")

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
        raise ValueError("cross-reconstruction artifact incorrectly claims identity")
    if abs(np.linalg.det(matrix[:3, :3]) - artifact["determinant"]) > 1e-6:
        raise ValueError("registration determinant does not match T_ORB_FROM_B")
    stats = artifact["orb_point_to_b_surface_mm"]
    if artifact["landmark_rms_mm"] > 2.0 or stats["p95_mm"] > 12.0:
        raise ValueError("real landmark/surface registration exceeds contract")
    if artifact["up_axis_agreement"] < 0.995 or artifact["front_axis_agreement"] < 0.995:
        raise ValueError("up/front orientation contract failed")
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
        raise ValueError(f"runtime contains prohibited v39 logic: {found}")
    for token in (
        "ModelRegistrationEvidence.TryParse",
        "ConfidenceWeightedPoseFusion.Step",
        "hierarchyTransformRoundTripVerified",
        "AssertStartPoseUnchanged",
        "ShowRepairPresentation",
        "SetReferenceHierarchyVisible(false)",
    ):
        if token not in controller:
            raise ValueError(f"managed v40 contract missing {token}")
    for token in ("SetPosePrior", "SOLVEPNP_SQPNP", "urp_orb_get_last_inliers"):
        if token not in native:
            raise ValueError(f"native baseline changed: missing {token}")
    if "BottleRepairAR_v40.apk" not in setup:
        raise ValueError("setup does not build v40")
    if "v40" not in app_controller:
        raise ValueError("tracking screen does not display v40")
    for token in (
        "orb-tracking-v40-cross-registered-adaptive-se3",
        "coconut-cross-reconstruction-sim3-v40",
        "bottle-orb-cross-registration-reference-b-v40",
    ):
        if token not in build_identity:
            raise ValueError(f"build identity is stale: {token}")

    print(json.dumps({
        "status": "BOTTLE_ORB_CROSS_REGISTRATION_V40_DATA_OK",
        "database_sha256": sha256(orb),
        "fbx_sha256": sha256(fbx),
        "records": len(cloud),
        "orb_bounds_min": cloud.min(axis=0).tolist(),
        "orb_bounds_max": cloud.max(axis=0).tolist(),
        "mouth_center_orb": artifact["mouth_center_orb"],
        "landmark_rms_mm": artifact["landmark_rms_mm"],
        "surface_mm": stats,
        "T_ORB_FROM_B": artifact["T_ORB_FROM_B"],
        "device_overlay_verified": False,
    }, indent=2))


if __name__ == "__main__":
    main()
