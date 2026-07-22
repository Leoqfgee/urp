#!/usr/bin/env python3
"""Offline replay for the exact URP3DM1 ORB database and Native matching policy.

This intentionally needs no new packages beyond the already installed OpenCV.
It can replay source frames now and the raw frames saved by Development Builds
later. Results are diagnostics, not a substitute for phone-camera acceptance.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import struct
from dataclasses import asdict, dataclass
from pathlib import Path

import cv2
import numpy as np


@dataclass
class Result:
    image: str
    detected_keypoints: int
    ratio_matches: int
    mutual_matches: int
    unique_matches: int
    occupied_grid_cells: int
    coverage_width: float
    coverage_height: float
    model_spread_x: float
    model_spread_y: float
    model_spread_z: float
    solver: str
    pose_inliers: int
    inlier_ratio: float
    reprojection_rms: float
    reprojection_max: float
    positive_depth: bool
    accepted: bool
    rejection: str


def load_model(path: Path) -> tuple[np.ndarray, np.ndarray]:
    data = path.read_bytes()
    if data[:8] != b"URP3DM1\0":
        raise ValueError(f"unexpected model magic in {path}")
    count = struct.unpack_from("<I", data, 8)[0]
    expected = 12 + count * 44
    if len(data) != expected:
        raise ValueError(f"model length {len(data)} != {expected}")
    points = np.empty((count, 3), np.float32)
    descriptors = np.empty((count, 32), np.uint8)
    offset = 12
    for i in range(count):
        points[i] = struct.unpack_from("<3f", data, offset)
        offset += 12
        descriptors[i] = np.frombuffer(data, np.uint8, 32, offset).copy()
        offset += 32
    return points, descriptors


def ratio_best(pairs, ratio: float):
    return [pair[0] for pair in pairs
            if len(pair) >= 2 and pair[0].distance < ratio * pair[1].distance]


def replay(image_path: Path, model_points: np.ndarray, model_desc: np.ndarray,
           ratio: float, minimum_matches: int, output: Path) -> Result:
    encoded = np.fromfile(image_path, dtype=np.uint8)
    image = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"cannot read {image_path}")
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    orb = cv2.ORB_create(1800)
    keypoints, frame_desc = orb.detectAndCompute(gray, None)
    if frame_desc is None or len(keypoints) < 8:
        return Result(str(image_path), len(keypoints), 0, 0, 0, 0, 0, 0,
                      0, 0, 0, "none", 0, 0, 999, 999, False, False,
                      "no_descriptors")

    matcher = cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=False)
    forward = ratio_best(matcher.knnMatch(model_desc, frame_desc, k=2), ratio)
    reverse = ratio_best(matcher.knnMatch(frame_desc, model_desc, k=2), ratio)
    reverse_best = {m.queryIdx: m.trainIdx for m in reverse}
    mutual = [m for m in forward if reverse_best.get(m.trainIdx) == m.queryIdx]
    mutual.sort(key=lambda value: value.distance)

    used_model: set[int] = set()
    used_frame: set[int] = set()
    cells = [0] * (8 * 12)
    good = []
    height, width = gray.shape
    for match in mutual:
        if match.queryIdx in used_model or match.trainIdx in used_frame:
            continue
        x, y = keypoints[match.trainIdx].pt
        col = min(7, max(0, int(x / max(1, width) * 8)))
        row = min(11, max(0, int(y / max(1, height) * 12)))
        cell = row * 8 + col
        if cells[cell] >= 8:
            continue
        cells[cell] += 1
        used_model.add(match.queryIdx)
        used_frame.add(match.trainIdx)
        good.append(match)

    frame_points = np.float32([keypoints[m.trainIdx].pt for m in good])
    object_points = np.float32([model_points[m.queryIdx] for m in good])
    occupied = sum(value > 0 for value in cells)
    if len(good):
        minimum = frame_points.min(axis=0)
        maximum = frame_points.max(axis=0)
        coverage = (maximum - minimum) / np.array([width, height])
        spread = object_points.max(axis=0) - object_points.min(axis=0)
    else:
        coverage = np.zeros(2)
        spread = np.zeros(3)

    best = ("none", 0, 0.0, 999.0, 999.0, False)
    camera = np.array([[width * .9, 0, width * .5],
                       [0, width * .9, height * .5],
                       [0, 0, 1]], np.float64)
    if len(good) >= 6:
        flags = [
            ("EPNP", cv2.SOLVEPNP_EPNP),
            ("SQPNP", cv2.SOLVEPNP_SQPNP),
            ("ITERATIVE", cv2.SOLVEPNP_ITERATIVE),
        ]
        for name, flag in flags:
            try:
                ok, rvec, tvec, inliers = cv2.solvePnPRansac(
                    object_points, frame_points, camera, None,
                    iterationsCount=300, reprojectionError=3.0,
                    confidence=.99, flags=flag)
                count = 0 if inliers is None else int(len(inliers))
                if not ok or count < 6:
                    continue
                indices = inliers.reshape(-1)
                cv2.solvePnPRefineLM(object_points[indices], frame_points[indices],
                                     camera, None, rvec, tvec)
                projected, _ = cv2.projectPoints(object_points[indices], rvec, tvec,
                                                  camera, None)
                errors = np.linalg.norm(projected.reshape(-1, 2)
                                        - frame_points[indices], axis=1)
                rms = float(math.sqrt(np.mean(errors * errors)))
                maximum_error = float(errors.max())
                candidate = (name, count, count / len(good), rms,
                             maximum_error, bool(tvec[2, 0] > 0))
                if candidate[1] > best[1] or (candidate[1] == best[1]
                                                and candidate[3] < best[3]):
                    best = candidate
            except cv2.error:
                pass

    solver, inliers, inlier_ratio, rms, max_error, positive = best
    rejection = "accepted"
    if len(good) < minimum_matches:
        rejection = "unique_matches"
    elif coverage[1] < .18:
        rejection = "vertical_coverage"
    elif coverage[0] < .05:
        rejection = "horizontal_coverage"
    elif occupied < 4:
        rejection = "grid_occupancy"
    elif inliers < min(10, max(6, math.ceil(len(good) * .50))):
        rejection = "pose_inliers"
    elif inlier_ratio < .50:
        rejection = "inlier_ratio"
    elif rms > 3.0:
        rejection = "reprojection_rms"
    elif not positive:
        rejection = "negative_depth"
    elif inliers < 8 and (rms > 1.5 or occupied < 5):
        rejection = "low_count_pose_unstable"
    accepted = rejection == "accepted"

    debug = image.copy()
    for match in good:
        point = tuple(round(v) for v in keypoints[match.trainIdx].pt)
        cv2.circle(debug, point, 4, (0, 255, 0) if accepted else (0, 180, 255), 1)
    cv2.putText(debug,
                f"unique={len(good)} cells={occupied} inliers={inliers} rms={rms:.2f} {rejection}",
                (12, 30), cv2.FONT_HERSHEY_SIMPLEX, .58, (0, 0, 255), 2,
                cv2.LINE_AA)
    extension = output.suffix or ".jpg"
    ok, encoded_debug = cv2.imencode(extension, debug)
    if ok:
        encoded_debug.tofile(output)
    return Result(str(image_path), len(keypoints), len(forward), len(mutual),
                  len(good), occupied, float(coverage[0]), float(coverage[1]),
                  float(spread[0]), float(spread[1]), float(spread[2]), solver,
                  inliers, inlier_ratio, rms, max_error, positive, accepted, rejection)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--frames", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--step", type=int, default=12)
    parser.add_argument("--ratio", type=float, default=.72)
    parser.add_argument("--minimum-matches", type=int, default=9)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    points, descriptors = load_model(args.model)
    frames = sorted(path for path in args.frames.rglob("*")
                    if path.suffix.lower() in {".jpg", ".jpeg", ".png"})[::max(1, args.step)]
    results = [replay(frame, points, descriptors, args.ratio,
                      args.minimum_matches, args.output / f"{frame.stem}_debug.jpg")
               for frame in frames]
    with (args.output / "results.csv").open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(asdict(results[0]).keys()))
        writer.writeheader()
        writer.writerows(asdict(result) for result in results)
    summary = {
        "model": str(args.model),
        "frames": len(results),
        "accepted": sum(result.accepted for result in results),
        "success_rate": sum(result.accepted for result in results) / max(1, len(results)),
        "thresholds": {
            "minimum_unique_matches": args.minimum_matches,
            "minimum_pose_inliers": "adaptive: clamp(ceil(unique*0.50), 6, 10)",
            "minimum_inlier_ratio": .50,
            "low_count_rule": "when inliers < 8: grid >= 5 and RMS <= 1.5px",
            "maximum_reprojection_rms": 3.0,
            "minimum_horizontal_coverage": .05,
            "minimum_vertical_coverage": .18,
            "minimum_occupied_grid_cells": 4,
        },
        "rejections": {reason: sum(r.rejection == reason for r in results)
                       for reason in sorted({r.rejection for r in results})},
        "results": [asdict(result) for result in results],
    }
    (args.output / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: summary[key] for key in
                      ("frames", "accepted", "success_rate", "rejections")},
                     ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
