#!/usr/bin/env python3
"""Reject sparse SfM ORB records unsupported by the same reconstruction surface."""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

import numpy as np
import trimesh


MAGIC = b"URP3DM1\0"
RECORD_SIZE = 44
METERS_PER_MODEL_UNIT = 0.17


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--surface", type=Path, required=True)
    parser.add_argument("--registration", type=Path, required=True)
    parser.add_argument("--maximum-distance-mm", type=float, default=5.0)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()

    payload = args.input.read_bytes()
    if payload[:8] != MAGIC:
        raise ValueError("invalid ORB database magic")
    count = struct.unpack_from("<I", payload, 8)[0]
    if len(payload) != 12 + count * RECORD_SIZE:
        raise ValueError("invalid ORB database length")
    records = [payload[12 + i * RECORD_SIZE:12 + (i + 1) * RECORD_SIZE] for i in range(count)]
    points = np.asarray([struct.unpack_from("<3f", item, 0) for item in records])

    registration = json.loads(args.registration.read_text(encoding="utf-8"))
    transform = np.asarray(registration["T_ORB_FROM_B"], dtype=np.float64).reshape(4, 4)
    surface = trimesh.load(args.surface, force="mesh", process=False)
    surface.apply_transform(transform)
    _closest, distances, triangles = trimesh.proximity.closest_point(surface, points)
    distances_mm = distances * METERS_PER_MODEL_UNIT * 1000.0
    keep = np.isfinite(distances_mm) & (triangles >= 0) & (distances_mm <= args.maximum_distance_mm)
    kept = [record for record, accepted in zip(records, keep) if accepted]
    if len(kept) < 2500:
        raise ValueError(f"only {len(kept)} surface-supported ORB records remain")
    output = bytearray(MAGIC)
    output.extend(struct.pack("<I", len(kept)))
    for record in kept:
        output.extend(record)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(output)
    kept_distances = distances_mm[keep]
    report = {
        "version": "same-reconstruction-surface-supported-orb-v41",
        "source_database": str(args.input),
        "source_database_sha256": hashlib.sha256(payload).hexdigest().upper(),
        "surface_mesh": str(args.surface),
        "surface_mesh_sha256": hashlib.sha256(args.surface.read_bytes()).hexdigest().upper(),
        "registration_artifact": str(args.registration),
        "maximum_distance_mm": args.maximum_distance_mm,
        "records_before": count,
        "records_after": len(kept),
        "records_rejected": count - len(kept),
        "retained_surface_distance_mm": {
            "rms": float(np.sqrt(np.mean(kept_distances ** 2))),
            "median": float(np.median(kept_distances)),
            "p90": float(np.percentile(kept_distances, 90.0)),
            "p95": float(np.percentile(kept_distances, 95.0)),
            "max": float(np.max(kept_distances)),
        },
        "output_database_sha256": hashlib.sha256(output).hexdigest().upper(),
        "descriptor_bytes_modified": False,
        "matching_and_pnp_thresholds_modified": False,
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("ORB_SURFACE_FILTER_V41_OK")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
