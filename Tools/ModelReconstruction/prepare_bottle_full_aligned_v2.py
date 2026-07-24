#!/usr/bin/env python3
"""Build and export the Blender-authored rigid bottle B+C asset.

Run with Blender:
  blender --background input.blend --python prepare_bottle_full_aligned_v2.py -- \
    --clean-cap bottle_cap_clean_39x10mm.obj \
    --blend-output output.blend --fbx-output output.fbx --report-output report.json

The no-cap body B comes from the approved Meshroom split.  The clean cap C is a
measured 39 x 10 mm Blender model whose +Z axis starts at its bottom rim.  This
script places the bottle mouth at the shared origin, aligns the cap's 8.65 mm
inner roof to that mouth plane, and bakes both meshes into one common frame.  The
resulting FBX hierarchy is the runtime contract:

  BottleRepairRoot
    DamagedBottleB
    BottleCapC
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


CANONICAL_BODY_HEIGHT_UNITS = 1.2
METERS_PER_MODEL_UNIT = 0.17
CLEAN_CAP_OUTER_DIAMETER_METERS = 0.039
CLEAN_CAP_INNER_DIAMETER_METERS = 0.0344
CLEAN_CAP_HEIGHT_METERS = 0.010
CLEAN_CAP_INNER_ROOF_HEIGHT_METERS = 0.00865


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--clean-cap", type=Path, required=True)
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


def remove_old_cap() -> None:
    for name in ("BottleCap", "BottleCapC"):
        obj = bpy.data.objects.get(name)
        if obj is not None:
            bpy.data.objects.remove(obj, do_unlink=True)


def import_clean_cap(path: Path) -> bpy.types.Object:
    if not path.is_file():
        raise FileNotFoundError(path)
    bpy.ops.object.select_all(action="DESELECT")
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=str(path))
    imported = [
        obj for obj in bpy.data.objects
        if obj not in before and obj.type == "MESH"
    ]
    if len(imported) != 1:
        raise RuntimeError(
            f"Expected exactly one clean cap mesh in {path}, got {len(imported)}"
        )
    return imported[0]


def local_points(obj: bpy.types.Object) -> list[Vector]:
    return [vertex.co.copy() for vertex in obj.data.vertices]


def bounds(points: list[Vector]) -> tuple[Vector, Vector]:
    return (
        Vector(tuple(min(point[axis] for point in points) for axis in range(3))),
        Vector(tuple(max(point[axis] for point in points) for axis in range(3))),
    )


def vector_list(value: Vector) -> list[float]:
    return [float(component) for component in value]


def matrix_list(value: Matrix) -> list[list[float]]:
    return [
        [float(value[row][column]) for column in range(4)]
        for row in range(4)
    ]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def main() -> None:
    args = parse_args()
    body = find_mesh("BottleBody", "DamagedBottleB")
    remove_old_cap()
    body_points = local_points(body)
    body_min, body_max = bounds(body_points)
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
    body.data.transform(canonical)
    body.data.update()
    body.parent = None
    body.matrix_world = Matrix.Identity(4)

    cap = import_clean_cap(args.clean_cap)
    cap_to_canonical = (
        Matrix.Translation((
            0.0,
            -CLEAN_CAP_INNER_ROOF_HEIGHT_METERS / METERS_PER_MODEL_UNIT,
            0.0,
        ))
        @ Matrix.Rotation(-math.pi / 2.0, 4, "X")
        @ Matrix.Scale(1.0 / METERS_PER_MODEL_UNIT, 4)
    )
    cap.data.transform(cap.matrix_world)
    cap.data.transform(cap_to_canonical)
    cap.data.update()
    cap.parent = None
    cap.matrix_world = Matrix.Identity(4)

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
    cap_bottom_below_mouth = canonical_body_max.y - canonical_cap_min.y
    cap_top_above_mouth = canonical_cap_max.y - canonical_body_max.y
    body_mouth_outer_diameter_units = 0.034 / METERS_PER_MODEL_UNIT
    cap_inner_diameter_units = (
        CLEAN_CAP_INNER_DIAMETER_METERS / METERS_PER_MODEL_UNIT
    )
    radial_clearance_units = (
        cap_inner_diameter_units - body_mouth_outer_diameter_units
    ) * 0.5

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
        "version": "bottle-no-cap-clean-cap-rigid-registration-v3",
        "runtimeHierarchy": {
            "root": root.name,
            "referenceB": body.name,
            "repairC": cap.name,
        },
        "sourceSharedTransform": {
            "bodySource": (
                "F:\\Meshroom_work\\bottle_full_clean_v2\\split_models"
                "\\bottle_no_cap\\texturedMesh.obj"
            ),
            "cleanCapSource": str(args.clean_cap),
            "cleanCapSha256": sha256(args.clean_cap),
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
            "outerDiameterMeters": CLEAN_CAP_OUTER_DIAMETER_METERS,
            "innerDiameterMeters": CLEAN_CAP_INNER_DIAMETER_METERS,
            "heightMeters": CLEAN_CAP_HEIGHT_METERS,
            "innerRoofHeightMeters": CLEAN_CAP_INNER_ROOF_HEIGHT_METERS,
            "vertices": len(cap.data.vertices),
            "polygons": len(cap.data.polygons),
            "boundsMin": vector_list(canonical_cap_min),
            "boundsMax": vector_list(canonical_cap_max),
            "localPosition": vector_list(cap.location),
            "localRotationRadians": vector_list(cap.rotation_euler),
            "localScale": vector_list(cap.scale),
        },
        "registration": {
            "method": (
                "Blender rigid registration: common mouth-centred frame; "
                "clean cap inner roof aligned to the B mouth plane"
            ),
            "capSourceToCanonicalMatrix": matrix_list(cap_to_canonical),
            "mouthPlaneY": float(canonical_body_max.y),
            "capBottomBelowMouthUnits": float(cap_bottom_below_mouth),
            "capBottomBelowMouthMeters": (
                float(cap_bottom_below_mouth) * METERS_PER_MODEL_UNIT
            ),
            "capTopAboveMouthUnits": float(cap_top_above_mouth),
            "capTopAboveMouthMeters": (
                float(cap_top_above_mouth) * METERS_PER_MODEL_UNIT
            ),
            "radialClearanceUnits": float(radial_clearance_units),
            "radialClearanceMeters": (
                float(radial_clearance_units) * METERS_PER_MODEL_UNIT
            ),
            "bToCLocalPosition": vector_list(cap.location),
            "bToCLocalRotationRadians": vector_list(cap.rotation_euler),
            "bToCLocalScale": vector_list(cap.scale),
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
