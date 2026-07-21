#!/usr/bin/env python3
"""Register real-photo ORB observations onto Blender reference model b.

The descriptors come from real observations of bottle a (the natural features
that survive camera/domain changes).  Their 3D records are projected to the
surface of the Blender-registered no-cap bottle b.  Runtime solvePnP therefore
estimates b's canonical pose; registered child c follows that pose while b is
hidden.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import subprocess
from pathlib import Path

import numpy as np


MAGIC = b"URP3DM1\0"
VERSION = "coconut-reference-b-observed-v2"


def parse_args() -> argparse.Namespace:
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument("--blender", type=Path,
                        default=Path(r"F:\Program Files\Blender 4.5 LTS\blender.exe"))
    parser.add_argument("--blend", type=Path,
                        default=Path(r"F:\Au\暑期任务\bottle0720\processed_20260721\b_c_registration\b_c_registered_canonical.blend"))
    parser.add_argument("--input", type=Path,
                        default=root / "Assets" / "OrbModels" / "bottle_global.bytes")
    parser.add_argument("--output", type=Path,
                        default=root / "Assets" / "OrbModels" / "bottle_reference_b.bytes")
    parser.add_argument("--work", type=Path,
                        default=root / "Tools" / "TrackingReferenceB" / "observed_registration")
    parser.add_argument("--maximum-surface-distance", type=float, default=0.12)
    parser.add_argument(
        "--snap-to-rough-surface",
        action="store_true",
        help="Diagnostic only. Snapping degrades PnP because b is a rough mesh; the default preserves SfM geometry.",
    )
    return parser.parse_args()


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read_database(path: Path) -> tuple[np.ndarray, np.ndarray]:
    data = path.read_bytes()
    if data[:8] != MAGIC:
        raise ValueError(f"Invalid ORB database magic: {path}")
    count = struct.unpack_from("<I", data, 8)[0]
    if len(data) != 12 + count * 44:
        raise ValueError(f"Invalid ORB database length: {path}")
    points = np.empty((count, 3), np.float32)
    descriptors = np.empty((count, 32), np.uint8)
    for index in range(count):
        offset = 12 + index * 44
        points[index] = struct.unpack_from("<3f", data, offset)
        descriptors[index] = np.frombuffer(data, np.uint8, 32, offset + 12).copy()
    return points, descriptors


def write_database(path: Path, points: np.ndarray, descriptors: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as handle:
        handle.write(MAGIC)
        handle.write(struct.pack("<I", len(points)))
        for point, descriptor in zip(points.astype(np.float32), descriptors.astype(np.uint8)):
            handle.write(struct.pack("<3f", *point))
            handle.write(descriptor.tobytes())


def fit_similarity(source: np.ndarray, target: np.ndarray):
    """Robust Umeyama source->target similarity; preserves PnP geometry."""
    keep = np.ones(len(source), dtype=bool)
    scale = 1.0
    rotation = np.eye(3)
    translation = np.zeros(3)
    for _ in range(3):
        src = source[keep]
        dst = target[keep]
        src_mean = src.mean(axis=0)
        dst_mean = dst.mean(axis=0)
        src_centered = src - src_mean
        dst_centered = dst - dst_mean
        covariance = dst_centered.T @ src_centered / len(src)
        u, singular, vt = np.linalg.svd(covariance)
        sign = np.ones(3)
        if np.linalg.det(u @ vt) < 0:
            sign[-1] = -1
        rotation = u @ np.diag(sign) @ vt
        variance = np.mean(np.sum(src_centered * src_centered, axis=1))
        scale = float(np.sum(singular * sign) / variance)
        translation = dst_mean - scale * (rotation @ src_mean)
        transformed = (scale * (rotation @ source.T)).T + translation
        errors = np.linalg.norm(transformed - target, axis=1)
        median = np.median(errors[keep])
        mad = np.median(np.abs(errors[keep] - median)) + 1e-9
        keep = errors <= median + 2.5 * 1.4826 * mad
    return scale, rotation, translation, keep


def main() -> None:
    args = parse_args()
    root = Path(__file__).resolve().parents[2]
    blender_script = Path(__file__).with_name("render_reference_b_orb_views.py")
    points, descriptors = read_database(args.input)
    args.work.mkdir(parents=True, exist_ok=True)
    request = args.work / "canonical_points.json"
    request.write_text(json.dumps({"points": points.tolist()}) + "\n", encoding="utf-8")
    command = [
        str(args.blender), "--background", str(args.blend), "--python", str(blender_script),
        "--", "--mode", "nearest", "--output", str(args.work), "--keypoints", str(request),
    ]
    completed = subprocess.run(command, check=False)
    if completed.returncode != 0:
        raise RuntimeError(f"Blender registration failed with exit code {completed.returncode}")
    result = json.loads((args.work / "registered_observed_points.json").read_text(encoding="utf-8"))
    registered = np.asarray([
        item["registered_canonical"] if item["registered_canonical"] is not None
        else [np.nan, np.nan, np.nan]
        for item in result["points"]
    ], np.float32)
    distances = np.asarray([
        item["distance_model_units"] if item["distance_model_units"] is not None else np.inf
        for item in result["points"]
    ], np.float32)
    surface_valid = np.isfinite(registered).all(axis=1) & (distances <= args.maximum_surface_distance)
    if int(surface_valid.sum()) < 1000:
        raise RuntimeError(
            f"Only {int(surface_valid.sum())} observed records register to b within "
            f"{args.maximum_surface_distance} model units"
        )
    # Fit one global similarity from the SfM observations to Blender b. Unlike
    # per-point snapping, this is a real a/b model registration and preserves
    # all rigid 3D relationships required by solvePnP.
    scale, rotation, translation, fit_keep = fit_similarity(
        points[surface_valid].astype(np.float64),
        registered[surface_valid].astype(np.float64),
    )
    similarity_points = (scale * (rotation @ points.astype(np.float64).T)).T + translation
    output_points = registered[surface_valid] if args.snap_to_rough_surface else similarity_points
    output_descriptors = descriptors[surface_valid] if args.snap_to_rough_surface else descriptors
    write_database(args.output, output_points, output_descriptors)

    kept_distances = distances[surface_valid]
    manifest = {
        "version": VERSION,
        "logic": "real bottle a observation descriptors are globally similarity-registered into no-cap Blender model b coordinates; solvePnP estimates b; only registered child c is rendered",
        "input_observation_database": str(args.input.relative_to(root)),
        "input_sha256": sha(args.input),
        "output": str(args.output.relative_to(root)),
        "output_sha256": sha(args.output),
        "input_records": len(points),
        "registered_records": len(output_points),
        "rejected_records": int(len(points) - len(output_points)),
        "registration_strategy": (
            "diagnostic nearest-surface snapping"
            if args.snap_to_rough_surface
            else "one robust global similarity maps SfM observation geometry into Blender b coordinates"
        ),
        "source_to_reference_b_similarity": {
            "scale": scale,
            "rotation_row_major": rotation.reshape(-1).tolist(),
            "translation": translation.tolist(),
            "fit_correspondences": int(fit_keep.sum()),
        },
        "surface_qa_records_within_tolerance": int(surface_valid.sum()),
        "maximum_surface_distance_model_units": args.maximum_surface_distance,
        "surface_distance_mean_model_units": float(kept_distances.mean()),
        "surface_distance_rms_model_units": float(np.sqrt(np.mean(kept_distances ** 2))),
        "surface_distance_max_model_units": float(kept_distances.max()),
        "canonical_bounds_min": output_points.min(axis=0).tolist(),
        "canonical_bounds_max": output_points.max(axis=0).tolist(),
        "registered_blend": str(args.blend),
        "registered_blend_sha256": sha(args.blend),
        "reference_b_objects": [
            "ReferenceBottleB_OriginalPhotogrammetry",
            "ReferenceBottleB_RegisteredOpenMouth",
        ],
        "repair_c_excluded_from_matching": True,
        "meters_per_model_unit": 0.17,
        "device_overlay_verified": False,
    }
    manifest_path = args.output.with_name(args.output.stem + "_manifest.json")
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
