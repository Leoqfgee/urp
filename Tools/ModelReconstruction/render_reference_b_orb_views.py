#!/usr/bin/env python3
"""Blender-side renderer/ray caster for the registered no-cap bottle model b.

This script is intentionally Blender-only.  The companion system-Python script
`generate_reference_b_orb_database.py` runs ORB on the rendered PNG files, then
invokes this script a second time to map every 2D keypoint back to the exact
surface point of b.  Repair part c is hidden in both passes.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


REFERENCE_NAME = "DamagedBottleB"
CAP_NAME = "BottleCapC"
TARGET = Vector((0.0, -0.58, 0.0))


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("render", "raycast", "nearest"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--keypoints", type=Path)
    parser.add_argument("--width", type=int, default=720)
    parser.add_argument("--height", type=int, default=960)
    return parser.parse_args(argv)


def reference_objects() -> list[bpy.types.Object]:
    body = bpy.data.objects.get(REFERENCE_NAME)
    cap = bpy.data.objects.get(CAP_NAME)
    if body is None or body.type != "MESH":
        raise RuntimeError(f"Expected one mesh named {REFERENCE_NAME!r}")
    if cap is None or cap.type != "MESH":
        raise RuntimeError(f"Expected one mesh named {CAP_NAME!r}")
    if body.parent is None or cap.parent != body.parent:
        raise RuntimeError("DamagedBottleB and BottleCapC must share BottleRepairRoot")
    return [body]


def isolate_reference_b(objects: list[bpy.types.Object]) -> None:
    keep = set(objects)
    for obj in bpy.context.scene.objects:
        visible = obj in keep
        obj.hide_render = not visible
        obj.hide_set(not visible)
    if any(obj.name == CAP_NAME and not obj.hide_render for obj in bpy.context.scene.objects):
        raise RuntimeError("Repair cap c must not be visible while generating b features")


def point_camera(camera: bpy.types.Object, target: Vector = TARGET) -> None:
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()


def camera_location(azimuth_degrees: float, elevation_degrees: float, distance: float) -> Vector:
    azimuth = math.radians(azimuth_degrees)
    elevation = math.radians(elevation_degrees)
    horizontal = distance * math.cos(elevation)
    # Canonical coordinates are x-right, y-up and z-front.
    return TARGET + Vector((
        horizontal * math.sin(azimuth),
        distance * math.sin(elevation),
        horizontal * math.cos(azimuth),
    ))


def ensure_camera() -> bpy.types.Object:
    data = bpy.data.cameras.new("ReferenceBOrbCameraData")
    data.lens = 52.0
    data.sensor_width = 36.0
    data.sensor_fit = "HORIZONTAL"
    camera = bpy.data.objects.new("ReferenceBOrbCamera", data)
    bpy.context.collection.objects.link(camera)
    bpy.context.scene.camera = camera
    return camera


def intrinsics(camera: bpy.types.Object, width: int, height: int) -> dict[str, float]:
    fx = camera.data.lens / camera.data.sensor_width * width
    # Square pixels and horizontal sensor fit give the same focal length in pixels.
    return {
        "fx": fx,
        "fy": fx,
        "cx": width * 0.5,
        "cy": height * 0.5,
    }


def render_views(args: argparse.Namespace, objects: list[bpy.types.Object]) -> None:
    args.output.mkdir(parents=True, exist_ok=True)
    isolate_reference_b(objects)
    camera = ensure_camera()
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = args.width
    scene.render.resolution_y = args.height
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    scene.render.image_settings.color_depth = "8"
    scene.view_settings.look = "AgX - Medium High Contrast"

    # Camera-relative area lights give repeatable yet non-flat texture shading.
    lights: list[bpy.types.Object] = []
    for name, energy, size in (("Key", 850.0, 2.4), ("Fill", 450.0, 2.0)):
        data = bpy.data.lights.new(f"ReferenceB{name}Data", "AREA")
        data.energy = energy
        data.shape = "DISK"
        data.size = size
        light = bpy.data.objects.new(f"ReferenceB{name}", data)
        bpy.context.collection.objects.link(light)
        light.hide_render = False
        light.hide_set(False)
        lights.append(light)

    views = []
    index = 0
    for elevation in (-10.0, 4.0, 16.0):
        for azimuth in range(0, 360, 30):
            for distance in (2.75, 3.35):
                camera.location = camera_location(float(azimuth), elevation, distance)
                point_camera(camera)
                right = camera.matrix_world.to_quaternion() @ Vector((1.0, 0.0, 0.0))
                up = camera.matrix_world.to_quaternion() @ Vector((0.0, 1.0, 0.0))
                lights[0].location = camera.location + right * 0.8 + up * 0.9
                lights[1].location = camera.location - right * 1.1 + up * 0.25
                point_camera(lights[0])
                point_camera(lights[1])

                view_id = f"b_{index:03d}_a{azimuth:03d}_e{int(elevation):+03d}_d{int(distance * 100):03d}"
                image_path = args.output / f"{view_id}.png"
                scene.render.filepath = str(image_path)
                bpy.ops.render.render(write_still=True)
                values = intrinsics(camera, args.width, args.height)
                views.append({
                    "id": view_id,
                    "image": image_path.name,
                    "azimuth_degrees": float(azimuth),
                    "elevation_degrees": elevation,
                    "distance_model_units": distance,
                    "camera_location_blender": list(camera.location),
                    **values,
                })
                index += 1

    metadata = {
        "version": "reference-b-render-v1",
        "reference_objects": [obj.name for obj in objects],
        "repair_c_excluded": True,
        "coordinate_conversion": "canonical Blender and Unity model coordinates are identical",
        "resolution": [args.width, args.height],
        "target_blender": list(TARGET),
        "views": views,
    }
    (args.output / "views.json").write_text(
        json.dumps(metadata, indent=2) + "\n", encoding="utf-8"
    )
    print(f"REFERENCE_B_RENDERED views={len(views)} output={args.output}")


def build_bvhs(objects: list[bpy.types.Object]):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    result = []
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        bvh = BVHTree.FromObject(evaluated, depsgraph)
        if bvh is None:
            raise RuntimeError(f"Could not build BVH for {obj.name}")
        result.append((obj, bvh))
    return result


def ray_for_pixel(camera: bpy.types.Object, pixel_x: float, pixel_y: float,
                  width: int, height: int, values: dict[str, float]) -> tuple[Vector, Vector]:
    direction_camera = Vector((
        (pixel_x - values["cx"]) / values["fx"],
        -(pixel_y - values["cy"]) / values["fy"],
        -1.0,
    )).normalized()
    origin = camera.matrix_world.translation.copy()
    direction = (camera.matrix_world.to_quaternion() @ direction_camera).normalized()
    return origin, direction


def nearest_hit(origin_world: Vector, direction_world: Vector, bvhs) -> Vector | None:
    best = None
    best_distance = float("inf")
    for obj, bvh in bvhs:
        inverse = obj.matrix_world.inverted()
        origin_local = inverse @ origin_world
        direction_local = (inverse.to_3x3() @ direction_world).normalized()
        location, _normal, _face, distance = bvh.ray_cast(origin_local, direction_local)
        if location is None:
            continue
        world = obj.matrix_world @ location
        world_distance = (world - origin_world).length
        if world_distance < best_distance:
            best = world
            best_distance = world_distance
    return best


def blender_to_canonical(point: Vector) -> list[float]:
    return [float(point.x), float(point.y), float(point.z)]


def raycast_keypoints(args: argparse.Namespace, objects: list[bpy.types.Object]) -> None:
    if args.keypoints is None:
        raise RuntimeError("--keypoints is required in raycast mode")
    args.output.mkdir(parents=True, exist_ok=True)
    payload = json.loads(args.keypoints.read_text(encoding="utf-8"))
    width, height = payload["resolution"]
    camera = ensure_camera()
    bvhs = build_bvhs(objects)
    hits = []
    hit_count = 0
    for view in payload["views"]:
        camera.location = camera_location(
            view["azimuth_degrees"], view["elevation_degrees"], view["distance_model_units"]
        )
        point_camera(camera)
        # Ray casting runs without a render call, so Blender has not otherwise
        # evaluated the camera transform for this view.
        bpy.context.view_layer.update()
        values = {name: float(view[name]) for name in ("fx", "fy", "cx", "cy")}
        view_hits = []
        for keypoint in view["keypoints"]:
            origin, direction = ray_for_pixel(
                camera, keypoint["x"], keypoint["y"], width, height, values
            )
            hit = nearest_hit(origin, direction, bvhs)
            if hit is None:
                continue
            view_hits.append({
                "index": keypoint["index"],
                "canonical": blender_to_canonical(hit),
            })
            hit_count += 1
        hits.append({"id": view["id"], "hits": view_hits})

    output_path = args.output / "surface_hits.json"
    output_path.write_text(
        json.dumps({
            "version": "reference-b-raycast-v1",
            "repair_c_excluded": True,
            "views": hits,
        }, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"REFERENCE_B_RAYCAST hits={hit_count} output={output_path}")


def canonical_to_blender(point: list[float]) -> Vector:
    return Vector((float(point[0]), -float(point[2]), float(point[1])))


def nearest_surface_point(point_world: Vector, bvhs) -> tuple[Vector | None, float]:
    best = None
    best_distance = float("inf")
    for obj, bvh in bvhs:
        inverse = obj.matrix_world.inverted()
        point_local = inverse @ point_world
        location, _normal, _face, _distance = bvh.find_nearest(point_local)
        if location is None:
            continue
        world = obj.matrix_world @ location
        distance = (world - point_world).length
        if distance < best_distance:
            best = world
            best_distance = distance
    return best, best_distance


def register_points_to_reference(args: argparse.Namespace, objects: list[bpy.types.Object]) -> None:
    if args.keypoints is None:
        raise RuntimeError("--keypoints must point to canonical_points.json in nearest mode")
    args.output.mkdir(parents=True, exist_ok=True)
    payload = json.loads(args.keypoints.read_text(encoding="utf-8"))
    bvhs = build_bvhs(objects)
    registered = []
    for index, point in enumerate(payload["points"]):
        source = canonical_to_blender(point)
        hit, distance = nearest_surface_point(source, bvhs)
        registered.append({
            "index": index,
            "source_canonical": point,
            "registered_canonical": None if hit is None else blender_to_canonical(hit),
            "distance_model_units": None if hit is None else float(distance),
        })
    output_path = args.output / "registered_observed_points.json"
    output_path.write_text(
        json.dumps({
            "version": "observed-orb-to-reference-b-v1",
            "repair_c_excluded": True,
            "points": registered,
        }, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"REFERENCE_B_NEAREST points={len(registered)} output={output_path}")


def main() -> None:
    args = parse_args()
    objects = reference_objects()
    if args.mode == "render":
        render_views(args, objects)
    elif args.mode == "raycast":
        raycast_keypoints(args, objects)
    else:
        register_points_to_reference(args, objects)


if __name__ == "__main__":
    main()
