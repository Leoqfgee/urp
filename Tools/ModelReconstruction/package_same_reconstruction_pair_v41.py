#!/usr/bin/env python3
"""Replace B with the same-reconstruction surface while preserving rigid C.

BottleCapC geometry and its local matrix are byte-for-byte/hash checked before
and after the operation.  The new B is baked into the measured ORB canonical
frame, while ReferenceNeckProxyB becomes an empty compatibility child because
the same-reconstruction B already contains the real reconstructed neck.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


def arguments() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-blend", type=Path, required=True)
    parser.add_argument("--same-reconstruction-obj", type=Path, required=True)
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


def matrix(values: list[float]) -> Matrix:
    return Matrix(tuple(tuple(values[row * 4 + column] for column in range(4))
                        for row in range(4)))


def mesh_digest(obj: bpy.types.Object) -> str:
    digest = hashlib.sha256()
    for vertex in obj.data.vertices:
        digest.update(struct.pack("<3f", *vertex.co))
    for polygon in obj.data.polygons:
        digest.update(struct.pack("<I", len(polygon.vertices)))
        for index in polygon.vertices:
            digest.update(struct.pack("<I", index))
    return digest.hexdigest().upper()


def matrix_values(value: Matrix) -> list[float]:
    return [float(item) for row in value for item in row]


def bounds(obj: bpy.types.Object) -> tuple[list[float], list[float]]:
    points = [vertex.co for vertex in obj.data.vertices]
    return (
        [float(min(point[axis] for point in points)) for axis in range(3)],
        [float(max(point[axis] for point in points)) for axis in range(3)],
    )


def identity(obj: bpy.types.Object) -> None:
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.location = Vector((0.0, 0.0, 0.0))
    obj.rotation_euler = Vector((0.0, 0.0, 0.0))
    obj.scale = Vector((1.0, 1.0, 1.0))


def main() -> None:
    args = arguments()
    artifact = json.loads(args.registration_artifact.read_text(encoding="utf-8"))
    transform = matrix(artifact["T_ORB_FROM_B"])
    bpy.ops.wm.open_mainfile(filepath=str(args.source_blend))
    root = bpy.data.objects.get("BottleRepairRoot")
    old_body = bpy.data.objects.get("DamagedBottleB")
    old_neck = bpy.data.objects.get("ReferenceNeckProxyB")
    cap = bpy.data.objects.get("BottleCapC")
    if root is None or old_body is None or old_neck is None or cap is None:
        raise RuntimeError("v40 rigid pair hierarchy is incomplete")
    cap_local_before = cap.matrix_local.copy()
    cap_mesh_before = mesh_digest(cap)

    bpy.data.objects.remove(old_neck, do_unlink=True)
    bpy.data.objects.remove(old_body, do_unlink=True)
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=str(args.same_reconstruction_obj))
    imported = [obj for obj in bpy.data.objects if obj not in before and obj.type == "MESH"]
    if len(imported) != 1:
        raise RuntimeError(f"expected one imported same-reconstruction B, got {len(imported)}")
    body = imported[0]
    body.name = "DamagedBottleB"
    body.data.name = "DamagedBottleB_Mesh_v41"
    # OBJ vertex coordinates are still the exact AliceVision file coordinates;
    # Blender stores its import-axis conversion on the object matrix. The
    # measured T_ORB_FROM_B applies to those file coordinates, so bake T
    # directly into vertices and discard (do not compose) the importer matrix.
    body.data.transform(transform)
    body.parent = root
    identity(body)

    neck_mesh = bpy.data.meshes.new("ReferenceNeckProxyB_CompatibilityEmpty_v41")
    neck = bpy.data.objects.new("ReferenceNeckProxyB", neck_mesh)
    bpy.context.collection.objects.link(neck)
    neck.parent = body
    identity(neck)

    identity(root)
    identity(cap)
    if cap.matrix_local != cap_local_before or cap.matrix_local != Matrix.Identity(4):
        raise RuntimeError("BottleCapC local matrix changed")
    if mesh_digest(cap) != cap_mesh_before:
        raise RuntimeError("BottleCapC geometry changed")

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
    cap_min, cap_max = bounds(cap)
    identity_values = matrix_values(Matrix.Identity(4))
    report = {
        "version": "bottle-orb-same-reconstruction-rigid-pair-v41",
        "runtimeHierarchy": {
            "root": root.name,
            "referenceB": body.name,
            "referenceNeckCompatibilityNode": neck.name,
            "repairC": cap.name,
        },
        "coordinateFrame": {
            "origin": "independently measured physical bottle mouth-ring centroid",
            "upAxis": "+Y measured base-to-mouth center-line",
            "printedFrontAxis": "+Z red-logo semantic direction",
            "metersPerModelUnit": 0.17,
        },
        "referenceB": {
            "source": str(args.same_reconstruction_obj),
            "sourceReconstruction": "F:\\Meshroom_work\\bottle_damaged",
            "boundsMin": body_min,
            "boundsMax": body_max,
            "containsReconstructedPhysicalNeck": True,
        },
        "referenceNeckB": {
            "emptyCompatibilityNode": True,
            "reason": "physical neck is part of the same-reconstruction DamagedBottleB mesh",
        },
        "repairC": {
            "geometrySha256Before": cap_mesh_before,
            "geometrySha256After": mesh_digest(cap),
            "boundsMin": cap_min,
            "boundsMax": cap_max,
            "localMatrixBefore": matrix_values(cap_local_before),
            "localMatrixAfter": matrix_values(cap.matrix_local),
            "geometryPolicy": "C geometry/local transform unchanged; shared canonical mouth frame only",
        },
        "registration": {
            "method": artifact["registration_method"],
            "sourceT_ORB_FROM_B": artifact["T_ORB_FROM_B"],
            "runtimeModelCoordinateAlignment": [
                1.0, 0.0, 0.0, 0.0,
                0.0, 0.0, -1.0, 0.0,
                0.0, 1.0, 0.0, 0.0,
                0.0, 0.0, 0.0, 1.0,
            ],
        },
        "rigidContract": {
            "rootLocalMatrix": identity_values,
            "bLocalMatrix": identity_values,
            "neckLocalMatrix": identity_values,
            "cLocalMatrix": identity_values,
            "cIsNeverPositionedIndependentlyAtRuntime": True,
        },
        "runtimeFbxSha256": artifact["target_b_mesh_sha256"],
        "rigidRelationshipPreserved": True,
        "deviceOverlayVerified": False,
    }
    args.report_output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("BOTTLE_ORB_PAIR_SAME_RECONSTRUCTION_V41_OK")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
