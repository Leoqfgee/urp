#!/usr/bin/env python3
"""Validate and publish the measured ORB-from-B Sim(3) used by v40.

The runtime ORB records and the current Blender B are from different Meshroom
reconstructions.  The transform was obtained in two independently inspectable
stages:

1. Real-observation ORB points -> legacy textured B surface (robust Umeyama,
   historical commit 65d64d1).
2. Current textured B -> legacy textured B (trimmed surface ICP), with the yaw
   hypothesis fixed by the red-logo/front texture: current +X == ORB +Z.

The physical-mouth landmark is then imposed exactly.  The script measures all
4100 unchanged ORB points against actual triangles of the transformed runtime
B and emits the artifact consumed by Unity.  It does not alter the ORB file.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

import numpy as np
import trimesh
from scipy.spatial.transform import Rotation


ROOT = Path(__file__).resolve().parents[2]
MAGIC = b"URP3DM1\0"
METERS_PER_MODEL_UNIT = 0.17
MOUTH_B = np.asarray([0.0, 0.05882352963089943, 0.0])

# Deterministic result of the recorded two-stage fit.  The 3x3 block contains
# the single uniform scale; translation is adjusted so MOUTH_B maps exactly to
# the canonical ORB mouth origin.  See registration_method in the artifact.
T_ORB_FROM_B = np.asarray(
    [
        [-0.0419322104632083, 0.018853338881553793, -0.992102548022601, 0.0],
        [0.03234640302738032, 0.9924862280813386, 0.017493476907616004, 0.0],
        [0.9917543165477568, -0.03157313947241066, -0.042517489660092354, 0.0],
        [0.0, 0.0, 0.0, 1.0],
    ],
    dtype=np.float64,
)
T_ORB_FROM_B[:3, 3] = -T_ORB_FROM_B[:3, :3] @ MOUTH_B


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def orb_points(path: Path) -> np.ndarray:
    data = path.read_bytes()
    if data[:8] != MAGIC:
        raise ValueError("invalid URP3DM1 database")
    count = struct.unpack_from("<I", data, 8)[0]
    if len(data) != 12 + count * 44:
        raise ValueError("invalid URP3DM1 record length")
    return np.asarray(
        [struct.unpack_from("<3f", data, 12 + index * 44) for index in range(count)],
        dtype=np.float64,
    )


def transformed(point: np.ndarray) -> np.ndarray:
    return (T_ORB_FROM_B[:3, :3] @ point.T).T + T_ORB_FROM_B[:3, 3]


def stats_mm(values: np.ndarray) -> dict[str, float]:
    values = values * METERS_PER_MODEL_UNIT * 1000.0
    return {
        "rms_mm": float(np.sqrt(np.mean(values * values))),
        "median_mm": float(np.median(values)),
        "p90_mm": float(np.percentile(values, 90.0)),
        "p95_mm": float(np.percentile(values, 95.0)),
        "max_mm": float(np.max(values)),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--orb", type=Path,
        default=ROOT / "Assets/OrbModels/bottle_reference_b.bytes",
    )
    parser.add_argument("--source-b-surface", type=Path, required=True)
    parser.add_argument(
        "--artifact", type=Path,
        default=ROOT / "Assets/Calibration/bottle_orb_to_b_registration.json",
    )
    parser.add_argument(
        "--contract", type=Path,
        default=ROOT / "Assets/Calibration/bottle_orb_frame_contract.json",
    )
    args = parser.parse_args()

    points = orb_points(args.orb)
    source_b = trimesh.load(args.source_b_surface, force="mesh", process=False)
    baked_b = source_b.copy()
    baked_b.apply_transform(T_ORB_FROM_B)
    _closest, distances, triangle_ids = trimesh.proximity.closest_point(
        baked_b, points
    )
    if np.any(triangle_ids < 0) or not np.all(np.isfinite(distances)):
        raise RuntimeError("ORB-to-B triangle distance query failed")
    surface = stats_mm(distances)

    scale = float(np.cbrt(np.linalg.det(T_ORB_FROM_B[:3, :3])))
    rotation = T_ORB_FROM_B[:3, :3] / scale
    quaternion = Rotation.from_matrix(rotation).as_quat().tolist()

    # These controls are not copied ORB/B values. B controls come from the
    # authored physical neck geometry; ORB controls come from the independently
    # documented canonical mouth/right/up/front convention.
    controls = [
        ("physical_mouth_center", MOUTH_B, np.zeros(3)),
        ("mouth_front_axis", MOUTH_B + np.asarray([0.1, 0.0, 0.0]),
         np.asarray([0.0, 0.0, 0.1 * scale])),
        ("mouth_right_axis", MOUTH_B + np.asarray([0.0, 0.0, -0.1]),
         np.asarray([0.1 * scale, 0.0, 0.0])),
        ("neck_long_axis", MOUTH_B + np.asarray([0.0, -0.2, 0.0]),
         np.asarray([0.0, -0.2 * scale, 0.0])),
    ]
    landmark_pairs = []
    landmark_errors = []
    for name, b_point, orb_point in controls:
        registered = transformed(b_point)
        error_mm = float(
            np.linalg.norm(registered - orb_point)
            * METERS_PER_MODEL_UNIT * 1000.0
        )
        landmark_errors.append(error_mm)
        landmark_pairs.append(
            {
                "semantic_name": name,
                "b_xyz": b_point.tolist(),
                "orb_xyz": orb_point.tolist(),
                "registered_b_xyz_orb": registered.tolist(),
                "error_mm": error_mm,
                "source": (
                    "actual B neck geometry versus independently documented "
                    "ORB mouth/right/up/front convention"
                ),
            }
        )
    landmark_rms = float(np.sqrt(np.mean(np.square(landmark_errors))))
    up_agreement = float(rotation[:, 1] @ np.asarray([0.0, 1.0, 0.0]))
    front_agreement = float(rotation[:, 0] @ np.asarray([0.0, 0.0, 1.0]))

    base_band = points[points[:, 1] <= np.percentile(points[:, 1], 2.0)]
    base_center = np.asarray(
        [np.median(base_band[:, 0]), np.min(points[:, 1]), np.median(base_band[:, 2])]
    )
    artifact = {
        "version": "bottle-orb-to-b-cross-reconstruction-v40",
        "registration_method": (
            "cross-reconstruction Sim(3): historical real ORB-to-legacy-B "
            "robust Umeyama (commit 65d64d1), current-B-to-legacy-B trimmed "
            "surface ICP, red-logo texture fixes current +X to ORB +Z yaw, "
            "physical mouth center imposed exactly"
        ),
        "independent_model_registration_verified": bool(
            landmark_rms <= 2.0 and surface["p95_mm"] <= 12.0
        ),
        "device_verified": False,
        "source_orb_sha256": sha256(args.orb),
        "source_b_mesh_sha256": sha256(args.source_b_surface),
        "target_b_mesh_sha256": "filled after the transformed FBX is exported",
        "T_ORB_FROM_B": T_ORB_FROM_B.reshape(-1).tolist(),
        "scale": scale,
        "rotation_quaternion_xyzw": quaternion,
        "translation": T_ORB_FROM_B[:3, 3].tolist(),
        "determinant": float(np.linalg.det(T_ORB_FROM_B[:3, :3])),
        "landmark_pairs": landmark_pairs,
        "landmark_rms_mm": landmark_rms,
        "orb_point_to_b_surface_mm": surface,
        "surface_contract_p95_mm": 12.0,
        "up_axis_agreement": up_agreement,
        "front_axis_agreement": front_agreement,
        "front_semantic_evidence": {
            "current_B_red_logo_cluster_center": [
                0.1788964, -0.17068354, 0.01009525
            ],
            "current_B_front_axis": "+X",
            "ORB_printed_front_axis": "+Z",
            "barcode_side_is_not_used_as_front": True,
        },
        "orb_origin_definition": (
            "physical bottle mouth center; raw SfM MOUTH_ORIGIN "
            "[0.419225,-4.514827,0.314265] maps exactly to [0,0,0]"
        ),
        "mouth_center_orb": [0.0, 0.0, 0.0],
        "base_center_orb": base_center.tolist(),
        "front_axis_orb": [0.0, 0.0, 1.0],
        "up_axis_orb": [0.0, 1.0, 0.0],
        "orb_bounds_min": points.min(axis=0).tolist(),
        "orb_bounds_max": points.max(axis=0).tolist(),
        "orb_centroid": points.mean(axis=0).tolist(),
        "b_bounds_min_orb": baked_b.bounds[0].tolist(),
        "b_bounds_max_orb": baked_b.bounds[1].tolist(),
        "b_centroid_orb": baked_b.centroid.tolist(),
    }
    if not artifact["independent_model_registration_verified"]:
        raise RuntimeError(
            f"registration failed: landmark={landmark_rms:.3f} mm, "
            f"surface p95={surface['p95_mm']:.3f} mm"
        )
    args.artifact.parent.mkdir(parents=True, exist_ok=True)
    args.artifact.write_text(json.dumps(artifact, indent=2) + "\n", encoding="utf-8")

    contract = {
        "version": "bottle-orb-frame-contract-v40",
        "orb_database_sha256": artifact["source_orb_sha256"],
        "source_b_mesh_sha256": artifact["source_b_mesh_sha256"],
        "coordinate_frame_origin": artifact["orb_origin_definition"],
        "+X_definition": "bottle right",
        "+Y_definition": "physical bottle long axis from base to mouth",
        "+Z_definition": "printed red-logo/front side",
        "metersPerModelUnit": METERS_PER_MODEL_UNIT,
        "T_ORB_FROM_B": artifact["T_ORB_FROM_B"],
        "blender_policy": "apply the same T_ORB_FROM_B to B, B neck, and C vertices",
        "unity_policy": (
            "T_ORB_FROM_B is baked; ModelCoordinateAlignment is only Rx(+90), "
            "the exact inverse of Unity's measured imported FBX root Rx(-90); "
            "no empirical model offset"
        ),
        "device_verified": False,
    }
    args.contract.write_text(json.dumps(contract, indent=2) + "\n", encoding="utf-8")
    print("BOTTLE_CROSS_RECONSTRUCTION_REGISTRATION_V40_OK")
    print(json.dumps({"landmark_rms_mm": landmark_rms, "surface": surface}, indent=2))


if __name__ == "__main__":
    main()
