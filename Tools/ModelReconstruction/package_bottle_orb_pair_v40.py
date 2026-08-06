#!/usr/bin/env python3
"""Bake one measured Sim(3) into the complete rigid B+C runtime pair.

BottleCapC is never calibrated independently.  Every mesh under
BottleRepairRoot (B body, B neck and C) receives the exact same matrix in root
coordinates; all object-local transforms remain identity.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


def arguments() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-fbx", type=Path, required=True)
    parser.add_argument("--registration-artifact", type=Path, required=True)
    parser.add_argument("--fbx-output", type=Path, required=True)
    parser.add_argument("--blend-output", type=Path, required=True)
    parser.add_argument("--report-output", type=Path, required=True)
    return parser.parse_args(argv)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def require(name: str) -> bpy.types.Object:
    result = bpy.data.objects.get(name)
    if result is None:
        raise RuntimeError(f"missing required runtime object {name}")
    return result


def matrix_from_row_major(values: list[float]) -> Matrix:
    if len(values) != 16:
        raise ValueError("T_ORB_FROM_B must contain 16 values")
    return Matrix(tuple(tuple(values[row * 4 + column] for column in range(4))
                        for row in range(4)))


def bounds(obj: bpy.types.Object) -> tuple[list[float], list[float]]:
    points = [vertex.co for vertex in obj.data.vertices]
    return (
        [float(min(point[axis] for point in points)) for axis in range(3)],
        [float(max(point[axis] for point in points)) for axis in range(3)],
    )


def identity_object(obj: bpy.types.Object) -> None:
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.location = Vector((0.0, 0.0, 0.0))
    obj.rotation_euler = Vector((0.0, 0.0, 0.0))
    obj.scale = Vector((1.0, 1.0, 1.0))


def main() -> None:
    args = arguments()
    artifact = json.loads(args.registration_artifact.read_text(encoding="utf-8"))
    transform = matrix_from_row_major(artifact["T_ORB_FROM_B"])

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(args.source_fbx))
    root = require("BottleRepairRoot")
    body = require("DamagedBottleB")
    neck = require("ReferenceNeckProxyB")
    cap = require("BottleCapC")
    if body.parent != root or cap.parent != root or neck.parent != body:
        raise RuntimeError("source FBX does not preserve BottleRepairRoot/B+C hierarchy")

    root_inverse = root.matrix_world.inverted()
    before_c_local = cap.matrix_local.copy()
    for obj in (body, neck, cap):
        root_from_mesh = root_inverse @ obj.matrix_world
        obj.data.transform(transform @ root_from_mesh)
    identity_object(root)
    identity_object(body)
    identity_object(neck)
    identity_object(cap)
    if cap.matrix_local != before_c_local or cap.matrix_local != Matrix.Identity(4):
        raise RuntimeError("BottleCapC local matrix changed while baking the rigid pair")

    args.fbx_output.parent.mkdir(parents=True, exist_ok=True)
    args.blend_output.parent.mkdir(parents=True, exist_ok=True)
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

    artifact["target_b_mesh_sha256"] = sha256(args.fbx_output)
    args.registration_artifact.write_text(
        json.dumps(artifact, indent=2) + "\n", encoding="utf-8"
    )
    body_min, body_max = bounds(body)
    neck_min, neck_max = bounds(neck)
    cap_min, cap_max = bounds(cap)
    identity = [float(value) for row in Matrix.Identity(4) for value in row]
    # Unity imports this Blender FBX with a measured root Rx(-90).  Keep the
    # source Sim(3) baked in the vertices and document only its exact inverse.
    unity_fbx_axis_inverse = [
        1.0, 0.0, 0.0, 0.0,
        0.0, 0.0, -1.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 0.0, 1.0,
    ]
    report = {
        "version": "bottle-orb-cross-reconstruction-rigid-pair-v40",
        "runtimeHierarchy": {
            "root": root.name,
            "referenceB": body.name,
            "referenceNeckB": neck.name,
            "repairC": cap.name,
        },
        "coordinateFrame": {
            "origin": "physical bottle mouth centre",
            "physicalMouthCentreModel": [0.0, 0.0, 0.0],
            "upAxis": "+Y base to mouth",
            "printedFrontAxis": "+Z red-logo/front side",
            "metersPerModelUnit": 0.17,
        },
        "referenceB": {
            "source": str(args.source_fbx),
            "sourceReconstruction": "F:\\Meshroom_work\\bottle_full_clean_v2",
            "boundsMin": body_min,
            "boundsMax": body_max,
            "localPosition": list(body.location),
            "localRotationRadians": list(body.rotation_euler),
            "localScale": list(body.scale),
        },
        "referenceNeckB": {
            "boundsMin": neck_min,
            "boundsMax": neck_max,
            "localPosition": list(neck.location),
            "localRotationRadians": list(neck.rotation_euler),
            "localScale": list(neck.scale),
        },
        "repairC": {
            "source": str(args.source_fbx),
            "geometryPolicy": (
                "same T_ORB_FROM_B baked into B, neck and C vertices; "
                "no independent C offset/rotation/scale"
            ),
            "boundsMin": cap_min,
            "boundsMax": cap_max,
            "localPosition": list(cap.location),
            "localRotationRadians": list(cap.rotation_euler),
            "localScale": list(cap.scale),
        },
        "registration": {
            "method": artifact["registration_method"],
            "sourceT_ORB_FROM_B": artifact["T_ORB_FROM_B"],
            "runtimeModelCoordinateAlignment": unity_fbx_axis_inverse,
            "registrationArtifact": str(args.registration_artifact),
        },
        "rigidContract": {
            "rootLocalMatrix": identity,
            "bLocalMatrix": identity,
            "neckLocalMatrix": identity,
            "cLocalMatrix": identity,
            "cIsNeverPositionedIndependentlyAtRuntime": True,
        },
        "sourceFbxSha256": sha256(args.source_fbx),
        "runtimeFbxSha256": artifact["target_b_mesh_sha256"],
        "rigidRelationshipPreserved": True,
        "deviceOverlayVerified": False,
    }
    args.report_output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("BOTTLE_ORB_PAIR_CROSS_REGISTRATION_V40_OK")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
