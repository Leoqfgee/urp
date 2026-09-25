#!/usr/bin/env python3
"""Remove 3D ORB records outside the measured canonical bottle envelope."""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

import numpy as np


MAGIC = b"URP3DM1\0"
PROFILE = np.asarray([
    (-1.200, 0.000), (-1.198, 0.105), (-1.190, 0.160),
    (-1.175, 0.182), (-1.150, 0.195), (-1.080, 0.200),
    (-0.900, 0.198), (-0.420, 0.195), (-0.300, 0.188),
    (-0.235, 0.174), (-0.180, 0.145), (-0.135, 0.115),
    (-0.112, 0.100), (-0.100, 0.095), (0.000, 0.095),
], dtype=np.float64)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    data = args.input.read_bytes()
    if data[:8] != MAGIC:
        raise ValueError("invalid database magic")
    count = struct.unpack_from("<I", data, 8)[0]
    records = [data[12 + i * 44:12 + (i + 1) * 44] for i in range(count)]
    points = np.asarray([struct.unpack_from("<3f", record) for record in records])
    expected_radius = np.interp(np.clip(points[:, 1], -1.198, 0.0),
                                PROFILE[:, 0], PROFILE[:, 1])
    radial = np.hypot(points[:, 0], points[:, 2])
    keep = ((points[:, 1] >= -1.220) & (points[:, 1] <= 0.020)
            & (radial <= expected_radius + 0.045))
    filtered_records = [record for record, accepted in zip(records, keep) if accepted]
    output = MAGIC + struct.pack("<I", len(filtered_records)) + b"".join(filtered_records)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(output)
    kept_points = points[keep]
    report = {
        "version": "coconut-damaged-canonical-v3",
        "input": str(args.input),
        "output": str(args.output),
        "input_sha256": sha(data),
        "output_sha256": sha(output),
        "input_records": count,
        "kept_records": len(filtered_records),
        "removed_records": int(count - len(filtered_records)),
        "canonical_bounds_min": kept_points.min(axis=0).tolist(),
        "canonical_bounds_max": kept_points.max(axis=0).tolist(),
        "filter": {
            "y_model_units": [-1.220, 0.020],
            "outer_radial_margin_model_units": 0.045,
            "inner_surface_margin_model_units": None,
            "envelope_source": "measured clean bottle lathe profile",
        },
        "source_provenance": {
            "intended_photo_set": "bottle_damaged (open/no-cap bottle)",
            "sfm_source_file_present_in_repository": False,
            "complete_bottle_or_cap_images_intentionally_used": False,
            "limitation": "A 3D envelope rejects external background geometry; points inside the bottle envelope cannot be semantically proven without the original SfM observation file and masks.",
        },
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
