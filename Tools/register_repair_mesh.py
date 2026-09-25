#!/usr/bin/env python3
"""Register a repair OBJ into a target object frame using a similarity transform."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import trimesh


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-obj", type=Path, required=True)
    parser.add_argument("--correspondences", type=Path, required=True)
    parser.add_argument("--output-obj", type=Path, required=True)
    parser.add_argument("--output-report", type=Path, required=True)
    parser.add_argument("--max-rms", type=float, default=0.01)
    return parser.parse_args()


def solve_similarity(source: np.ndarray, target: np.ndarray) -> tuple[float, np.ndarray, np.ndarray]:
    if source.shape != target.shape or source.ndim != 2 or source.shape[1] != 3:
        raise ValueError("source and target points must be Nx3 arrays")
    if len(source) < 4:
        raise ValueError("at least four correspondences are required")

    source_mean = source.mean(axis=0)
    target_mean = target.mean(axis=0)
    source_centered = source - source_mean
    target_centered = target - target_mean
    covariance = target_centered.T @ source_centered / len(source)
    u, singular, vt = np.linalg.svd(covariance)
    sign = np.eye(3)
    if np.linalg.det(u @ vt) < 0:
        sign[-1, -1] = -1
    rotation = u @ sign @ vt
    variance = np.sum(source_centered**2) / len(source)
    if variance <= 1e-12:
        raise ValueError("source points do not span a usable volume")
    scale = float(np.trace(np.diag(singular) @ sign) / variance)
    translation = target_mean - scale * (rotation @ source_mean)
    return scale, rotation, translation


def main() -> None:
    args = parse_args()
    data = json.loads(args.correspondences.read_text(encoding="utf-8"))
    source = np.asarray(data["source_points"], dtype=np.float64)
    target = np.asarray(data["target_points"], dtype=np.float64)
    scale, rotation, translation = solve_similarity(source, target)
    transformed = (scale * (rotation @ source.T)).T + translation
    errors = np.linalg.norm(transformed - target, axis=1)
    rms = float(np.sqrt(np.mean(errors**2)))
    if not np.isfinite(rms) or rms > args.max_rms:
        raise SystemExit(f"registration RMS {rms:.6f} exceeds limit {args.max_rms:.6f}")

    mesh = trimesh.load(args.source_obj, force="mesh", process=False)
    mesh.vertices = (scale * (rotation @ mesh.vertices.T)).T + translation
    args.output_obj.parent.mkdir(parents=True, exist_ok=True)
    mesh.export(args.output_obj)

    matrix = np.eye(4)
    matrix[:3, :3] = scale * rotation
    matrix[:3, 3] = translation
    report = {
        "source": str(args.source_obj),
        "output": str(args.output_obj),
        "scale": scale,
        "rotation": rotation.tolist(),
        "translation": translation.tolist(),
        "matrix": matrix.tolist(),
        "rms_model_units": rms,
        "max_error_model_units": float(errors.max()),
        "correspondence_count": int(len(source)),
    }
    args.output_report.parent.mkdir(parents=True, exist_ok=True)
    args.output_report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
