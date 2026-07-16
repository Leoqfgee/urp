#!/usr/bin/env python3
"""Create a repeatable complete viewer mesh from the cleaned bottle and cap."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import trimesh


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bottle", type=Path, required=True)
    parser.add_argument("--cap", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    return parser.parse_args()


def load_mesh(path: Path) -> trimesh.Trimesh:
    loaded = trimesh.load(path, force="mesh", process=False)
    if not isinstance(loaded, trimesh.Trimesh) or loaded.is_empty:
        raise ValueError(f"Could not load a non-empty mesh: {path}")
    return loaded


def estimate_opening(vertices: np.ndarray) -> tuple[np.ndarray, float]:
    minimum = vertices.min(axis=0)
    maximum = vertices.max(axis=0)
    threshold = minimum[1] + (maximum[1] - minimum[1]) * 0.90
    top = vertices[vertices[:, 1] >= threshold]
    if len(top) < 20:
        raise ValueError("Not enough upper-neck vertices to estimate the opening")
    x_low, x_high = np.quantile(top[:, 0], [0.05, 0.95])
    z_low, z_high = np.quantile(top[:, 2], [0.05, 0.95])
    center = np.array(
        [(x_low + x_high) * 0.5, top[:, 1].max(), (z_low + z_high) * 0.5]
    )
    diameter = float(max(x_high - x_low, z_high - z_low))
    if diameter <= 0:
        raise ValueError("Estimated opening diameter is not positive")
    return center, diameter


def main() -> None:
    args = parse_args()
    bottle = load_mesh(args.bottle)
    cap = load_mesh(args.cap).copy()
    opening_center, opening_diameter = estimate_opening(
        np.asarray(bottle.vertices, dtype=np.float64)
    )
    cap_bounds = np.asarray(cap.bounds, dtype=np.float64)
    cap_diameter = float(max(cap_bounds[1, 0] - cap_bounds[0, 0],
                             cap_bounds[1, 2] - cap_bounds[0, 2]))
    scale = opening_diameter / cap_diameter
    cap.apply_scale(scale)
    cap_bounds = np.asarray(cap.bounds, dtype=np.float64)
    cap_bottom_center = np.array(
        [
            (cap_bounds[0, 0] + cap_bounds[1, 0]) * 0.5,
            cap_bounds[0, 1],
            (cap_bounds[0, 2] + cap_bounds[1, 2]) * 0.5,
        ]
    )
    translation = opening_center - cap_bottom_center
    cap.apply_translation(translation)

    complete = trimesh.util.concatenate([bottle, cap])
    args.output_dir.mkdir(parents=True, exist_ok=True)
    output = args.output_dir / "complete_bottle_processed.obj"
    complete.export(output)
    report = {
        "bottle": str(args.bottle),
        "cap": str(args.cap),
        "output": str(output),
        "opening_center": opening_center.tolist(),
        "estimated_opening_diameter_model_units": opening_diameter,
        "cap_scale": scale,
        "cap_translation": translation.tolist(),
        "vertices": int(len(complete.vertices)),
        "faces": int(len(complete.faces)),
        "bounds": np.asarray(complete.bounds).tolist(),
        "note": "Viewer composite only; physical scale remains unverified.",
    }
    (args.output_dir / "complete_bottle_processed_report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
