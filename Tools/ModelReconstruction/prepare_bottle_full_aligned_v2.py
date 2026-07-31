#!/usr/bin/env python3
"""Publish the single production bottle B+C asset.

Run Blender with the already mouth-centred registered B+C source:

  blender --background bottle_no_cap_clean_cap_registered.blend \
    --python prepare_bottle_full_aligned_v2.py -- \
    --blend-output bottle_full_aligned_v2.blend \
    --fbx-output bottle_full_aligned_v2.fbx \
    --report-output bottle_full_aligned_v2_report.json

The approved Meshroom scan ends at the damaged shoulder cut (model Y=0), not
at the physical bottle mouth.  A clean 10 mm neck is therefore part of B and
is hidden with B after registration.  The independently authored 39 x 10 mm
cap C is lifted by the same 10 mm in mesh coordinates.  B and C remain rigid
siblings under BottleRepairRoot and all runtime object transforms stay identity.
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
NECK_HEIGHT_METERS = 0.010
NECK_HEIGHT_MODEL_UNITS = NECK_HEIGHT_METERS / METERS_PER_MODEL_UNIT
NECK_OUTER_DIAMETER_METERS = 0.034
CAP_OUTER_DIAMETER_METERS = 0.039
CAP_HEIGHT_METERS = 0.010
SEGMENTS = 96


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


def build_neck() -> bpy.types.Object:
    old = bpy.data.objects.get("ReferenceNeckProxyB")
    if old is not None:
        bpy.data.objects.remove(old, do_unlink=True)

    # Y-up profile measured from the supplied front photograph.  The complete
    # visible neck is about the same height as the 10 mm cap.  Rings are kept
    # compact: no elongated stem and no extra runtime offset.
    h = NECK_HEIGHT_MODEL_UNITS
    profile = [
        (0.000 * h, 0.086),
        (0.100 * h, 0.086),
        (0.160 * h, 0.101),
        (0.190 * h, 0.112),
        (0.350 * h, 0.112),
        (0.410 * h, 0.101),
        (0.560 * h, 0.101),
        (0.600 * h, 0.108),
        (0.710 * h, 0.108),
        (0.770 * h, 0.101),
        (0.820 * h, 0.107),
        (0.940 * h, 0.107),
        (1.000 * h, 0.100),
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


def matrix_values(obj: bpy.types.Object) -> list[float]:
    return [float(value) for row in obj.matrix_local for value in row]


def main() -> None:
    args = parse_args()
    root = require_object("BottleRepairRoot")
    body = require_object("DamagedBottleB", "MESH")
    cap = require_object("BottleCapC", "MESH")
    if body.parent != root or cap.parent != root:
        raise RuntimeError("B and C must be rigid siblings under BottleRepairRoot")

    neck = build_neck()
    neck.parent = body
    neck.matrix_parent_inverse = Matrix.Identity(4)
    neck.location = Vector((0.0, 0.0, 0.0))
    neck.rotation_euler = Vector((0.0, 0.0, 0.0))
    neck.scale = Vector((1.0, 1.0, 1.0))

    # The source cap's inner roof is aligned to the damaged Y=0 cut.  Bake the
    # 10 mm physical-mouth lift into vertices so Unity imports identity object
    # transforms and cannot reinterpret Blender +Y as a sideways local offset.
    for vertex in cap.data.vertices:
        vertex.co.y += NECK_HEIGHT_MODEL_UNITS
    cap.data.update()
    cap.location = Vector((0.0, 0.0, 0.0))
    cap.rotation_euler = Vector((0.0, 0.0, 0.0))
    cap.scale = Vector((1.0, 1.0, 1.0))

    for obj in (root, body):
        obj.location = Vector((0.0, 0.0, 0.0))
        obj.rotation_euler = Vector((0.0, 0.0, 0.0))
        obj.scale = Vector((1.0, 1.0, 1.0))

    keep = {root, body, neck, cap}
    for obj in list(bpy.data.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    args.blend_output.parent.mkdir(parents=True, exist_ok=True)
    args.fbx_output.parent.mkdir(parents=True, exist_ok=True)
    args.report_output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(args.blend_output))

    bpy.ops.object.select_all(action="DESELECT")
    for obj in (root, body, neck, cap):
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
    neck_min, neck_max = local_bounds(neck)
    cap_min, cap_max = local_bounds(cap)
    payload = {
        "version": "bottle-full-aligned-v2-rigid-neck-cap-v33",
        "runtimeHierarchy": {
            "root": root.name,
            "referenceB": body.name,
            "referenceNeckB": neck.name,
            "repairC": cap.name,
        },
        "coordinateFrame": {
            "origin": "damaged B shoulder cut datum",
            "physicalMouthCentreModel": [0.0, NECK_HEIGHT_MODEL_UNITS, 0.0],
            "upAxis": "+Y from shoulder cut to mouth",
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
            "localPosition": list(body.location),
            "localRotationRadians": list(body.rotation_euler),
            "localScale": list(body.scale),
        },
        "referenceNeckB": {
            "purpose": "B-only alignment geometry; hidden with B after Start",
            "heightMeters": NECK_HEIGHT_METERS,
            "outerDiameterMeters": NECK_OUTER_DIAMETER_METERS,
            "boundsMin": neck_min,
            "boundsMax": neck_max,
            "localPosition": list(neck.location),
            "localRotationRadians": list(neck.rotation_euler),
            "localScale": list(neck.scale),
        },
        "repairC": {
            "source": "bottle_cap_clean_39x10mm.obj",
            "outerDiameterMeters": CAP_OUTER_DIAMETER_METERS,
            "heightMeters": CAP_HEIGHT_METERS,
            "boundsMin": cap_min,
            "boundsMax": cap_max,
            "localPosition": list(cap.location),
            "localRotationRadians": list(cap.rotation_euler),
            "localScale": list(cap.scale),
        },
        "registration": {
            "method": (
                "Blender rigid registration in B coordinates; 10 mm B neck "
                "from scan cut to physical mouth; C lift baked into mesh"
            ),
            "bCutDatumModelY": 0.0,
            "mouthPlaneModelY": NECK_HEIGHT_MODEL_UNITS,
            "neckHeightMetersFromBounds": (
                (neck_max[1] - neck_min[1]) * METERS_PER_MODEL_UNIT
            ),
            "capHeightMetersFromBounds": (
                (cap_max[1] - cap_min[1]) * METERS_PER_MODEL_UNIT
            ),
            "capBottomMeters": cap_min[1] * METERS_PER_MODEL_UNIT,
            "capTopMeters": cap_max[1] * METERS_PER_MODEL_UNIT,
            "capOverlapsNeckAxially": (
                cap_min[1] <= neck_max[1] and cap_max[1] >= neck_min[1]
            ),
            "bToCLocalPosition": list(cap.location),
            "bToCLocalRotationRadians": list(cap.rotation_euler),
            "bToCLocalScale": list(cap.scale),
        },
        "rigidContract": {
            "bLocalMatrix": matrix_values(body),
            "neckLocalMatrix": matrix_values(neck),
            "cLocalMatrix": matrix_values(cap),
            "cIsNeverPositionedIndependentlyAtRuntime": True,
        },
        "rigidRelationshipPreserved": True,
        "deviceOverlayVerified": False,
    }
    args.report_output.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print("BOTTLE_FULL_ALIGNED_V33_OK")
    print(json.dumps(payload, ensure_ascii=False))


if __name__ == "__main__":
    main()
