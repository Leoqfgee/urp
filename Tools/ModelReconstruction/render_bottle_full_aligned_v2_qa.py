#!/usr/bin/env python3
"""Render six fixed QA views of the canonical rigid B+C Blender asset."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args(argv)


def point_at(obj: bpy.types.Object, target: Vector) -> None:
    # This asset is canonical Y-up (mouth at Y=0, body toward negative Y).
    # Blender's to_track_quat uses its conventional Z-up frame and can roll a
    # near-horizontal camera by 180 degrees here, so construct a Y-up camera
    # basis explicitly.
    forward = (target - obj.location).normalized()
    world_up = Vector((0.0, 1.0, 0.0))
    if abs(forward.dot(world_up)) > 0.98:
        world_up = Vector((0.0, 0.0, 1.0))
    right = forward.cross(world_up).normalized()
    up = right.cross(forward).normalized()
    rotation = Matrix((right, up, -forward)).transposed()
    obj.rotation_euler = rotation.to_euler()


def main() -> None:
    args = parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    body = bpy.data.objects.get("DamagedBottleB")
    cap = bpy.data.objects.get("BottleCapC")
    root = bpy.data.objects.get("BottleRepairRoot")
    if body is None or cap is None or root is None:
        raise RuntimeError("Expected BottleRepairRoot/DamagedBottleB/BottleCapC")
    if body.parent != root or cap.parent != root:
        raise RuntimeError("B and C must be siblings under BottleRepairRoot")

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 720
    scene.render.resolution_y = 960
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.world.color = (0.018, 0.022, 0.028)
    scene.view_settings.look = "AgX - Medium High Contrast"

    camera_data = bpy.data.cameras.new("BottleBCQACameraData")
    camera_data.lens = 58.0
    camera = bpy.data.objects.new("BottleBCQACamera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera

    target = Vector((0.0, -0.56, 0.0))
    views = {
        "front": Vector((0.0, -0.48, 2.35)),
        "back": Vector((0.0, -0.48, -2.35)),
        "left": Vector((-2.35, -0.48, 0.0)),
        "right": Vector((2.35, -0.48, 0.0)),
        "top": Vector((0.0, 2.05, 0.35)),
        "oblique": Vector((1.55, 0.35, 1.75)),
    }

    lights = []
    for name, energy, size in (
        ("Key", 180.0, 2.0),
        ("Fill", 75.0, 2.5),
        ("Rim", 110.0, 1.6),
    ):
        data = bpy.data.lights.new(f"BottleBCQA{name}Data", "AREA")
        data.energy = energy
        data.shape = "DISK"
        data.size = size
        light = bpy.data.objects.new(f"BottleBCQA{name}", data)
        bpy.context.collection.objects.link(light)
        lights.append(light)

    rendered = []
    for name, location in views.items():
        camera.location = location
        point_at(camera, target)
        right = camera.matrix_world.to_quaternion() @ Vector((1.0, 0.0, 0.0))
        up = camera.matrix_world.to_quaternion() @ Vector((0.0, 1.0, 0.0))
        forward = camera.matrix_world.to_quaternion() @ Vector((0.0, 0.0, -1.0))
        lights[0].location = camera.location + right * 0.8 + up * 0.8 + forward * 0.2
        lights[1].location = camera.location - right * 1.1 + up * 0.2
        lights[2].location = target - forward * 1.4 + up * 0.5
        for light in lights:
            point_at(light, target)
        output = args.output / f"{name}.png"
        scene.render.filepath = str(output)
        bpy.ops.render.render(write_still=True)
        rendered.append({
            "view": name,
            "image": output.name,
            "cameraLocation": [float(value) for value in location],
        })

    payload = {
        "version": "bottle-full-aligned-v2-qa",
        "hierarchy": "BottleRepairRoot/DamagedBottleB + BottleCapC",
        "bodyLocalMatrix": [float(value) for row in body.matrix_local for value in row],
        "capLocalMatrix": [float(value) for row in cap.matrix_local for value in row],
        "views": rendered,
        "deviceOverlayVerified": False,
    }
    (args.output / "views.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print("BOTTLE_BC_QA_RENDER_OK")


if __name__ == "__main__":
    main()
