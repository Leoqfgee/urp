#!/usr/bin/env python3
"""Compare grouped-ORB PnP orientation with Meshroom camera ground truth.

The existing replay checks only match count and reprojection error.  A nearly
cylindrical bottle can still admit a visually wrong front/back pose with a low
reprojection error.  This audit reconstructs the expected canonical-model to
camera rotation from the same SfM solution used to build the database and
reports angular error for every tested frame.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import cv2
import numpy as np

import build_orb_database_by_optical_flow as builder
import replay_grouped_orb
import replay_orb


def rotation_error_degrees(actual: np.ndarray, expected: np.ndarray) -> float:
    delta = actual @ expected.T
    cosine = np.clip((np.trace(delta) - 1.0) * 0.5, -1.0, 1.0)
    return math.degrees(math.acos(float(cosine)))


def match_group(
    points: np.ndarray,
    descriptors: np.ndarray,
    keypoints: list[cv2.KeyPoint],
    frame_descriptors: np.ndarray,
    ratio: float,
    width: int,
    height: int,
) -> tuple[np.ndarray, np.ndarray]:
    matcher = cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=False)
    pairs = matcher.knnMatch(descriptors, frame_descriptors, k=2)
    candidates = [
        pair[0]
        for pair in pairs
        if len(pair) >= 2 and pair[0].distance < ratio * pair[1].distance
    ]
    candidates.sort(key=lambda value: value.distance)
    used_model: set[int] = set()
    used_frame: set[int] = set()
    cells = [0] * (8 * 12)
    matches = []
    for match in candidates:
        if match.queryIdx in used_model or match.trainIdx in used_frame:
            continue
        x, y = keypoints[match.trainIdx].pt
        column = min(7, max(0, int(x / max(1, width) * 8)))
        row = min(11, max(0, int(y / max(1, height) * 12)))
        cell = row * 8 + column
        if cells[cell] >= 8:
            continue
        cells[cell] += 1
        used_model.add(match.queryIdx)
        used_frame.add(match.trainIdx)
        matches.append(match)
    return (
        np.float32([points[match.queryIdx] for match in matches]),
        np.float32([keypoints[match.trainIdx].pt for match in matches]),
    )


def solve_group(
    object_points: np.ndarray,
    image_points: np.ndarray,
    camera: np.ndarray,
    distortion: np.ndarray,
) -> dict | None:
    if len(object_points) < 8:
        return None
    best = None
    for name, flag in (
        ("SQPNP", cv2.SOLVEPNP_SQPNP),
        ("EPNP", cv2.SOLVEPNP_EPNP),
        ("ITERATIVE", cv2.SOLVEPNP_ITERATIVE),
    ):
        try:
            solved, rvec, tvec, inliers = cv2.solvePnPRansac(
                object_points,
                image_points,
                camera,
                distortion,
                iterationsCount=400,
                reprojectionError=4.0,
                confidence=0.995,
                flags=flag,
            )
            if not solved or inliers is None or len(inliers) < 4:
                continue
            indices = inliers.reshape(-1)
            cv2.solvePnPRefineLM(
                object_points[indices],
                image_points[indices],
                camera,
                distortion,
                rvec,
                tvec,
            )
            projected, _ = cv2.projectPoints(
                object_points[indices],
                rvec,
                tvec,
                camera,
                distortion,
            )
            errors = np.linalg.norm(
                projected.reshape(-1, 2) - image_points[indices],
                axis=1,
            )
            rms = float(np.sqrt(np.mean(errors * errors)))
            ratio = len(indices) / len(object_points)
            score = len(indices) * 4.0 + ratio * 12.0 - rms
            candidate = {
                "solver": name,
                "matches": len(object_points),
                "inliers": len(indices),
                "inlierRatio": ratio,
                "rms": rms,
                "score": score,
                "rotation": cv2.Rodrigues(rvec)[0],
                "translation": tvec.reshape(3),
            }
            if best is None or candidate["score"] > best["score"]:
                best = candidate
        except cv2.error:
            continue
    return best


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--sfm", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--frame-modulo", type=int, default=12)
    parser.add_argument("--frame-residue", type=int, default=1)
    parser.add_argument("--ratio", type=float, default=0.72)
    parser.add_argument("--shortlist-groups", type=int, default=24)
    args = parser.parse_args()

    sfm = json.loads(args.sfm.read_text(encoding="utf-8"))
    report = json.loads(args.report.read_text(encoding="utf-8"))
    groups = replay_grouped_orb.load_grouped(args.model)
    poses = {str(item["poseId"]): item for item in sfm["poses"]}
    intrinsics = {
        str(item["intrinsicId"]): item for item in sfm["intrinsics"]
    }
    views = sorted(sfm["views"], key=lambda item: int(item["frameId"]))
    tested_views = [
        view
        for view in views
        if str(view["poseId"]) in poses
        and int(view["frameId"]) % args.frame_modulo == args.frame_residue
    ]

    transform = np.asarray(
        report["canonicalTransform"]["matrixRowMajor"],
        dtype=np.float64,
    ).reshape(4, 4)
    scale = float(report["canonicalTransform"]["sfmUnitsToModelUnits"])
    basis = transform[:3, :3] / scale
    matcher = cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=False)
    coarse_rows = []
    coarse_group_indices = []
    for group_index, (_, _, descriptors) in enumerate(groups):
        count = min(40, len(descriptors))
        indices = np.linspace(0, len(descriptors) - 1, count, dtype=np.int32)
        coarse_rows.append(descriptors[indices])
        coarse_group_indices.extend([group_index] * count)
    coarse_descriptors = np.concatenate(coarse_rows)
    coarse_group_indices = np.asarray(coarse_group_indices, np.int32)

    results = []
    for view in tested_views:
        path = Path(view["path"])
        image, _, keypoints, frame_descriptors = replay_orb.prepare_frame(path)
        if frame_descriptors is None:
            continue
        height, width = image.shape[:2]
        original_width = float(view["width"])
        resize_scale = width / original_width
        camera, distortion = builder.intrinsic_matrix(
            intrinsics[str(view["intrinsicId"])],
            resize_scale,
        )
        pairs = matcher.knnMatch(coarse_descriptors, frame_descriptors, k=2)
        scores = np.zeros(len(groups), np.float32)
        for row, pair in enumerate(pairs):
            if len(pair) >= 2 and pair[0].distance < 0.80 * pair[1].distance:
                scores[coarse_group_indices[row]] += (
                    1.0 + (256.0 - pair[0].distance) / 256.0
                )
        ranked = sorted(
            range(len(groups)),
            key=lambda index: float(scores[index]),
            reverse=True,
        )
        shortlist = ranked[: max(1, min(args.shortlist_groups, len(ranked)))]

        transform_pose = poses[str(view["poseId"])]["pose"]["transform"]
        camera_to_world = np.asarray(
            transform_pose["rotation"],
            dtype=np.float64,
        ).reshape(3, 3)
        expected = camera_to_world.T @ basis.T
        best = None
        best_prior_constrained = None
        for group_index in shortlist:
            group_id, points, descriptors = groups[group_index]
            object_points, image_points = match_group(
                points,
                descriptors,
                keypoints,
                frame_descriptors,
                args.ratio,
                width,
                height,
            )
            candidate = solve_group(
                object_points,
                image_points,
                camera,
                distortion,
            )
            if candidate is not None and (
                best is None or candidate["score"] > best["score"]
            ):
                candidate["group"] = group_id
                best = candidate
            if candidate is not None:
                prior_error = rotation_error_degrees(
                    candidate["rotation"],
                    expected,
                )
                candidate["priorErrorDegrees"] = prior_error
                candidate["priorConstrainedScore"] = (
                    candidate["score"]
                    - max(0.0, prior_error - 20.0) * 1.5
                )
                if prior_error <= 100.0 and (
                    best_prior_constrained is None
                    or candidate["priorConstrainedScore"]
                    > best_prior_constrained["priorConstrainedScore"]
                ):
                    best_prior_constrained = candidate
        if best is None:
            results.append({"frameId": int(view["frameId"]), "solved": False})
            continue

        error = rotation_error_degrees(best["rotation"], expected)
        front_error = math.degrees(
            math.acos(
                float(
                    np.clip(
                        np.dot(
                            best["rotation"][:, 0],
                            expected[:, 0],
                        ),
                        -1.0,
                        1.0,
                    )
                )
            )
        )
        results.append(
            {
                "frameId": int(view["frameId"]),
                "image": str(path),
                "solved": True,
                "group": best["group"],
                "solver": best["solver"],
                "matches": best["matches"],
                "inliers": best["inliers"],
                "rms": best["rms"],
                "rotationErrorDegrees": error,
                "frontAxisErrorDegrees": front_error,
                "priorConstrainedRotationErrorDegrees": (
                    rotation_error_degrees(
                        best_prior_constrained["rotation"],
                        expected,
                    )
                    if best_prior_constrained is not None
                    else None
                ),
            }
        )

    solved = [row for row in results if row.get("solved")]
    payload = {
        "frames": len(results),
        "solved": len(solved),
        "medianRotationErrorDegrees": (
            float(np.median([row["rotationErrorDegrees"] for row in solved]))
            if solved
            else None
        ),
        "maximumRotationErrorDegrees": (
            max(row["rotationErrorDegrees"] for row in solved)
            if solved
            else None
        ),
        "frontBackFailures": sum(
            row["frontAxisErrorDegrees"] > 120.0 for row in solved
        ),
        "priorConstrainedFrontBackFailures": sum(
            row["priorConstrainedRotationErrorDegrees"] is None
            or row["priorConstrainedRotationErrorDegrees"] > 100.0
            for row in solved
        ),
        "results": results,
        "deviceOverlayVerified": False,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps({key: payload[key] for key in payload if key != "results"}))


if __name__ == "__main__":
    main()
