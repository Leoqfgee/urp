#!/usr/bin/env python3
"""Publish the approved no-cap scan B with a clean neck guide and cap C.

Run with Blender using the already registered B+C blend:

  blender --background bottle_no_cap_clean_cap_registered.blend \
    --python prepare_bottle_clean_cap.py -- \
    --blend-output bottle_no_cap_clean_cap_v29.blend \
    --fbx-output bottle_no_cap_clean_cap_v29.fbx \
    --report-output bottle_no_cap_clean_cap_v29_report.json

The scan remains the authoritative B geometry and coordinate frame.  The clean
neck is a visual child of B that restores only the cylindrical bottle-neck
silhouette that is absent from the noisy scan.  It is hidden together with B
after registration.  C remains the independently authored clean cap and is
never repositioned at runtime.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


METERS_PER_MODEL_UNIT = 0.17
NECK_OUTER_DIAMETER_METERS = 0.034
CAP_OUTER_DIAMETER_METERS = 0.039
CAP_HEIGHT_METERS = 0.010
SEGMENTS = 96
NECK_HEIGHT_MODEL_UNITS = CAP_HEIGHT_METERS / METERS_PER_MODEL_UNIT


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--blend-output", type=Path, required=True)
    parser.add_argument("--fbx-output", type=Path, required=True)
    parser.add_argument("--report-output", type=Path, required=True)
    return parser.parse_args(argv)


def require_object(name: str, object_type: str | None = None) -> bpy.types.Object:
    obj = bpy.data.objects.get(name)
    if obj is None or (object_type is not None and obj.type != object_type):
        raise RuntimeError(f"Missing required {object_type or ''} object {name}")
    return obj


def build_lathed_neck() -> bpy.types.Object:
    old = bpy.data.objects.get("ReferenceNeckProxyB")
    if old is not None:
        bpy.data.objects.remove(old, do_unlink=True)

    # Canonical asset is Y-up and the approved source registration defines the
    # physical mouth plane at Y=0. Device evidence proved that extending
    # the guide to 32.3 mm created an artificial long neck.  The real neck and
    # the clean cap are both about 10 mm high, and the cap screws around the
    # neck instead of being stacked above it.  Build a 10 mm guide immediately
    # below the mouth plane.  The clean cap keeps the original Blender
    # registration at Y=0 and therefore overlaps almost the entire guide.
    h = NECK_HEIGHT_MODEL_UNITS
    profile = [
        (-1.000 * h, 0.112),
        (-0.920 * h, 0.112),
        (-0.850 * h, 0.103),
        (-0.650 * h, 0.103),
        (-0.590 * h, 0.108),
        (-0.470 * h, 0.108),
        (-0.410 * h, 0.101),
        (-0.250 * h, 0.101),
        (-0.190 * h, 0.106),
        (-0.090 * h, 0.106),
        (-0.040 * h, 0.101),
        (0.000, 0.100),
    ]
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, ...]] = []
    for y, radius in profile:
        for segment in range(SEGMENTS):
            angle = 2.0 * math.pi * segment / SEGMENTS
            vertices.append((radius * math.cos(angle), y, radius * math.sin(angle)))

    for row in range(len(profile) - 1):
        for segment in range(SEGMENTS):
            next_segment = (segment + 1) % SEGMENTS
            a = row * SEGMENTS + segment
            b = row * SEGMENTS + next_segment
            c = (row + 1) * SEGMENTS + next_segment
            d = (row + 1) * SEGMENTS + segment
            faces.append((a, b, c, d))

    # Close the mouth and lower blend surfaces so the guide is well-defined
    # from front, oblique and top-down views.
    bottom_center = len(vertices)
    vertices.append((0.0, profile[0][0], 0.0))
    top_center = len(vertices)
    vertices.append((0.0, profile[-1][0], 0.0))
    for segment in range(SEGMENTS):
        next_segment = (segment + 1) % SEGMENTS
        faces.append((bottom_center, next_segment, segment))
        top_a = (len(profile) - 1) * SEGMENTS + segment
        top_b = (len(profile) - 1) * SEGMENTS + next_segment
        faces.append((top_center, top_a, top_b))

    mesh = bpy.data.meshes.new("ReferenceNeckProxyBMesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    neck = bpy.data.objects.new("ReferenceNeckProxyB", mesh)
    bpy.context.collection.objects.link(neck)

    material = bpy.data.materials.get("ReferenceNeckWhite")
    if material is None:
        material = bpy.data.materials.new("ReferenceNeckWhite")
    material.diffuse_color = (0.92, 0.92, 0.90, 1.0)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled is not None:
        principled.inputs["Base Color"].default_value = (0.92, 0.92, 0.90, 1.0)
        principled.inputs["Roughness"].default_value = 0.32
        principled.inputs["Metallic"].default_value = 0.0
    neck.data.materials.append(material)
    for polygon in neck.data.polygons:
        polygon.use_smooth = True
    return neck


def local_bounds(obj: bpy.types.Object) -> tuple[list[float], list[float]]:
    points = [vertex.co for vertex in obj.data.vertices]
    return (
        [float(min(point[axis] for point in points)) for axis in range(3)],
        [float(max(point[axis] for point in points)) for axis in range(3)],
    )


def main() -> None:
    args = parse_args()
    root = require_object("BottleRepairRoot")
    body = require_object("DamagedBottleB", "MESH")
    cap = require_object("BottleCapC", "MESH")
    if body.parent != root or cap.parent != root:
        raise RuntimeError("B and C must be rigid siblings under BottleRepairRoot")

    neck = build_lathed_neck()
    neck.parent = body
    neck.matrix_parent_inverse = Matrix.Identity(4)
    neck.location = Vector((0.0, 0.0, 0.0))
    neck.rotation_euler = Vector((0.0, 0.0, 0.0))
    neck.scale = Vector((1.0, 1.0, 1.0))
    cap.location = Vector((0.0, 0.0, 0.0))
    cap.rotation_euler = Vector((0.0, 0.0, 0.0))
    cap.scale = Vector((1.0, 1.0, 1.0))

    keep = {root, body, cap, neck}
    for obj in list(bpy.data.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    args.blend_output.parent.mkdir(parents=True, exist_ok=True)
    args.fbx_output.parent.mkdir(parents=True, exist_ok=True)
    args.report_output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(args.blend_output))

    bpy.ops.object.select_all(action="DESELECT")
    for obj in (root, body, cap, neck):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=str(args.fbx_output),
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        apply_unit_scale=True,
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
    )

    body_min, body_max = local_bounds(body)
    cap_local_min, cap_local_max = local_bounds(cap)
    neck_local_min, neck_local_max = local_bounds(neck)
    cap_min = [
        cap_local_min[0],
        cap_local_min[1],
        cap_local_min[2],
    ]
    cap_max = [
        cap_local_max[0],
        cap_local_max[1],
        cap_local_max[2],
    ]
    neck_min = [
        neck_local_min[0],
        neck_local_min[1],
        neck_local_min[2],
    ]
    neck_max = [
        neck_local_max[0],
        neck_local_max[1],
        neck_local_max[2],
    ]
    payload = {
        "version": "bottle-no-cap-clean-cap-v29",
        "runtimeHierarchy": {
            "root": root.name,
            "referenceB": body.name,
            "referenceNeckGuideB": neck.name,
            "repairC": cap.name,
        },
        "coordinateFrame": {
            "origin": "physical bottle mouth centre",
            "physicalMouthCentreModel": [
                0.0,
                0.0,
                0.0,
            ],
            "upAxis": "+Y from body to mouth",
            "printedFrontAxis": "+X",
            "metersPerModelUnit": METERS_PER_MODEL_UNIT,
        },
        "referenceB": {
            "source": (
                "F:\\Meshroom_work\\bottle_full_clean_v2\\split_models"
                "\\bottle_no_cap\\texturedMesh.obj"
            ),
            "boundsMin": body_min,
            "boundsMax": body_max,
        },
        "referenceNeckGuideB": {
            "purpose": (
                "opaque B-only coarse-alignment geometry; hidden with B after Start"
            ),
            "outerDiameterMeters": NECK_OUTER_DIAMETER_METERS,
            "boundsMin": neck_min,
            "boundsMax": neck_max,
        },
        "repairC": {
            "source": "bottle_cap_clean_39x10mm.obj",
            "outerDiameterMeters": CAP_OUTER_DIAMETER_METERS,
            "heightMeters": CAP_HEIGHT_METERS,
            "boundsMin": cap_min,
            "boundsMax": cap_max,
        },
        "capSeating": {
            "bCutDatumModelY": 0.0,
            "mouthPlaneModelY": 0.0,
            "neckHeightMeters": (
                (neck_max[1] - neck_min[1]) * METERS_PER_MODEL_UNIT
            ),
            "capHeightMetersFromBounds": (
                (cap_max[1] - cap_min[1]) * METERS_PER_MODEL_UNIT
            ),
            "capBottomMeters": cap_min[1] * METERS_PER_MODEL_UNIT,
            "capTopMeters": cap_max[1] * METERS_PER_MODEL_UNIT,
            "neckTopMeters": neck_max[1] * METERS_PER_MODEL_UNIT,
            "neckMaximumDiameterMeters": (
                max(abs(neck_min[0]), abs(neck_max[0])) * 2.0
                * METERS_PER_MODEL_UNIT
            ),
            "capOverlapsNeckAxially": (
                cap_min[1] <= neck_max[1] and cap_max[1] >= neck_max[1]
            ),
        },
        "rigidContract": {
            "bLocalMatrix": [float(value) for row in body.matrix_local for value in row],
            "neckLocalMatrix": [float(value) for row in neck.matrix_local for value in row],
            "cLocalMatrix": [float(value) for row in cap.matrix_local for value in row],
            "cIsNeverPositionedIndependentlyAtRuntime": True,
        },
        "deviceOverlayVerified": False,
    }
    args.report_output.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print("BOTTLE_CLEAN_CAP_V29_OK")
    print(json.dumps(payload, ensure_ascii=False))


if __name__ == "__main__":
    main()
