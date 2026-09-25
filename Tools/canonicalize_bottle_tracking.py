#!/usr/bin/env python3
"""Move bottle ORB points and the registered cap into a mouth-centred frame."""

from __future__ import annotations

import json
import struct
from pathlib import Path

import numpy as np
import trimesh


ROOT = Path(__file__).resolve().parents[1]
MAGIC = b"URP3DM1\0"
RECORD_SIZE = 44
MOUTH_ORIGIN = np.asarray([0.419225, -4.514827, 0.314265], dtype=np.float64)
BASIS_ROWS = np.asarray(
    [
        [1.0, 0.0, 0.0],
        [0.0, -1.0, 0.0],
        [0.0, 0.0, -1.0],
    ],
    dtype=np.float64,
)
METERS_PER_MODEL_UNIT = 0.17
CAP_DIAMETER_METERS = 0.039
CAP_HEIGHT_METERS = 0.010


def to_canonical(points: np.ndarray) -> np.ndarray:
    return (BASIS_ROWS @ (points - MOUTH_ORIGIN).T).T


def transform_orb(path: Path) -> str:
    data = bytearray(path.read_bytes())
    if data[:8] != MAGIC:
        raise ValueError(f"{path}: invalid ORB magic")
    count = struct.unpack_from("<I", data, 8)[0]
    if len(data) != 12 + count * RECORD_SIZE:
        raise ValueError(f"{path}: invalid ORB record count")
    points = np.asarray(
        [
            struct.unpack_from("<fff", data, 12 + index * RECORD_SIZE)
            for index in range(count)
        ],
        dtype=np.float64,
    )
    if float(points[:, 1].mean()) > -2.0:
        return "already canonical"
    canonical = to_canonical(points)
    for index, point in enumerate(canonical):
        struct.pack_into(
            "<fff",
            data,
            12 + index * RECORD_SIZE,
            float(point[0]),
            float(point[1]),
            float(point[2]),
        )
    path.write_bytes(data)
    return f"transformed {count} points"


def rebuild_cap() -> dict:
    source_path = ROOT / "Assets/Models/MeshroomCapProcessed/meshroom_cap_processed.obj"
    output_path = ROOT / "Assets/Models/RegisteredRepair/coconut_bottle_cap_registered.obj"
    report_path = (
        ROOT
        / "Assets/Models/RegisteredRepair/coconut_bottle_cap_registration_report.json"
    )
    previous = json.loads(report_path.read_text(encoding="utf-8"))
    matrix = np.asarray(previous["matrix"], dtype=np.float64)
    mesh = trimesh.load(source_path, force="mesh", process=False)
    homogeneous = np.column_stack(
        [np.asarray(mesh.vertices, dtype=np.float64), np.ones(len(mesh.vertices))]
    )
    registered = (matrix @ homogeneous.T).T[:, :3]
    canonical = to_canonical(registered)

    horizontal_center = np.asarray(
        [
            (canonical[:, 0].min() + canonical[:, 0].max()) * 0.5,
            canonical[:, 1].min(),
            (canonical[:, 2].min() + canonical[:, 2].max()) * 0.5,
        ]
    )
    canonical -= horizontal_center
    raw_size = canonical.max(axis=0) - canonical.min(axis=0)
    desired_diameter = CAP_DIAMETER_METERS / METERS_PER_MODEL_UNIT
    desired_height = CAP_HEIGHT_METERS / METERS_PER_MODEL_UNIT
    diameter = max(float(raw_size[0]), float(raw_size[2]))
    scale = np.asarray(
        [
            desired_diameter / diameter,
            desired_height / float(raw_size[1]),
            desired_diameter / diameter,
        ]
    )
    canonical *= scale
    mesh.vertices = canonical
    output_path.parent.mkdir(parents=True, exist_ok=True)
    mesh.export(output_path)

    final_size = canonical.max(axis=0) - canonical.min(axis=0)
    final_report = {
        **previous,
        "output": str(output_path.relative_to(ROOT)),
        "coordinate_system": "bottle_mouth_canonical_x_right_y_up_z_front",
        "source_mouth_origin_sfm": MOUTH_ORIGIN.tolist(),
        "canonical_horizontal_recentering": horizontal_center.tolist(),
        "physical_normalization_scale_xyz": scale.tolist(),
        "final_bounds_model_units": [
            canonical.min(axis=0).tolist(),
            canonical.max(axis=0).tolist(),
        ],
        "final_size_model_units": final_size.tolist(),
        "final_size_meters": (final_size * METERS_PER_MODEL_UNIT).tolist(),
        "dimension_sources": {
            "cap_outer_diameter_m": CAP_DIAMETER_METERS,
            "cap_height_m": CAP_HEIGHT_METERS,
            "measurement_source": "user supplied 2026-07-16",
        },
        "device_overlay_verified": False,
    }
    report_path.write_text(
        json.dumps(final_report, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    return final_report


def main() -> None:
    results = {}
    for path in sorted((ROOT / "Assets/OrbModels").glob("bottle_*.bytes")):
        results[str(path.relative_to(ROOT))] = transform_orb(path)
    cap = rebuild_cap()
    print(json.dumps({"orb": results, "cap": cap}, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
