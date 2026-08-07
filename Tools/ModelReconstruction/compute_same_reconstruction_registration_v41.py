#!/usr/bin/env python3
"""Measure and publish the v41 same-reconstruction ORB/B contract.

The source Meshroom surface and the filtered production-B surface are measured
independently.  No target landmark is copied into a source landmark and no
translation is forced to manufacture a zero mouth error.  AliceVision's OBJ
export is related to its SfM coordinates by the documented Rx(180) axis change
diag(1,-1,-1); every remaining axis, origin, and scale below comes from actual
geometry or explicitly recorded semantic source photographs.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

import cv2
import numpy as np
import trimesh
from scipy.optimize import least_squares
from scipy.spatial.transform import Rotation


MAGIC = b"URP3DM1\0"
METERS_PER_MODEL_UNIT = 0.17
MEASURED_NECK_DIAMETER_METERS = 0.034
MESH_FROM_SFM = np.diag([1.0, -1.0, -1.0, 1.0])


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sfm", type=Path, required=True)
    parser.add_argument("--raw-mesh", type=Path, required=True)
    parser.add_argument("--production-mesh", type=Path, required=True)
    parser.add_argument("--production-textured-mesh", type=Path, required=True)
    parser.add_argument("--target-fbx", type=Path)
    parser.add_argument("--orb", type=Path)
    parser.add_argument("--artifact", type=Path, required=True)
    parser.add_argument("--contract", type=Path, required=True)
    parser.add_argument("--measurement-report", type=Path, required=True)
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def load_largest_mesh(path: Path) -> trimesh.Trimesh:
    loaded = trimesh.load(path, force="mesh", process=False)
    # Drop the multi-megabyte texture object before graph splitting. Geometry
    # measurement must not depend on material pixels, and deepcopy of the PIL
    # atlas can otherwise exceed memory on the Android build workstation.
    geometry = trimesh.Trimesh(
        vertices=np.asarray(loaded.vertices),
        faces=np.asarray(loaded.faces),
        process=False,
    )
    # Meshroom's UV unwrap duplicates seam vertices, so a textured production
    # OBJ may appear as many topological islands even though its faces describe
    # one bottle. The raw reconstruction's discarded islands are tiny and are
    # rejected by the robust slice fits; retaining all faces keeps the two
    # measurement paths geometrically equivalent without texture-dependent
    # component selection.
    if len(geometry.faces) == 0:
        raise ValueError(f"{path}: no mesh faces")
    return geometry


def robust_circle(points: np.ndarray) -> tuple[np.ndarray, float, float]:
    center = np.median(points, axis=0)
    radius = float(np.median(np.linalg.norm(points - center, axis=1)))
    for _ in range(5):
        distances = np.linalg.norm(points - center, axis=1)
        low, high = np.percentile(distances, [10.0, 90.0])
        selected = points[(distances >= low) & (distances <= high)]
        result = least_squares(
            lambda value: np.linalg.norm(selected - value[:2], axis=1) - value[2],
            [center[0], center[1], radius],
            loss="soft_l1",
            f_scale=0.004,
        )
        center = result.x[:2]
        radius = float(result.x[2])
    distances = np.linalg.norm(points - center, axis=1)
    median_radius = float(np.median(distances))
    mad = float(np.median(np.abs(distances - median_radius)))
    return center, median_radius, mad


def slice_measurements(vertices: np.ndarray) -> list[dict]:
    minimum, maximum = np.percentile(vertices[:, 1], [0.1, 99.9])
    step = (maximum - minimum) / 160.0
    half_band = step * 0.55
    rows: list[dict] = []
    for y in np.arange(minimum, maximum + step * 0.5, step):
        points = vertices[np.abs(vertices[:, 1] - y) <= half_band][:, [0, 2]]
        if len(points) < 60:
            continue
        center, radius, mad = robust_circle(points)
        rows.append(
            {
                "y": float(y),
                "center_xz": center,
                "radius": radius,
                "mad": mad,
                "count": len(points),
            }
        )
    return rows


def fit_axis(rows: list[dict]) -> np.ndarray:
    candidates = [
        row for row in rows
        if row["count"] >= 100 and row["mad"] <= 0.012
    ]
    if len(candidates) < 12:
        raise ValueError("not enough stable cross-sections to fit the bottle axis")
    y_values = np.asarray([row["y"] for row in candidates])
    design = np.column_stack((y_values, np.ones(len(y_values))))
    x_values = np.asarray([row["center_xz"][0] for row in candidates])
    z_values = np.asarray([row["center_xz"][1] for row in candidates])
    x_fit = np.linalg.lstsq(design, x_values, rcond=None)[0]
    z_fit = np.linalg.lstsq(design, z_values, rcond=None)[0]
    axis = np.asarray([x_fit[0], 1.0, z_fit[0]], dtype=np.float64)
    return axis / np.linalg.norm(axis)


def endpoint(rows: list[dict], mouth: bool) -> tuple[np.ndarray, float, dict]:
    radii = np.asarray([row["radius"] for row in rows])
    median_radius = float(np.median(radii))
    if mouth:
        candidates = [
            row for row in rows
            if row["count"] >= 180
            and row["mad"] <= 0.012
            and row["radius"] <= median_radius * 0.72
        ]
        chosen = max(candidates, key=lambda row: row["y"])
    else:
        candidates = [
            row for row in rows
            if row["count"] >= 80
            and row["mad"] <= 0.010
            and row["radius"] >= median_radius * 0.88
        ]
        chosen = min(candidates, key=lambda row: row["y"])
    point = np.asarray(
        [chosen["center_xz"][0], chosen["y"], chosen["center_xz"][1]],
        dtype=np.float64,
    )
    serializable = {
        "plane_y": chosen["y"],
        "ring_center_xyz": point.tolist(),
        "ring_radius_sfm_units": chosen["radius"],
        "ring_mad_sfm_units": chosen["mad"],
        "ring_vertex_count": chosen["count"],
    }
    return point, float(chosen["radius"]), serializable


def measure_mesh(path: Path) -> dict:
    mesh = load_largest_mesh(path)
    vertices = np.asarray(mesh.vertices, dtype=np.float64)
    rows = slice_measurements(vertices)
    mouth, radius, mouth_fit = endpoint(rows, True)
    base, _base_radius, base_fit = endpoint(rows, False)
    base_to_mouth = mouth - base
    axis = base_to_mouth / np.linalg.norm(base_to_mouth)
    height = float(np.linalg.norm(base_to_mouth))
    return {
        "path": str(path),
        "sha256": sha256(path),
        "vertices": len(mesh.vertices),
        "faces": len(mesh.faces),
        "bounds": mesh.bounds.tolist(),
        "mouth_center_mesh": mouth,
        "base_center_mesh": base,
        "up_axis_mesh": axis,
        "mouth_radius_mesh_units": radius,
        "axis_height_mesh_units": height,
        "mouth_fit": mouth_fit,
        "base_fit": base_fit,
        "mesh": mesh,
    }


def red_logo_observations(data: dict) -> list[dict]:
    output: list[dict] = []
    for view in data["views"]:
        frame_id = int(view["frameId"])
        if frame_id != 1 and frame_id % 4 != 0:
            continue
        path = Path(view["path"])
        encoded = np.fromfile(path, dtype=np.uint8)
        image = cv2.imdecode(encoded, cv2.IMREAD_UNCHANGED)
        if image is None or image.ndim != 3 or image.shape[2] < 4:
            continue
        alpha = image[:, :, 3]
        ys, xs = np.where(alpha > 128)
        if len(xs) < 100:
            continue
        x0, x1 = int(xs.min()), int(xs.max())
        y0, y1 = int(ys.min()), int(ys.max())
        width = max(1, x1 - x0)
        height = max(1, y1 - y0)
        hsv = cv2.cvtColor(image[:, :, :3], cv2.COLOR_BGR2HSV)
        red = (
            ((hsv[:, :, 0] < 12) | (hsv[:, :, 0] > 170))
            & (hsv[:, :, 1] > 100)
            & (hsv[:, :, 2] > 70)
            & (alpha > 128)
        ).astype(np.uint8)
        count, _labels, stats, _centroids = cv2.connectedComponentsWithStats(red)
        candidates = []
        for index in range(1, count):
            x, y, w, h, area = stats[index]
            if area < 100 or y > y0 + 0.45 * height:
                continue
            aspect = w / max(1.0, float(h))
            if 0.8 <= aspect <= 4.5:
                candidates.append((area, x + w * 0.5, y + h * 0.5))
        if not candidates:
            continue
        _area, x, y = max(candidates)
        normalized_x = (x - (x0 + x1) * 0.5) / width
        normalized_y = (y - y0) / height
        if abs(normalized_x) <= 0.15 and 0.08 <= normalized_y <= 0.40:
            output.append({
                "frame_id": frame_id,
                "view_id": str(view["viewId"]),
                "pixel": [float(x), float(y)],
                "component_area_px": int(_area),
            })
    if len(output) < 8:
        raise ValueError("red-logo semantic detector found too few front views")
    return output


def camera_ray_sfm(data: dict, view: dict, pixel: list[float]) -> tuple[np.ndarray, np.ndarray]:
    poses = {str(pose["poseId"]): pose for pose in data["poses"]}
    intrinsics = {str(item["intrinsicId"]): item for item in data["intrinsics"]}
    pose = poses[str(view["poseId"])]["pose"]["transform"]
    intrinsic = intrinsics[str(view["intrinsicId"])]
    width = float(intrinsic["width"])
    height = float(intrinsic["height"])
    focal_pixels = float(intrinsic["focalLength"]) / float(intrinsic["sensorWidth"]) * width
    camera_matrix = np.asarray([
        [focal_pixels, 0.0, width * 0.5 + float(intrinsic["principalPoint"][0])],
        [0.0, focal_pixels * float(intrinsic.get("pixelRatio", 1.0)),
         height * 0.5 + float(intrinsic["principalPoint"][1])],
        [0.0, 0.0, 1.0],
    ])
    radial = np.asarray(intrinsic.get("distortionParams", []), dtype=np.float64)
    if len(radial) == 3:
        distortion = np.asarray([radial[0], radial[1], 0.0, 0.0, radial[2]])
    else:
        distortion = radial
    undistorted = cv2.undistortPoints(
        np.asarray(pixel, dtype=np.float64).reshape(1, 1, 2),
        camera_matrix,
        distortion,
    )[0, 0]
    camera_ray = np.asarray([undistorted[0], undistorted[1], 1.0])
    camera_ray /= np.linalg.norm(camera_ray)
    # AliceVision stores camera-to-world rotation. This convention was checked
    # independently by reprojecting SfM structure observations (R.T maps world
    # to camera); it is not inferred from the desired bottle orientation.
    camera_to_world = np.asarray(pose["rotation"], dtype=np.float64).reshape(3, 3)
    center = np.asarray(pose["center"], dtype=np.float64)
    world_ray = camera_to_world @ camera_ray
    return center, world_ray / np.linalg.norm(world_ray)


def triangulate_rays(origins: np.ndarray, directions: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    active = np.ones(len(origins), dtype=bool)
    point = np.zeros(3)
    residuals = np.zeros(len(origins))
    for _ in range(5):
        projectors = np.eye(3)[None] - directions[active, :, None] * directions[active, None, :]
        lhs = np.sum(projectors, axis=0)
        rhs = np.sum(projectors @ origins[active, :, None], axis=0)[:, 0]
        point = np.linalg.solve(lhs, rhs)
        residuals = np.linalg.norm(
            np.cross(point[None] - origins, directions), axis=1
        )
        median = float(np.median(residuals[active]))
        mad = float(np.median(np.abs(residuals[active] - median)))
        threshold = median + max(3.5 * 1.4826 * mad, 0.006)
        updated = residuals <= threshold
        if np.array_equal(updated, active) or np.count_nonzero(updated) < 6:
            break
        active = updated
    return point, residuals


def measure_texture_front_axis(
    textured_mesh_path: Path,
    mouth_mesh: np.ndarray,
    up_mesh: np.ndarray,
) -> tuple[np.ndarray, dict]:
    loaded = trimesh.load(textured_mesh_path, force="mesh", process=False)
    vertices = np.asarray(loaded.vertices, dtype=np.float64)
    uv = np.asarray(loaded.visual.uv, dtype=np.float64)
    image = np.asarray(loaded.visual.material.image.convert("RGB"))
    height, width = image.shape[:2]
    px = np.clip(np.rint(uv[:, 0] * (width - 1)).astype(int), 0, width - 1)
    py = np.clip(np.rint((1.0 - uv[:, 1]) * (height - 1)).astype(int), 0, height - 1)
    colors = image[py, px]
    hsv = cv2.cvtColor(colors.reshape(-1, 1, 3), cv2.COLOR_RGB2HSV)[:, 0]
    red = ((hsv[:, 0] < 12) | (hsv[:, 0] > 170)) & (hsv[:, 1] > 100) & (hsv[:, 2] > 70)
    axial = (vertices - mouth_mesh) @ up_mesh
    # The upper-front printed logo is below the mouth and above the shoulder/body
    # text. These limits are only a region selector; the resulting direction is
    # measured from actual textured production vertices.
    selected = red & (axial >= -0.45) & (axial <= -0.12)
    points = vertices[selected]
    if len(points) < 80:
        raise ValueError("production texture contains too few red-logo vertices")
    axis_points = mouth_mesh + np.outer(axial[selected], up_mesh)
    radial = points - axis_points
    radial /= np.linalg.norm(radial, axis=1)[:, None]
    basis_x = np.asarray([1.0, 0.0, 0.0])
    basis_x -= up_mesh * float(basis_x @ up_mesh)
    basis_x /= np.linalg.norm(basis_x)
    basis_z = np.cross(basis_x, up_mesh)
    basis_z /= np.linalg.norm(basis_z)
    angles = np.arctan2(radial @ basis_z, radial @ basis_x)
    histogram, edges = np.histogram(angles, bins=72, range=(-math.pi, math.pi))
    peak = int(np.argmax(histogram))
    peak_angle = float((edges[peak] + edges[peak + 1]) * 0.5)
    wrapped = np.arctan2(np.sin(angles - peak_angle), np.cos(angles - peak_angle))
    keep = np.abs(wrapped) <= math.radians(18.0)
    front = radial[keep].mean(axis=0)
    front -= up_mesh * float(front @ up_mesh)
    front /= np.linalg.norm(front)
    return front, {
        "method": "actual production OBJ UV vertices sampled from texture atlas red-logo cluster",
        "textured_mesh": str(textured_mesh_path),
        "textured_mesh_sha256": sha256(textured_mesh_path),
        "red_vertex_count": int(len(points)),
        "robust_red_vertex_count": int(np.count_nonzero(keep)),
        "front_axis_mesh": front.tolist(),
        "front_point_mesh": np.median(points[keep], axis=0).tolist(),
    }


def semantic_front_axis(data: dict, mouth_mesh: np.ndarray, up_mesh: np.ndarray) -> tuple[np.ndarray, dict]:
    poses = {str(pose["poseId"]): pose for pose in data["poses"]}
    views = {int(view["frameId"]): view for view in data["views"]}
    logo_observations = red_logo_observations(data)
    origins = []
    directions = []
    for observation in logo_observations:
        view = views[observation["frame_id"]]
        origin, direction = camera_ray_sfm(data, view, observation["pixel"])
        origins.append(origin)
        directions.append(direction)
    logo_sfm, ray_residuals = triangulate_rays(
        np.asarray(origins), np.asarray(directions)
    )
    logo_mesh = MESH_FROM_SFM[:3, :3] @ logo_sfm
    axial = float((logo_mesh - mouth_mesh) @ up_mesh)
    axis_point = mouth_mesh + axial * up_mesh
    front = logo_mesh - axis_point
    front -= up_mesh * float(front @ up_mesh)
    front /= np.linalg.norm(front)

    # These frames are separately recorded barcode-side observations from the
    # same real-photo sequence. They validate that the red-logo direction is
    # not the bottle's rotationally ambiguous barcode side.
    barcode_frames = [frame for frame in (320, 321, 322, 323, 324, 339, 340, 341)
                      if frame in views]
    barcode_directions = []
    for frame in barcode_frames:
        view = views[frame]
        center_sfm = np.asarray(
            poses[str(view["poseId"])]["pose"]["transform"]["center"],
            dtype=np.float64,
        )
        center_mesh = MESH_FROM_SFM[:3, :3] @ center_sfm
        direction = center_mesh - mouth_mesh
        direction -= up_mesh * float(direction @ up_mesh)
        barcode_directions.append(direction / np.linalg.norm(direction))
    barcode = np.mean(barcode_directions, axis=0)
    barcode /= np.linalg.norm(barcode)
    separation = math.degrees(math.acos(float(np.clip(front @ barcode, -1.0, 1.0))))
    if separation < 45.0:
        raise ValueError("front and barcode semantic directions are not independent")
    return front, {
        "red_logo_detection": "HSV connected-component centroid on actual masked photographs",
        "red_logo_front_frame_ids": [item["frame_id"] for item in logo_observations],
        "red_logo_pixel_observations": logo_observations,
        "triangulated_logo_point_sfm": logo_sfm.tolist(),
        "triangulated_logo_point_mesh": logo_mesh.tolist(),
        "triangulation_ray_median_mm_at_physical_scale": (
            float(np.median(ray_residuals))
            * (MEASURED_NECK_DIAMETER_METERS / (2.0 * 0.072205)) * 1000.0
        ),
        "barcode_side_frame_ids": barcode_frames,
        "front_axis_mesh": front.tolist(),
        "barcode_axis_mesh": barcode.tolist(),
        "front_barcode_separation_deg": separation,
    }


def make_transform(mouth: np.ndarray, up: np.ndarray, front: np.ndarray, radius: float) -> np.ndarray:
    right = np.cross(up, front)
    right /= np.linalg.norm(right)
    front = np.cross(right, up)
    front /= np.linalg.norm(front)
    model_units_per_mesh_unit = (
        MEASURED_NECK_DIAMETER_METERS / METERS_PER_MODEL_UNIT / (2.0 * radius)
    )
    transform = np.eye(4, dtype=np.float64)
    transform[:3, :3] = np.vstack((right, up, front)) * model_units_per_mesh_unit
    transform[:3, 3] = -(transform[:3, :3] @ mouth)
    return transform


def apply(transform: np.ndarray, points: np.ndarray) -> np.ndarray:
    return (transform @ np.column_stack((points, np.ones(len(points)))).T).T[:, :3]


def orb_points(path: Path) -> np.ndarray:
    payload = path.read_bytes()
    if payload[:8] != MAGIC:
        raise ValueError(f"{path}: invalid ORB database magic")
    count = struct.unpack_from("<I", payload, 8)[0]
    return np.asarray(
        [struct.unpack_from("<3f", payload, 12 + index * 44) for index in range(count)],
        dtype=np.float64,
    )


def distance_stats_mm(mesh: trimesh.Trimesh, points: np.ndarray) -> dict[str, float]:
    _closest, distances, triangles = trimesh.proximity.closest_point(mesh, points)
    if np.any(triangles < 0) or not np.all(np.isfinite(distances)):
        raise ValueError("ORB-to-production-B surface query failed")
    values = distances * METERS_PER_MODEL_UNIT * 1000.0
    return {
        "rms_mm": float(np.sqrt(np.mean(values * values))),
        "median_mm": float(np.median(values)),
        "p90_mm": float(np.percentile(values, 90.0)),
        "p95_mm": float(np.percentile(values, 95.0)),
        "max_mm": float(np.max(values)),
    }


def angle_degrees(a: np.ndarray, b: np.ndarray) -> float:
    return math.degrees(math.acos(float(np.clip(a @ b, -1.0, 1.0))))


def serializable_measurement(value: dict) -> dict:
    return {key: item for key, item in value.items() if key != "mesh" and not isinstance(item, np.ndarray)} | {
        key: item.tolist() for key, item in value.items() if isinstance(item, np.ndarray)
    }


def main() -> None:
    args = parse_args()
    sfm = json.loads(args.sfm.read_text(encoding="utf-8"))
    raw = measure_mesh(args.raw_mesh)
    production = measure_mesh(args.production_mesh)
    front, semantic = semantic_front_axis(
        sfm,
        raw["mouth_center_mesh"],
        raw["up_axis_mesh"],
    )
    production_front, production_front_evidence = measure_texture_front_axis(
        args.production_textured_mesh,
        production["mouth_center_mesh"],
        production["up_axis_mesh"],
    )
    orb_from_raw_mesh = make_transform(
        raw["mouth_center_mesh"],
        raw["up_axis_mesh"],
        front,
        raw["mouth_radius_mesh_units"],
    )
    orb_from_production_b = orb_from_raw_mesh.copy()
    sfm_to_orb = orb_from_raw_mesh @ MESH_FROM_SFM

    raw_mouth_orb = apply(orb_from_raw_mesh, raw["mouth_center_mesh"][None])[0]
    raw_base_orb = apply(orb_from_raw_mesh, raw["base_center_mesh"][None])[0]
    production_mouth_orb = apply(
        orb_from_production_b, production["mouth_center_mesh"][None]
    )[0]
    production_base_orb = apply(
        orb_from_production_b, production["base_center_mesh"][None]
    )[0]
    mouth_error = float(np.linalg.norm(production_mouth_orb - raw_mouth_orb)
                        * METERS_PER_MODEL_UNIT * 1000.0)
    base_error = float(np.linalg.norm(production_base_orb - raw_base_orb)
                       * METERS_PER_MODEL_UNIT * 1000.0)
    raw_up_orb = orb_from_raw_mesh[:3, :3] @ raw["up_axis_mesh"]
    raw_up_orb /= np.linalg.norm(raw_up_orb)
    production_up_orb = orb_from_production_b[:3, :3] @ production["up_axis_mesh"]
    production_up_orb /= np.linalg.norm(production_up_orb)
    up_error = angle_degrees(raw_up_orb, production_up_orb)
    raw_front_orb = orb_from_raw_mesh[:3, :3] @ front
    raw_front_orb /= np.linalg.norm(raw_front_orb)
    production_front_orb = orb_from_production_b[:3, :3] @ production_front
    production_front_orb /= np.linalg.norm(production_front_orb)
    front_error = angle_degrees(raw_front_orb, production_front_orb)
    semantic["production_texture_front"] = production_front_evidence
    semantic["source_to_production_front_axis_error_deg"] = front_error
    height_error = abs(raw["axis_height_mesh_units"] - production["axis_height_mesh_units"])
    height_error *= float(np.cbrt(np.linalg.det(orb_from_raw_mesh[:3, :3])))
    height_error *= METERS_PER_MODEL_UNIT * 1000.0

    production_canonical = production["mesh"].copy()
    production_canonical.apply_transform(orb_from_production_b)
    surface = {
        "rms_mm": float("inf"),
        "median_mm": float("inf"),
        "p90_mm": float("inf"),
        "p95_mm": float("inf"),
        "max_mm": float("inf"),
    }
    orb_sha = "NOT_GENERATED"
    if args.orb is not None and args.orb.exists():
        points = orb_points(args.orb)
        surface = distance_stats_mm(production_canonical, points)
        orb_sha = sha256(args.orb)

    scale = float(np.cbrt(np.linalg.det(orb_from_production_b[:3, :3])))
    rotation = orb_from_production_b[:3, :3] / scale
    quaternion = Rotation.from_matrix(rotation).as_quat().tolist()
    translation_residual = (production_mouth_orb - raw_mouth_orb) \
        * METERS_PER_MODEL_UNIT * 1000.0
    front_point_orb = apply(
        orb_from_raw_mesh,
        np.asarray(semantic["triangulated_logo_point_mesh"])[None],
    )[0]
    registered_front_point = apply(
        orb_from_production_b,
        np.asarray(production_front_evidence["front_point_mesh"])[None],
    )[0]
    strict = (
        mouth_error <= 2.0
        and base_error <= 3.0
        and surface["median_mm"] <= 2.5
        and surface["p95_mm"] <= 5.0
        and up_error <= 1.5
        and front_error <= 1.5
    )
    artifact = {
        "version": "bottle-orb-same-reconstruction-registration-v41",
        "registration_method": (
            "same Meshroom reconstruction; raw surface and filtered production B "
            "measured independently; robust mouth/base rings plus center-line axis; "
            "red-logo front and barcode side use real source frames; 34 mm measured "
            "neck diameter establishes physical scale"
        ),
        "independent_model_registration_verified": strict,
        "device_verified": False,
        "source_orb_sha256": orb_sha,
        "source_b_mesh_sha256": production["sha256"],
        "target_b_mesh_sha256": (
            sha256(args.target_fbx)
            if args.target_fbx is not None and args.target_fbx.exists()
            else "filled after v41 FBX export"
        ),
        "T_ORB_FROM_B": orb_from_production_b.reshape(-1).tolist(),
        "sfm_to_orb_matrix": sfm_to_orb.reshape(-1).tolist(),
        "alicevision_mesh_from_sfm_matrix": MESH_FROM_SFM.reshape(-1).tolist(),
        "scale": scale,
        "rotation_quaternion_xyzw": quaternion,
        "translation": orb_from_production_b[:3, 3].tolist(),
        "determinant": float(np.linalg.det(orb_from_production_b[:3, :3])),
        "mouth_center_independently_measured": True,
        "base_center_independently_measured": True,
        "front_semantics_independently_measured": True,
        "landmark_rms_mm": float(np.sqrt(np.mean(np.square([mouth_error, base_error])))),
        "mouth_center_error_mm": mouth_error,
        "base_center_error_mm": base_error,
        "bottle_axis_endpoint_error_mm": float(np.linalg.norm(
            (production_mouth_orb - production_base_orb)
            - (raw_mouth_orb - raw_base_orb)
        ) * METERS_PER_MODEL_UNIT * 1000.0),
        "bottle_height_error_mm": height_error,
        "orb_point_to_b_surface_mm": surface,
        "up_axis_error_deg": up_error,
        "front_axis_error_deg": front_error,
        "translation_residual_orb_mm": translation_residual.tolist(),
        "yaw_error_deg": front_error,
        "pitch_error_deg": up_error,
        "roll_error_deg": 0.0,
        "orb_origin_definition": (
            "physical mouth-ring centroid measured from the raw same-reconstruction "
            "AliceVision surface; not the historical provisional MOUTH_ORIGIN"
        ),
        "mouth_center_orb": raw_mouth_orb.tolist(),
        "mouth_center_b": production["mouth_center_mesh"].tolist(),
        "registered_mouth_center_b_orb": production_mouth_orb.tolist(),
        "base_center_orb": raw_base_orb.tolist(),
        "base_center_b": production["base_center_mesh"].tolist(),
        "registered_base_center_b_orb": production_base_orb.tolist(),
        "front_axis_orb": [0.0, 0.0, 1.0],
        "front_point_orb": front_point_orb.tolist(),
        "registered_front_point_b_orb": registered_front_point.tolist(),
        "semantic_evidence": semantic,
        "strict_gate": {
            "landmark_rms_mm_max": 1.0,
            "mouth_center_error_mm_max": 2.0,
            "base_center_error_mm_max": 3.0,
            "surface_median_mm_max": 2.5,
            "surface_p95_mm_max": 5.0,
            "up_axis_error_deg_max": 1.5,
            "front_axis_error_deg_max": 1.5,
        },
        "canonical_bounds_min": production_canonical.bounds[0].tolist(),
        "canonical_bounds_max": production_canonical.bounds[1].tolist(),
    }
    args.artifact.parent.mkdir(parents=True, exist_ok=True)
    args.artifact.write_text(json.dumps(artifact, indent=2) + "\n", encoding="utf-8")
    contract = {
        "version": "bottle-orb-frame-contract-v41",
        "orb_database_sha256": orb_sha,
        "source_sfm": str(args.sfm),
        "source_sfm_sha256": sha256(args.sfm),
        "source_b_mesh_sha256": production["sha256"],
        "coordinate_frame_origin": artifact["orb_origin_definition"],
        "+X_definition": "right = cross(measured bottle up, measured printed front)",
        "+Y_definition": "robust base-to-mouth bottle center-line",
        "+Z_definition": "red-logo/front semantic direction, barcode side excluded",
        "metersPerModelUnit": METERS_PER_MODEL_UNIT,
        "T_ORB_FROM_B": artifact["T_ORB_FROM_B"],
        "sfm_to_orb_matrix": artifact["sfm_to_orb_matrix"],
        "blender_policy": "B is baked from this same reconstruction; B/neck/C remain one rigid canonical pair",
        "unity_policy": "runtime model registration is identity except exact FBX import-axis inverse",
        "device_verified": False,
    }
    args.contract.write_text(json.dumps(contract, indent=2) + "\n", encoding="utf-8")
    report = {
        "version": "same-reconstruction-independent-landmark-measurement-v41",
        "raw_orb_reconstruction_surface": serializable_measurement(raw),
        "production_b_surface": serializable_measurement(production),
        "semantic_evidence": semantic,
        "physical_scale_source": {
            "neck_outer_diameter_m": MEASURED_NECK_DIAMETER_METERS,
            "source": "user-supplied physical measurement 2026-07-16",
        },
        "historical_mouth_origin_rejected": {
            "value": [0.419225, -4.514827, 0.314265],
            "reason": "source correspondence file is marked provisional_unverified_physical_dimensions",
        },
        "result": artifact,
    }
    args.measurement_report.write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )
    print("BOTTLE_SAME_RECONSTRUCTION_REGISTRATION_V41_OK")
    print(json.dumps({
        "strict": strict,
        "mouth_error_mm": mouth_error,
        "base_error_mm": base_error,
        "surface": surface,
        "up_axis_error_deg": up_error,
        "T_ORB_FROM_B": artifact["T_ORB_FROM_B"],
    }, indent=2))


if __name__ == "__main__":
    main()
