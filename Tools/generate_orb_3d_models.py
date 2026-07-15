#!/usr/bin/env python3
"""Build ORB descriptor-to-3D maps from a Meshroom camera reconstruction."""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

import cv2
import numpy as np


MAGIC = b"URP3DM1\0"
MODEL_BOUNDS_MIN = np.array([0.15, -4.50, 0.05], dtype=np.float64)
MODEL_BOUNDS_MAX = np.array([0.70, -3.10, 0.50], dtype=np.float64)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sfm", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--step", type=int, default=10)
    parser.add_argument("--max-points", type=int, default=650)
    parser.add_argument("--min-points", type=int, default=45)
    return parser.parse_args()


def camera_projection(view: dict, poses: dict) -> tuple[np.ndarray, np.ndarray]:
    transform = poses[view["poseId"]]["pose"]["transform"]
    camera_to_world = np.asarray(transform["rotation"], dtype=np.float64).reshape(3, 3)
    center = np.asarray(transform["center"], dtype=np.float64)
    world_to_camera = camera_to_world.T
    projection = np.column_stack((world_to_camera, -world_to_camera @ center))
    return projection, center


def triangulate_keyframe(
    views: list[dict],
    start_index: int,
    end_index: int,
    poses: dict,
    camera_matrix: np.ndarray,
    distortion: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    start_image = cv2.imread(views[start_index]["path"], cv2.IMREAD_GRAYSCALE)
    if start_image is None:
        return np.empty((0, 3)), np.empty((0, 32), np.uint8), np.empty((0,))

    orb = cv2.ORB_create(nfeatures=4000, fastThreshold=7)
    keypoints, descriptors = orb.detectAndCompute(start_image, None)
    if descriptors is None or len(keypoints) == 0:
        return np.empty((0, 3)), np.empty((0, 32), np.uint8), np.empty((0,))

    start_pixels = np.float32([keypoint.pt for keypoint in keypoints])
    tracked_pixels = start_pixels[:, None, :].copy()
    previous = start_image
    valid_tracks = np.ones(len(keypoints), dtype=bool)

    for frame_index in range(start_index + 1, end_index + 1):
        current = cv2.imread(views[frame_index]["path"], cv2.IMREAD_GRAYSCALE)
        if current is None:
            valid_tracks[:] = False
            break

        next_pixels, status, _ = cv2.calcOpticalFlowPyrLK(
            previous,
            current,
            tracked_pixels,
            None,
            winSize=(25, 25),
            maxLevel=4,
            criteria=(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 30, 0.01),
        )
        backward_pixels, backward_status, _ = cv2.calcOpticalFlowPyrLK(
            current,
            previous,
            next_pixels,
            None,
            winSize=(25, 25),
            maxLevel=4,
        )
        forward_backward_error = np.linalg.norm(backward_pixels - tracked_pixels, axis=2).ravel()
        valid_tracks &= (
            (status.ravel() > 0)
            & (backward_status.ravel() > 0)
            & (forward_backward_error < 1.0)
        )
        tracked_pixels = next_pixels
        previous = current

    end_pixels = tracked_pixels[:, 0, :]
    start_projection, start_center = camera_projection(views[start_index], poses)
    end_projection, end_center = camera_projection(views[end_index], poses)
    start_undistorted = cv2.undistortPoints(
        start_pixels[:, None, :], camera_matrix, distortion
    ).reshape(-1, 2)
    end_undistorted = cv2.undistortPoints(
        end_pixels[:, None, :], camera_matrix, distortion
    ).reshape(-1, 2)

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
    focal = camera_matrix[0, 0]
    reprojection_error = focal * (
        np.linalg.norm(start_reprojection - start_undistorted, axis=1)
        + np.linalg.norm(end_reprojection - end_undistorted, axis=1)
    )

    start_rays = points - start_center
    end_rays = points - end_center
    cos_angle = np.sum(start_rays * end_rays, axis=1) / (
        np.linalg.norm(start_rays, axis=1) * np.linalg.norm(end_rays, axis=1)
    )
    parallax = np.degrees(np.arccos(np.clip(cos_angle, -1.0, 1.0)))
    inside_model = np.all(points >= MODEL_BOUNDS_MIN, axis=1) & np.all(
        points <= MODEL_BOUNDS_MAX, axis=1
    )
    valid = (
        valid_tracks
        & (start_camera[:, 2] > 0)
        & (end_camera[:, 2] > 0)
        & (reprojection_error < 4.0)
        & (parallax > 2.0)
        & (parallax < 70.0)
        & inside_model
    )
    responses = np.asarray([keypoint.response for keypoint in keypoints], dtype=np.float32)
    return points[valid], descriptors[valid], responses[valid]


def select_spatially_distributed(
    points: np.ndarray,
    descriptors: np.ndarray,
    responses: np.ndarray,
    maximum: int,
) -> tuple[np.ndarray, np.ndarray]:
    if len(points) <= maximum:
        return points, descriptors

    normalized = (points - MODEL_BOUNDS_MIN) / (MODEL_BOUNDS_MAX - MODEL_BOUNDS_MIN)
    cells = np.clip((normalized * np.array([5, 10, 4])).astype(int), 0, [4, 9, 3])
    order = np.argsort(-responses)
    per_cell: dict[tuple[int, int, int], list[int]] = {}
    for index in order:
        cell = tuple(cells[index])
        per_cell.setdefault(cell, []).append(int(index))

    selected: list[int] = []
    while len(selected) < maximum:
        added = False
        for indices in per_cell.values():
            if indices and len(selected) < maximum:
                selected.append(indices.pop(0))
                added = True
        if not added:
            break
    return points[selected], descriptors[selected]


def write_model(path: Path, points: np.ndarray, descriptors: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as output:
        output.write(MAGIC)
        output.write(struct.pack("<I", len(points)))
        for point, descriptor in zip(points.astype(np.float32), descriptors.astype(np.uint8)):
            output.write(struct.pack("<fff", *point))
            output.write(descriptor.tobytes())


def main() -> None:
    args = parse_args()
    data = json.loads(args.sfm.read_text(encoding="utf-8"))
    views = sorted(data["views"], key=lambda view: int(view["frameId"]))
    poses = {pose["poseId"]: pose for pose in data["poses"]}
    intrinsic = data["intrinsics"][0]
    width = float(intrinsic["width"])
    height = float(intrinsic["height"])
    focal = float(intrinsic["focalLength"]) / float(intrinsic["sensorWidth"]) * width
    principal_x = width * 0.5 + float(intrinsic["principalPoint"][0])
    principal_y = height * 0.5 + float(intrinsic["principalPoint"][1])
    camera_matrix = np.array(
        [[focal, 0.0, principal_x], [0.0, focal, principal_y], [0.0, 0.0, 1.0]]
    )
    radial = [float(value) for value in intrinsic["distortionParams"]]
    distortion = np.array([radial[0], radial[1], 0.0, 0.0, radial[2]])

    for old_model in args.output.glob("bottle_view_*.bytes"):
        old_model.unlink()

    written = 0
    for start_index in range(0, len(views) - 4, args.step):
        best = (np.empty((0, 3)), np.empty((0, 32), np.uint8), np.empty((0,)))
        best_end = -1
        for gap in range(4, 11):
            end_index = start_index + gap
            if end_index >= len(views):
                break
            candidate = triangulate_keyframe(
                views, start_index, end_index, poses, camera_matrix, distortion
            )
            if len(candidate[0]) > len(best[0]):
                best = candidate
                best_end = end_index

        points, descriptors, responses = best
        if len(points) < args.min_points:
            continue
        points, descriptors = select_spatially_distributed(
            points, descriptors, responses, args.max_points
        )
        frame_id = int(views[start_index]["frameId"]) + 1
        path = args.output / f"bottle_view_{frame_id:04d}.bytes"
        write_model(path, points, descriptors)
        print(
            f"{path.name}: {len(points)} points, "
            f"tracked through {Path(views[best_end]['path']).name}"
        )
        written += 1

    if written == 0:
        raise SystemExit("No usable ORB 3D model was generated")
    print(f"Generated {written} ORB 3D model views in {args.output}")


if __name__ == "__main__":
    main()
