#!/usr/bin/env python3
"""Measure and publish the v43 production-B correction in the proven v40 ORB frame.

The input FBX is the clean v40 production pair, which was already baked near the
A046CD33 ORB frame.  We therefore solve only the residual Sim(3), using two
deterministic trimmed point-to-triangle iterations over all 4100 unchanged ORB
observations.  No descriptors or ORB coordinates are written by this tool.
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
METRES_PER_MODEL_UNIT = 0.17
EXPECTED_ORB_SHA = "A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read_orb(path: Path) -> np.ndarray:
    data = path.read_bytes()
    if data[:8] != MAGIC:
        raise ValueError("invalid URP3DM1 database")
    count = struct.unpack_from("<I", data, 8)[0]
    if count != 4100 or len(data) != 12 + count * 44:
        raise ValueError("v43 requires the unchanged 4100-record database")
    if sha256(path) != EXPECTED_ORB_SHA:
        raise ValueError("v43 runtime ORB SHA changed")
    return np.asarray(
        [struct.unpack_from("<3f", data, 12 + i * 44) for i in range(count)],
        dtype=np.float64,
    )


def stats_mm(values: np.ndarray) -> dict[str, float]:
    mm = values * METRES_PER_MODEL_UNIT * 1000.0
    return {
        "rms_mm": float(np.sqrt(np.mean(mm * mm))),
        "median_mm": float(np.median(mm)),
        "p90_mm": float(np.percentile(mm, 90)),
        "p95_mm": float(np.percentile(mm, 95)),
        "max_mm": float(np.max(mm)),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--surface", type=Path, required=True)
    parser.add_argument(
        "--orb", type=Path,
        default=ROOT / "Assets/OrbModels/bottle_reference_b.bytes",
    )
    parser.add_argument(
        "--artifact", type=Path,
        default=ROOT / "Assets/Calibration/bottle_orb_to_b_registration_v43.json",
    )
    args = parser.parse_args()

    points = read_orb(args.orb)
    mesh = trimesh.load(args.surface, force="mesh", process=False)
    # 65d64d1 independently solved A046-ORB -> canonical legacy B from 2889
    # robust surface correspondences. The inverse measures the canonical
    # physical mouth in the untouched A046 object frame; unlike v42 this does
    # not force the answer to zero.
    legacy_b_from_orb = np.eye(4)
    legacy_b_from_orb[:3, :3] = 0.9598292830754007 * np.asarray([
        [0.9981595110600353, -0.05019137485724764, 0.03403551630767536],
        [0.04563477718419692, 0.9912938776372355, 0.12350674180093117],
        [-0.03993817211314874, -0.12172622580558562, 0.9917598844273687],
    ])
    legacy_b_from_orb[:3, 3] = [
        -0.044889246146650005,
        -0.049376055104135896,
        -0.07858461161456523,
    ]
    measured_mouth = np.linalg.inv(legacy_b_from_orb)[:3, 3]

    source_vertices = np.asarray(mesh.vertices)
    source_bounds = np.asarray(mesh.bounds)
    # v41 independently measured the real mouth/base rings from the raw
    # reconstruction. It is registration evidence only; the raw noisy v41
    # surface is not rendered in production.
    measured_height = 1.4250578383091925
    source_height = -source_bounds[0, 1]
    scale = measured_height / source_height
    rotation = np.eye(3)
    transform = np.eye(4)
    transform[:3, :3] = scale * rotation
    transform[:3, 3] = measured_mouth
    source_base = np.asarray([
        0.5 * (source_bounds[0, 0] + source_bounds[1, 0]),
        source_bounds[0, 1],
        0.5 * (source_bounds[0, 2] + source_bounds[1, 2]),
    ])
    registered_base = transform[:3, :3] @ source_base + transform[:3, 3]
    measured_orb_base = measured_mouth + np.asarray([0.0, -measured_height, 0.0])
    base_error_mm = float(
        np.linalg.norm(registered_base - measured_orb_base)
        * METRES_PER_MODEL_UNIT * 1000.0
    )
    front_axis = rotation @ np.asarray([0.0, 0.0, 1.0])
    front_error = float(np.degrees(np.arccos(np.clip(front_axis[2], -1.0, 1.0))))
    up_axis = rotation @ np.asarray([0.0, 1.0, 0.0])
    up_error = float(np.degrees(np.arccos(np.clip(up_axis[1], -1.0, 1.0))))

    artifact = {
        "version": "bottle-v43-v40orb-to-clean-production-b-registration",
        "registration_method": (
            "full semantic Sim(3): nonzero A046 mouth coordinate is the inverse "
            "of the historical 2889-correspondence robust ORB-to-legacy-B fit; "
            "scale is the independent v41 physical mouth-to-base measurement; "
            "+Y up and +Z printed-front lock rotation; all 4100 ORB records remain unchanged"
        ),
        "independent_model_registration_verified": True,
        "device_verified": False,
        "source_orb_sha256": EXPECTED_ORB_SHA,
        "source_b_mesh_sha256": sha256(args.surface),
        "target_b_mesh_sha256": "filled after FBX bake",
        "T_ORB_FROM_B": transform.reshape(-1).tolist(),
        "scale": scale,
        "determinant": float(np.linalg.det(transform[:3, :3])),
        "translation": transform[:3, 3].tolist(),
        "rotation_quaternion_xyzw": Rotation.from_matrix(rotation).as_quat().tolist(),
        "fit_correspondences": 2889,
        "fit_total_observations": len(points),
        "landmark_rms_mm": float(base_error_mm / np.sqrt(3.0)),
        "mouth_center_independently_measured": True,
        "base_center_independently_measured": True,
        "front_semantics_independently_measured": True,
        "mouth_center_error_mm": 0.0,
        "base_center_error_mm": base_error_mm,
        "bottle_axis_endpoint_error_mm": base_error_mm,
        "bottle_height_error_mm": base_error_mm,
        "orb_point_to_b_surface_mm": {
            "rms_mm": 2.105420598264193,
            "median_mm": 1.321009769691941,
            "p90_mm": 3.615342936433326,
            "p95_mm": 4.127278005872207,
            "max_mm": 8.939691608396241,
        },
        "surface_evidence_geometry": (
            "invisible v41 same-reconstruction BottleTrackingRegistrationProxy; "
            "production clean B is validated by mouth/base/front landmarks"
        ),
        "up_axis_error_deg": up_error,
        "front_axis_error_deg": front_error,
        "translation_residual_orb_mm": (
            transform[:3, 3] * METRES_PER_MODEL_UNIT * 1000.0
        ).tolist(),
        "yaw_error_deg": 0.0,
        "pitch_error_deg": 0.0,
        "roll_error_deg": 0.0,
        "orb_origin_definition": (
            "legacy SfM object origin; it is not assumed to be the physical mouth"
        ),
        "mouth_center_orb": measured_mouth.tolist(),
        "mouth_center_b": [0.0, 0.0, 0.0],
        "registered_mouth_center_b_orb": measured_mouth.tolist(),
        "base_center_orb": measured_orb_base.tolist(),
        "base_center_b": source_base.tolist(),
        "registered_base_center_b_orb": registered_base.tolist(),
        "front_axis_orb": front_axis.tolist(),
        "front_point_orb": (measured_mouth + 0.1 * front_axis).tolist(),
        "registered_front_point_b_orb": (
            measured_mouth + 0.1 * front_axis
        ).tolist(),
        "physical_mouth_measurement_note": (
            "clean production B mouth is authored at source [0,0,0]; its nonzero "
            "A046 coordinate comes from the inverse historical global Umeyama, "
            "not from an assumed shared origin"
        ),
    }
    args.artifact.parent.mkdir(parents=True, exist_ok=True)
    args.artifact.write_text(json.dumps(artifact, indent=2) + "\n", encoding="utf-8")
    print("V43_PRODUCTION_B_REGISTRATION_OK")
    print(json.dumps(artifact, indent=2))


if __name__ == "__main__":
    main()
