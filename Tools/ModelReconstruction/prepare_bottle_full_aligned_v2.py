#!/usr/bin/env python3
"""Canonicalize and export the Blender-authored rigid bottle B+C asset.

Run with Blender:
  blender --background input.blend --python prepare_bottle_full_aligned_v2.py -- \
    --blend-output output.blend --fbx-output output.fbx --report-output report.json

The source split uses one shared Meshroom coordinate system.  This script applies
one uniform transform to both meshes, places the bottle-mouth seam at the origin,
keeps Y as the physical up axis, and bakes all object transforms.  The resulting
FBX hierarchy is the runtime contract:

  BottleRepairRoot
    DamagedBottleB
    BottleCapC
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


CANONICAL_BODY_HEIGHT_UNITS = 1.2
METERS_PER_MODEL_UNIT = 0.17


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--blend-output", type=Path, required=True)
    parser.add_argument("--fbx-output", type=Path, required=True)
    parser.add_argument("--report-output", type=Path, required=True)
    return parser.parse_args(argv)


def find_mesh(*names: str) -> bpy.types.Object:
    for name in names:
        obj = bpy.data.objects.get(name)
        if obj is not None and obj.type == "MESH":
            return obj
    raise RuntimeError(f"Missing mesh object; expected one of {names}")


def local_points(obj: bpy.types.Object) -> list[Vector]:
    return [vertex.co.copy() for vertex in obj.data.vertices]


def bounds(points: list[Vector]) -> tuple[Vector, Vector]:
    return (
        Vector(tuple(min(point[axis] for point in points) for axis in range(3))),
        Vector(tuple(max(point[axis] for point in points) for axis in range(3))),
    )


def vector_list(value: Vector) -> list[float]:
    return [float(component) for component in value]


def main() -> None:
    args = parse_args()
    body = find_mesh("BottleBody", "DamagedBottleB")
    cap = find_mesh("BottleCap", "BottleCapC")
    body_points = local_points(body)
    cap_points = local_points(cap)
    body_min, body_max = bounds(body_points)
    cap_min, cap_max = bounds(cap_points)
    body_height = body_max.y - body_min.y
    if body_height <= 1e-6:
        raise RuntimeError("Bottle body has no usable height")

    top_band = [
        point for point in body_points
        if point.y >= body_max.y - body_height * 0.035
    ]
    if len(top_band) < 16:
        raise RuntimeError("Bottle mouth band contains too few vertices")
    top_x = sorted(point.x for point in top_band)
    top_z = sorted(point.z for point in top_band)
    mouth_center = Vector((
        top_x[len(top_x) // 2],
        body_max.y,
        top_z[len(top_z) // 2],
    ))
    uniform_scale = CANONICAL_BODY_HEIGHT_UNITS / body_height
    canonical = (
        Matrix.Scale(uniform_scale, 4)
        @ Matrix.Translation(-mouth_center)
    )

    old_parent = body.parent
    for obj in (body, cap):
        # Both meshes were split from one reconstruction.  Applying this same
        # matrix preserves their exact seam and all relative B/C geometry.
        obj.data.transform(canonical)
        obj.data.update()
        obj.parent = None
        obj.matrix_world = Matrix.Identity(4)

    root = old_parent if old_parent is not None and old_parent.type == "EMPTY" else None
    if root is None:
        root = bpy.data.objects.new("BottleRepairRoot", None)
        bpy.context.collection.objects.link(root)
    root.name = "BottleRepairRoot"
    root.matrix_world = Matrix.Identity(4)
    body.name = "DamagedBottleB"
    cap.name = "BottleCapC"
    for obj in (body, cap):
        obj.parent = root
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.location = Vector((0.0, 0.0, 0.0))
        obj.rotation_euler = Vector((0.0, 0.0, 0.0))
        obj.scale = Vector((1.0, 1.0, 1.0))

    # Remove stale cameras/lights/helpers so the exported file has one explicit
    # semantic hierarchy and no runtime-surprising nodes.
    keep = {root, body, cap}
    for obj in list(bpy.data.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    canonical_body_points = local_points(body)
    canonical_cap_points = local_points(cap)
    canonical_body_min, canonical_body_max = bounds(canonical_body_points)
    canonical_cap_min, canonical_cap_max = bounds(canonical_cap_points)
    seam_delta = canonical_cap_min.y - canonical_body_max.y

    args.blend_output.parent.mkdir(parents=True, exist_ok=True)
    args.fbx_output.parent.mkdir(parents=True, exist_ok=True)
    args.report_output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(args.blend_output))

    bpy.ops.object.select_all(action="DESELECT")
    for obj in (root, body, cap):
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

    report = {
        "version": "bottle-full-aligned-v2-canonical",
        "runtimeHierarchy": {
            "root": root.name,
            "referenceB": body.name,
            "repairC": cap.name,
        },
        "sourceSharedTransform": {
            "mouthCenter": vector_list(mouth_center),
            "uniformScale": uniform_scale,
            "canonicalBodyHeightUnits": CANONICAL_BODY_HEIGHT_UNITS,
            "metersPerModelUnit": METERS_PER_MODEL_UNIT,
            "canonicalBodyHeightMeters": (
                CANONICAL_BODY_HEIGHT_UNITS * METERS_PER_MODEL_UNIT
            ),
        },
        "referenceB": {
            "vertices": len(body.data.vertices),
            "polygons": len(body.data.polygons),
            "boundsMin": vector_list(canonical_body_min),
            "boundsMax": vector_list(canonical_body_max),
            "localPosition": vector_list(body.location),
            "localRotationRadians": vector_list(body.rotation_euler),
            "localScale": vector_list(body.scale),
        },
        "repairC": {
            "vertices": len(cap.data.vertices),
            "polygons": len(cap.data.polygons),
            "boundsMin": vector_list(canonical_cap_min),
            "boundsMax": vector_list(canonical_cap_max),
            "localPosition": vector_list(cap.location),
            "localRotationRadians": vector_list(cap.rotation_euler),
            "localScale": vector_list(cap.scale),
        },
        "seam": {
            "capMinimumYMinusBodyMaximumY": seam_delta,
            "interpretation": (
                "small negative means the scan surfaces overlap slightly at the split seam"
                if seam_delta < 0.0
                else "positive means a gap exists at the split seam"
            ),
        },
        "relationshipStorage": [
            str(args.blend_output),
            str(args.fbx_output),
        ],
        "rigidRelationshipPreserved": True,
        "deviceOverlayVerified": False,
    }
    args.report_output.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print("BOTTLE_BC_CANONICAL_OK")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
