#!/usr/bin/env python3
"""Triangulate real-photo ORB features through reconstructed bottle views.

Unlike proximity-associating ORB to SIFT landmarks, this builder tracks each
ORB keypoint itself with forward/backward LK optical flow.  The two endpoint
camera poses then triangulate the exact feature that owns the descriptor.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
from collections import defaultdict
from pathlib import Path

import cv2
import numpy as np


MAGIC = b"URP3DM1\0"
GROUPED_MAGIC = b"URP3DM2\0"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sfm", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--grouped-output", type=Path)
    parser.add_argument("--maximum-records-per-group", type=int, default=600)
    parser.add_argument("--max-width", type=int, default=640)
    parser.add_argument("--max-records", type=int, default=8000)
    parser.add_argument("--view-modulo", type=int, default=1)
    parser.add_argument("--view-residue", type=int, default=0)
    parser.add_argument("--minimum-gap", type=int, default=3)
    parser.add_argument("--maximum-gap", type=int, default=9)
    return parser.parse_args()


def load_gray_mask(path: Path, max_width: int) -> tuple[np.ndarray, np.ndarray, float]:
    encoded = np.fromfile(path, dtype=np.uint8)
    image = cv2.imdecode(encoded, cv2.IMREAD_UNCHANGED)
    if image is None:
        raise ValueError(f"Cannot read {path}")
    if image.ndim == 3 and image.shape[2] == 4:
        gray = cv2.cvtColor(image[:, :, :3], cv2.COLOR_BGR2GRAY)
        mask = image[:, :, 3]
    elif image.ndim == 3:
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        mask = np.full(gray.shape, 255, np.uint8)
    else:
        gray = image
        mask = np.full(gray.shape, 255, np.uint8)
    scale = min(1.0, max_width / float(gray.shape[1]))
    if scale < 1.0:
        size = (max_width, max(1, round(gray.shape[0] * scale)))
        gray = cv2.resize(gray, size, interpolation=cv2.INTER_AREA)
        mask = cv2.resize(mask, size, interpolation=cv2.INTER_NEAREST)
    return gray, mask, scale


def camera_projection(view: dict, poses: dict) -> tuple[np.ndarray, np.ndarray]:
    transform = poses[str(view["poseId"])]["pose"]["transform"]
    camera_to_world = np.asarray(transform["rotation"], dtype=np.float64).reshape(3, 3)
    center = np.asarray(transform["center"], dtype=np.float64)
    world_to_camera = camera_to_world.T
    projection = np.column_stack((world_to_camera, -world_to_camera @ center))
    return projection, center


def canonical_transform(
    landmarks: np.ndarray,
    first_camera_center: np.ndarray,
) -> tuple[np.ndarray, dict]:
    centre = np.median(landmarks, axis=0)
    eigenvalues, eigenvectors = np.linalg.eigh(np.cov((landmarks - centre).T))
    up = eigenvectors[:, int(np.argmax(eigenvalues))]
    axial = (landmarks - centre) @ up
    low = float(np.percentile(axial, 2.0))
    high = float(np.percentile(axial, 98.0))
    radial = np.linalg.norm((landmarks - centre) - np.outer(axial, up), axis=1)
    band = max((high - low) * 0.12, 1e-9)
    low_radius = float(np.percentile(radial[axial < low + band], 75.0))
    high_radius = float(np.percentile(radial[axial > high - band], 75.0))
    if high_radius > low_radius:
        up = -up
        axial = -axial
        low, high = -high, -low
        low_radius, high_radius = high_radius, low_radius
    scale = 1.2 / (high - low)
    mouth = centre + up * high
    front = first_camera_center - mouth
    front -= up * float(np.dot(front, up))
    front /= float(np.linalg.norm(front))
    right = np.cross(front, up)
    right /= float(np.linalg.norm(right))
    basis = np.vstack((front, up, right))
    matrix = np.eye(4, dtype=np.float64)
    matrix[:3, :3] = basis * scale
    matrix[:3, 3] = -(basis @ mouth) * scale
    return matrix, {
        "mouthCentreSfm": mouth.tolist(),
        "frontAxisSfm": front.tolist(),
        "upAxisSfm": up.tolist(),
        "rightAxisSfm": right.tolist(),
        "sfmUnitsToModelUnits": scale,
        "mouthEndRadiusSfmP75": high_radius,
        "baseEndRadiusSfmP75": low_radius,
        "matrixRowMajor": matrix.reshape(-1).tolist(),
    }


def apply_transform(points: np.ndarray, matrix: np.ndarray) -> np.ndarray:
    return (
        matrix
        @ np.column_stack((points, np.ones(len(points)))).T
    ).T[:, :3]


def intrinsic_matrix(intrinsic: dict, scale: float) -> tuple[np.ndarray, np.ndarray]:
    width = float(intrinsic["width"])
    focal = float(intrinsic["focalLength"]) / float(intrinsic["sensorWidth"]) * width
    principal_x = width * 0.5 + float(intrinsic["principalPoint"][0])
    principal_y = float(intrinsic["height"]) * 0.5 + float(intrinsic["principalPoint"][1])
    camera = np.asarray(
        [
            [focal * scale, 0.0, principal_x * scale],
            [0.0, focal * scale, principal_y * scale],
            [0.0, 0.0, 1.0],
        ],
        dtype=np.float64,
    )
    radial = [float(value) for value in intrinsic.get("distortionParams", [0, 0, 0])]
    while len(radial) < 3:
        radial.append(0.0)
    distortion = np.asarray(
        [radial[0], radial[1], 0.0, 0.0, radial[2]],
        dtype=np.float64,
    )
    return camera, distortion


def triangulate_start(
    views: list[dict],
    start_index: int,
    end_index: int,
    poses: dict,
    intrinsic: dict,
    max_width: int,
    orb: cv2.ORB,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, dict]:
    start_gray, start_mask, scale = load_gray_mask(
        Path(views[start_index]["path"]),
        max_width,
    )
    keypoints, descriptors = orb.detectAndCompute(start_gray, start_mask)
    if descriptors is None or len(keypoints) == 0:
        return (
            np.empty((0, 3)),
            np.empty((0, 32), np.uint8),
            np.empty((0,)),
            {"detected": len(keypoints), "valid": 0},
        )
    start_pixels = np.float32([keypoint.pt for keypoint in keypoints])
    tracked = start_pixels[:, None, :]
    previous = start_gray
    valid = np.ones(len(keypoints), dtype=bool)
    end_mask = start_mask
    for frame_index in range(start_index + 1, end_index + 1):
        current, end_mask, current_scale = load_gray_mask(
            Path(views[frame_index]["path"]),
            max_width,
        )
        if abs(current_scale - scale) > 1e-6:
            valid[:] = False
            break
        forward, forward_status, _ = cv2.calcOpticalFlowPyrLK(
            previous,
            current,
            tracked,
            None,
            winSize=(25, 25),
            maxLevel=4,
            criteria=(
                cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT,
                30,
                0.01,
            ),
        )
        backward, backward_status, _ = cv2.calcOpticalFlowPyrLK(
            current,
            previous,
            forward,
            None,
            winSize=(25, 25),
            maxLevel=4,
        )
        fb_error = np.linalg.norm(backward - tracked, axis=2).ravel()
        valid &= (
            (forward_status.ravel() > 0)
            & (backward_status.ravel() > 0)
            & (fb_error < 0.75)
        )
        tracked = forward
        previous = current

    end_pixels = tracked[:, 0, :]
    inside = (
        (end_pixels[:, 0] >= 0)
        & (end_pixels[:, 0] < end_mask.shape[1])
        & (end_pixels[:, 1] >= 0)
        & (end_pixels[:, 1] < end_mask.shape[0])
    )
    valid &= inside
    safe_x = np.clip(np.round(end_pixels[:, 0]).astype(int), 0, end_mask.shape[1] - 1)
    safe_y = np.clip(np.round(end_pixels[:, 1]).astype(int), 0, end_mask.shape[0] - 1)
    valid &= end_mask[safe_y, safe_x] > 0

    camera, distortion = intrinsic_matrix(intrinsic, scale)
    start_undistorted = cv2.undistortPoints(
        start_pixels[:, None, :],
        camera,
        distortion,
    ).reshape(-1, 2)
    end_undistorted = cv2.undistortPoints(
        end_pixels[:, None, :],
        camera,
        distortion,
    ).reshape(-1, 2)
    start_projection, start_center = camera_projection(views[start_index], poses)
    end_projection, end_center = camera_projection(views[end_index], poses)
    homogeneous = cv2.triangulatePoints(
        start_projection,
        end_projection,
        start_undistorted.T,
        end_undistorted.T,
    )
    points = (homogeneous[:3] / homogeneous[3]).T
    homogeneous_points = np.column_stack((points, np.ones(len(points))))
    start_camera = (start_projection @ homogeneous_points.T).T
    end_camera = (end_projection @ homogeneous_points.T).T
    start_reprojection = start_camera[:, :2] / start_camera[:, 2, None]
    end_reprojection = end_camera[:, :2] / end_camera[:, 2, None]
    focal = camera[0, 0]
    reprojection_error = focal * (
        np.linalg.norm(start_reprojection - start_undistorted, axis=1)
        + np.linalg.norm(end_reprojection - end_undistorted, axis=1)
    )
    start_rays = points - start_center
    end_rays = points - end_center
    cosine = np.sum(start_rays * end_rays, axis=1) / (
        np.linalg.norm(start_rays, axis=1) * np.linalg.norm(end_rays, axis=1)
    )
    parallax = np.degrees(np.arccos(np.clip(cosine, -1.0, 1.0)))
    valid &= (
        np.isfinite(points).all(axis=1)
        & (start_camera[:, 2] > 0)
        & (end_camera[:, 2] > 0)
        & (reprojection_error < 3.0)
        & (parallax > 1.0)
        & (parallax < 50.0)
    )
    responses = np.asarray(
        [keypoint.response for keypoint in keypoints],
        dtype=np.float32,
    )
    return points[valid], descriptors[valid], responses[valid], {
        "detected": len(keypoints),
        "valid": int(np.count_nonzero(valid)),
        "medianReprojection": (
            float(np.median(reprojection_error[valid]))
            if np.any(valid)
            else None
        ),
        "medianParallaxDegrees": (
            float(np.median(parallax[valid]))
            if np.any(valid)
            else None
        ),
    }


def select_records(
    points: np.ndarray,
    descriptors: np.ndarray,
    responses: np.ndarray,
    maximum: int,
) -> np.ndarray:
    # Reject triangulated outliers with the physical canonical bottle envelope.
    radial = np.hypot(points[:, 0], points[:, 2])
    valid = (
        (points[:, 1] >= -1.28)
        & (points[:, 1] <= 0.06)
        & (radial <= 0.34)
    )
    indices = np.flatnonzero(valid)
    if len(indices) <= maximum:
        return indices
    selected_points = points[indices]
    minimum = np.asarray([-0.34, -1.28, -0.34])
    span = np.asarray([0.68, 1.34, 0.68])
    cells = np.floor(
        np.clip((selected_points - minimum) / span, 0.0, 0.999999)
        * np.asarray([10, 20, 10])
    ).astype(np.int32)
    order = indices[np.argsort(-responses[indices])]
    cell_by_index = {
        int(index): tuple(int(value) for value in cell)
        for index, cell in zip(indices, cells)
    }
    buckets: dict[tuple[int, int, int], list[int]] = defaultdict(list)
    for index in order:
        buckets[cell_by_index[int(index)]].append(int(index))
    output: list[int] = []
    while len(output) < maximum:
        added = False
        for bucket in buckets.values():
            if bucket and len(output) < maximum:
                output.append(bucket.pop(0))
                added = True
        if not added:
            break
    return np.asarray(output, dtype=np.int64)


def write_database(path: Path, points: np.ndarray, descriptors: np.ndarray) -> bytes:
    payload = bytearray(MAGIC)
    payload.extend(struct.pack("<I", len(points)))
    for point, descriptor in zip(points.astype(np.float32), descriptors.astype(np.uint8)):
        payload.extend(struct.pack("<fff", *point))
        payload.extend(descriptor.tobytes())
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return bytes(payload)


def write_grouped_database(
    path: Path,
    points: np.ndarray,
    descriptors: np.ndarray,
    group_ids: np.ndarray,
) -> bytes:
    groups = sorted(int(value) for value in np.unique(group_ids))
    payload = bytearray(GROUPED_MAGIC)
    payload.extend(struct.pack("<II", len(points), len(groups)))
    for group_id in groups:
        indices = np.flatnonzero(group_ids == group_id)
        payload.extend(struct.pack("<II", group_id, len(indices)))
        for index in indices:
            payload.extend(struct.pack("<fff", *points[index].astype(np.float32)))
            payload.extend(descriptors[index].astype(np.uint8).tobytes())
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return bytes(payload)


def main() -> None:
    args = parse_args()
    data = json.loads(args.sfm.read_text(encoding="utf-8"))
    poses = {str(pose["poseId"]): pose for pose in data["poses"]}
    views = sorted(data["views"], key=lambda view: int(view["frameId"]))
    pose_indices = [
        index
        for index, view in enumerate(views)
        if str(view["poseId"]) in poses
    ]
    intrinsics = {
        str(intrinsic["intrinsicId"]): intrinsic
        for intrinsic in data["intrinsics"]
    }
    structure_points = np.asarray(
        [[float(value) for value in landmark["X"]] for landmark in data["structure"]],
        dtype=np.float64,
    )
    first_pose_view = views[pose_indices[0]]
    first_center = camera_projection(first_pose_view, poses)[1]
    transform, transform_report = canonical_transform(structure_points, first_center)
    orb = cv2.ORB_create(
        5000,
        1.15,
        10,
        31,
        0,
        2,
        cv2.ORB_HARRIS_SCORE,
        31,
        7,
    )

    all_points: list[np.ndarray] = []
    all_descriptors: list[np.ndarray] = []
    all_responses: list[np.ndarray] = []
    all_group_ids: list[np.ndarray] = []
    view_reports: list[dict] = []
    for start_index in pose_indices:
        frame_id = int(views[start_index]["frameId"])
        if frame_id % args.view_modulo != args.view_residue:
            continue
        candidates = [
            index
            for index in pose_indices
            if start_index + args.minimum_gap <= index <= start_index + args.maximum_gap
        ]
        best = None
        best_end = None
        best_info = None
        for end_index in candidates:
            if views[end_index]["intrinsicId"] != views[start_index]["intrinsicId"]:
                continue
            result = triangulate_start(
                views,
                start_index,
                end_index,
                poses,
                intrinsics[str(views[start_index]["intrinsicId"])],
                args.max_width,
                orb,
            )
            if best is None or len(result[0]) > len(best[0]):
                best = result
                best_end = end_index
                best_info = result[3]
        if best is None or len(best[0]) < 10:
            continue
        canonical = apply_transform(best[0], transform)
        all_points.append(canonical)
        all_descriptors.append(best[1])
        all_responses.append(best[2])
        all_group_ids.append(
            np.full(len(canonical), len(view_reports), dtype=np.int32)
        )
        view_reports.append(
            {
                "startFrameId": frame_id,
                "startPath": views[start_index]["path"],
                "endFrameId": int(views[best_end]["frameId"]),
                "endPath": views[best_end]["path"],
                **best_info,
            }
        )
    if not all_points:
        raise ValueError("No optical-flow ORB records were triangulated")
    points = np.concatenate(all_points)
    descriptors = np.concatenate(all_descriptors)
    responses = np.concatenate(all_responses)
    group_ids = np.concatenate(all_group_ids)
    selected = select_records(points, descriptors, responses, args.max_records)
    points = points[selected]
    descriptors = descriptors[selected]
    group_ids = group_ids[selected]
    if len(points) < 50:
        raise ValueError(f"Only {len(points)} records survived the bottle envelope")
    payload = write_database(args.output, points, descriptors)
    grouped_payload = None
    grouped_points = np.empty((0, 3), dtype=np.float64)
    grouped_descriptors = np.empty((0, 32), dtype=np.uint8)
    grouped_ids = np.empty((0,), dtype=np.int32)
    if args.grouped_output is not None:
        grouped_selections: list[np.ndarray] = []
        unselected_points = np.concatenate(all_points)
        unselected_descriptors = np.concatenate(all_descriptors)
        unselected_responses = np.concatenate(all_responses)
        unselected_groups = np.concatenate(all_group_ids)
        for group_id in sorted(int(value) for value in np.unique(unselected_groups)):
            group_indices = np.flatnonzero(unselected_groups == group_id)
            local_selection = select_records(
                unselected_points[group_indices],
                unselected_descriptors[group_indices],
                unselected_responses[group_indices],
                args.maximum_records_per_group,
            )
            if len(local_selection) >= 8:
                grouped_selections.append(group_indices[local_selection])
        if not grouped_selections:
            raise ValueError("No grouped ORB keyframe contains eight valid records")
        grouped_selection = np.concatenate(grouped_selections)
        grouped_points = unselected_points[grouped_selection]
        grouped_descriptors = unselected_descriptors[grouped_selection]
        grouped_ids = unselected_groups[grouped_selection]
        grouped_payload = write_grouped_database(
            args.grouped_output,
            grouped_points,
            grouped_descriptors,
            grouped_ids,
        )
    report = {
        "version": "real-no-cap-orb-optical-flow-v27",
        "sourceSfm": str(args.sfm),
        "inputViews": len(views),
        "reconstructedViews": len(pose_indices),
        "viewSelection": {
            "modulo": args.view_modulo,
            "residue": args.view_residue,
        },
        "recordsBeforeEnvelope": int(len(np.concatenate(all_points))),
        "records": len(points),
        "databaseSha256": hashlib.sha256(payload).hexdigest().upper(),
        "groupedDatabase": (
            {
                "path": str(args.grouped_output),
                "records": len(grouped_points),
                "groups": int(len(np.unique(grouped_ids))),
                "maximumRecordsPerGroup": args.maximum_records_per_group,
                "sha256": hashlib.sha256(grouped_payload).hexdigest().upper(),
            }
            if grouped_payload is not None
            else None
        ),
        "canonicalBoundsMin": points.min(axis=0).tolist(),
        "canonicalBoundsMax": points.max(axis=0).tolist(),
        "canonicalTransform": transform_report,
        "repairCExcluded": True,
        "deviceOverlayVerified": False,
        "views": view_reports,
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(
        json.dumps(
            {
                "records": report["records"],
                "reconstructedViews": report["reconstructedViews"],
                "databaseSha256": report["databaseSha256"],
            },
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
