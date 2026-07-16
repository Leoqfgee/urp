#!/usr/bin/env python3
"""Offline integrity checks for ORB models, OBJ assets, and calibration data."""

from __future__ import annotations

import json
import struct
from pathlib import Path

import numpy as np
import trimesh


ROOT = Path(__file__).resolve().parents[1]


def read_orb(path: Path) -> np.ndarray:
    data = path.read_bytes()
    if data[:8] != b"URP3DM1\0":
        raise ValueError(f"{path}: invalid magic")
    count = struct.unpack_from("<I", data, 8)[0]
    record_size = 44
    expected = 12 + count * record_size
    if len(data) != expected:
        raise ValueError(f"{path}: expected {expected} bytes, got {len(data)}")
    return np.asarray([
        struct.unpack_from("<fff", data, 12 + index * record_size)
        for index in range(count)
    ], dtype=np.float64)


def main() -> None:
    orb_paths = sorted((ROOT / "Assets/OrbModels").glob("bottle_view_*.bytes"))
    if not orb_paths:
        raise SystemExit("no ORB models found")
    ranges = []
    for path in orb_paths:
        points = read_orb(path)
        ranges.append((points.min(axis=0), points.max(axis=0)))
        spread = points.max(axis=0) - points.min(axis=0)
        if len(points) < 45 or np.count_nonzero(spread > 0.05) < 3:
            raise SystemExit(f"{path.name}: insufficient 3D distribution")
    global_min = np.vstack([item[0] for item in ranges]).min(axis=0)
    global_max = np.vstack([item[1] for item in ranges]).max(axis=0)
    if np.any(global_min < np.array([0.10, -4.70, 0.00])) \
            or np.any(global_max > np.array([0.75, -3.00, 0.55])):
        raise SystemExit("ORB models do not share the expected SfM coordinate domain")
    global_model_path = ROOT / "Assets/OrbModels/bottle_global.bytes"
    global_points = read_orb(global_model_path)
    if len(global_points) < 1000:
        raise SystemExit("merged global ORB model contains too few records")

    calibration = json.loads(
        (ROOT / "Tools/calibration/coconut_bottle_repair_calibration.json").read_text(
            encoding="utf-8"
        )
    )
    mouth = np.asarray(calibration["mouth_center_in_model"])
    if np.any(mouth < global_min - 0.2) or np.any(mouth > global_max + 0.2):
        raise SystemExit("mouth frame lies outside the ORB coordinate domain")

    registration = json.loads(
        (
            ROOT
            / "Assets/Models/RegisteredRepair/"
            "coconut_bottle_cap_registration_report.json"
        ).read_text(encoding="utf-8")
    )
    if registration["correspondence_count"] < 4:
        raise SystemExit("cap registration has too few correspondences")
    if registration["rms_model_units"] > 0.001:
        raise SystemExit("cap registration RMS exceeds the configured offline limit")

    mesh_reports = {}
    for path in sorted((ROOT / "Assets/Models").glob("*Processed/*.obj")):
        mesh = trimesh.load(path, force="mesh", process=False)
        components = trimesh.graph.connected_components(
            mesh.face_adjacency,
            nodes=np.arange(len(mesh.faces)),
            min_len=1,
        )
        mesh_reports[str(path.relative_to(ROOT))] = {
            "vertices": int(len(mesh.vertices)),
            "faces": int(len(mesh.faces)),
            "components": int(len(components)),
            "bounds": mesh.bounds.tolist(),
        }
        if len(mesh.faces) == 0:
            raise SystemExit(f"{path}: empty mesh")

    report = {
        "orb_model_count": len(orb_paths),
        "merged_orb_record_count": len(global_points),
        "orb_global_min": global_min.tolist(),
        "orb_global_max": global_max.tolist(),
        "calibration_status": calibration["status"],
        "cap_registration_rms_model_units": registration["rms_model_units"],
        "mesh_reports": mesh_reports,
    }
    output = ROOT / "Builds/validation-report.json"
    output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
