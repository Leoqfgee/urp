"""Deterministic v51 QA for portrait intrinsics and the existing PnP LM refinement."""

from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Calibration" / "v51_pose_math_qa.json"


def rotate_pixel(pixel: np.ndarray, width: int, height: int, degrees: int) -> np.ndarray:
    x, y = pixel
    if degrees == 0:
        return np.array([x, y])
    if degrees == 90:
        return np.array([height - 1.0 - y, x])
    if degrees == 180:
        return np.array([width - 1.0 - x, height - 1.0 - y])
    if degrees == 270:
        return np.array([y, width - 1.0 - x])
    raise ValueError(degrees)


def rotated_intrinsics(k: np.ndarray, width: int, height: int, degrees: int):
    fx, fy, cx, cy = k[0, 0], k[1, 1], k[0, 2], k[1, 2]
    if degrees == 0:
        return k.copy(), width, height
    if degrees == 90:
        return np.array([[fy, 0, height - 1.0 - cy], [0, fx, cx], [0, 0, 1.0]]), height, width
    if degrees == 180:
        return np.array([[fx, 0, width - 1.0 - cx], [0, fy, height - 1.0 - cy], [0, 0, 1.0]]), width, height
    if degrees == 270:
        return np.array([[fy, 0, cy], [0, fx, width - 1.0 - cx], [0, 0, 1.0]]), height, width
    raise ValueError(degrees)


def rotate_camera_xy(point: np.ndarray, degrees: int) -> np.ndarray:
    x, y, z = point
    if degrees == 0:
        return point.copy()
    if degrees == 90:
        return np.array([-y, x, z])
    if degrees == 180:
        return np.array([-x, -y, z])
    if degrees == 270:
        return np.array([y, -x, z])
    raise ValueError(degrees)


def intrinsics_qa():
    width, height = 1920, 1440
    k = np.array([[1412.3, 0, 960.2], [0, 1410.7, 720.1], [0, 0, 1.0]])
    points = np.array([
        [-0.12, -0.18, 0.55], [0.16, -0.07, 0.70], [0.03, 0.21, 0.62],
        [-0.19, 0.11, 0.85], [0.09, 0.04, 0.48],
    ])
    results = {}
    all_errors = []
    for degrees in (0, 90, 180, 270):
        kr, _, _ = rotated_intrinsics(k, width, height, degrees)
        errors = []
        for point in points:
            original = np.array([
                k[0, 0] * point[0] / point[2] + k[0, 2],
                k[1, 1] * point[1] / point[2] + k[1, 2],
            ])
            expected = rotate_pixel(original, width, height, degrees)
            rotated = rotate_camera_xy(point, degrees)
            actual = np.array([
                kr[0, 0] * rotated[0] / rotated[2] + kr[0, 2],
                kr[1, 1] * rotated[1] / rotated[2] + kr[1, 2],
            ])
            errors.append(float(np.linalg.norm(expected - actual)))
        results[str(degrees)] = {
            "rms_px": float(np.sqrt(np.mean(np.square(errors)))),
            "max_px": max(errors),
        }
        all_errors.extend(errors)
    return {
        "rotations": results,
        "overall_rms_px": float(np.sqrt(np.mean(np.square(all_errors)))),
        "pass_threshold_px": 0.1,
        "passed": max(all_errors) < 0.1,
    }


def reprojection_rms(object_points, image_points, k, rvec, tvec):
    projected, _ = cv2.projectPoints(object_points, rvec, tvec, k, None)
    delta = projected.reshape(-1, 2) - image_points
    return float(np.sqrt(np.mean(np.sum(delta * delta, axis=1))))


def pnp_refinement_qa():
    rng = np.random.default_rng(5101)
    object_points = rng.uniform([-0.035, -0.12, -0.025], [0.035, 0.12, 0.025], (96, 3)).astype(np.float64)
    k = np.array([[980.0, 0, 640.0], [0, 982.0, 360.0], [0, 0, 1.0]])
    true_rvec = np.array([[0.12], [-0.19], [0.035]], dtype=np.float64)
    true_tvec = np.array([[0.012], [-0.018], [0.62]], dtype=np.float64)
    image_points, _ = cv2.projectPoints(object_points, true_rvec, true_tvec, k, None)
    image_points = image_points.reshape(-1, 2) + rng.normal(0.0, 0.85, (96, 2))
    image_points[[3, 18, 51, 77]] += rng.normal(0.0, 12.0, (4, 2))
    ok, rvec, tvec, inliers = cv2.solvePnPRansac(
        object_points, image_points, k, None, iterationsCount=300,
        reprojectionError=3.0, confidence=0.999, flags=cv2.SOLVEPNP_ITERATIVE)
    if not ok or inliers is None:
        raise RuntimeError("deterministic solvePnPRansac failed")
    ids = inliers.reshape(-1)
    before = reprojection_rms(object_points[ids], image_points[ids], k, rvec, tvec)
    refined_rvec, refined_tvec = cv2.solvePnPRefineLM(
        object_points[ids], image_points[ids], k, None, rvec.copy(), tvec.copy())
    after = reprojection_rms(object_points[ids], image_points[ids], k, refined_rvec, refined_tvec)
    rotation_delta = float(np.linalg.norm(refined_rvec - rvec) * 180.0 / np.pi)
    translation_delta_mm = float(np.linalg.norm(refined_tvec - tvec) * 1000.0)
    return {
        "ransac_inliers": int(len(ids)),
        "before_refine_rms_px": before,
        "after_refine_rms_px": after,
        "rms_improvement_px": before - after,
        "rotation_delta_deg": rotation_delta,
        "translation_delta_mm": translation_delta_mm,
        "passed": after <= before + 1e-9,
        "runtime_note": "native already applies solvePnPRefineLM to the RANSAC inlier set",
    }


def main():
    artifact = {
        "version": "v51",
        "portrait_intrinsics_rotation": intrinsics_qa(),
        "pnp_refinement": pnp_refinement_qa(),
    }
    OUT.write_text(json.dumps(artifact, indent=2), encoding="utf-8")
    print(json.dumps(artifact, indent=2))
    if not artifact["portrait_intrinsics_rotation"]["passed"] or not artifact["pnp_refinement"]["passed"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
