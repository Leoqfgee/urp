#!/usr/bin/env python3
"""Add a closed, non-emissive production backing shell behind scan holes."""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


def args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--fbx-output", type=Path, required=True)
    parser.add_argument("--blend-output", type=Path, required=True)
    parser.add_argument("--registration-proxy", type=Path, required=True)
    return parser.parse_args(argv)


def material(name: str, colour: tuple[float, float, float, float]):
    result = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    result.use_nodes = True
    result.diffuse_color = colour
    node = next(
        (candidate for candidate in result.node_tree.nodes
         if candidate.type == "BSDF_PRINCIPLED"),
        None,
    )
    if node is None:
        raise RuntimeError("Principled BSDF node was not created")
    node.inputs["Base Color"].default_value = colour
    node.inputs["Metallic"].default_value = 0.0
    node.inputs["Roughness"].default_value = 0.42
    emission = node.inputs.get("Emission Color") or node.inputs.get("Emission")
    if emission is not None:
        emission.default_value = (0.0, 0.0, 0.0, 1.0)
    strength = node.inputs.get("Emission Strength")
    if strength is not None:
        strength.default_value = 0.0
    return result


def main() -> None:
    options = args()
    root = bpy.data.objects["BottleRepairRoot"]
    body = bpy.data.objects["DamagedBottleB"]
    cap = bpy.data.objects["BottleCapC"]
    neck = bpy.data.objects["ReferenceNeckProxyB"]

    old_proxy = bpy.data.objects.get("BottleTrackingRegistrationProxy")
    if old_proxy is not None:
        bpy.data.objects.remove(old_proxy, do_unlink=True)
    bpy.ops.wm.ply_import(filepath=str(options.registration_proxy))
    proxy = bpy.context.active_object
    proxy.name = "BottleTrackingRegistrationProxy"
    proxy.parent = root
    proxy.matrix_parent_inverse = Matrix.Identity(4)
    proxy.location = Vector((0.0, 0.0, 0.0))
    proxy.rotation_euler = Vector((0.0, 0.0, 0.0))
    proxy.scale = Vector((1.0, 1.0, 1.0))
    proxy.hide_render = True
    proxy.hide_viewport = True

    old = bpy.data.objects.get("ProductionBCleanBackingShell")
    if old is not None:
        bpy.data.objects.remove(old, do_unlink=True)

    centre_x, centre_z = 0.0457595619, 0.0891441543
    # Each row is y, x-radius, z-radius. Radii stay well inside the real scan
    # silhouette and become visible only through reconstruction holes.
    profile = [
        (-1.3864, 0.165, 0.145),
        (-1.30, 0.178, 0.155),
        (-1.12, 0.182, 0.160),
        (-0.92, 0.178, 0.156),
        (-0.35, 0.170, 0.150),
        (-0.20, 0.158, 0.140),
        (-0.10, 0.125, 0.112),
        (-0.02, 0.090, 0.082),
        (0.0387, 0.082, 0.078),
    ]
    segments = 128
    vertices = []
    faces = []
    for y, radius_x, radius_z in profile:
        for segment in range(segments):
            angle = 2.0 * math.pi * segment / segments
            vertices.append((
                centre_x + radius_x * math.cos(angle),
                y,
                centre_z + radius_z * math.sin(angle),
            ))
    for row in range(len(profile) - 1):
        for segment in range(segments):
            nxt = (segment + 1) % segments
            a = row * segments + segment
            b = row * segments + nxt
            c = (row + 1) * segments + nxt
            d = (row + 1) * segments + segment
            faces.append((a, b, c, d))
    faces.append(tuple(reversed(tuple(range(segments)))))
    top_start = (len(profile) - 1) * segments
    faces.append(tuple(top_start + i for i in range(segments)))

    mesh = bpy.data.meshes.new("ProductionBCleanBackingShellMesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(material("ProductionBackingWhite", (0.78, 0.79, 0.74, 1.0)))
    mesh.materials.append(material("ProductionBackingGreen", (0.18, 0.46, 0.08, 1.0)))
    shell = bpy.data.objects.new("ProductionBCleanBackingShell", mesh)
    bpy.context.collection.objects.link(shell)
    shell.parent = body
    shell.matrix_parent_inverse = Matrix.Identity(4)
    shell.location = Vector((0.0, 0.0, 0.0))
    shell.rotation_euler = Vector((0.0, 0.0, 0.0))
    shell.scale = Vector((1.0, 1.0, 1.0))
    for polygon in mesh.polygons:
        y = sum(mesh.vertices[i].co.y for i in polygon.vertices) / len(polygon.vertices)
        polygon.material_index = 1 if y < -1.02 else 0
        polygon.use_smooth = True

    options.blend_output.parent.mkdir(parents=True, exist_ok=True)
    options.fbx_output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(options.blend_output))
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (root, body, neck, shell, proxy, cap):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=str(options.fbx_output), use_selection=True,
        object_types={"EMPTY", "MESH"}, apply_unit_scale=True,
        bake_space_transform=False, axis_forward="-Z", axis_up="Y",
        add_leaf_bones=False, bake_anim=False, path_mode="COPY",
        embed_textures=True,
    )
    print("PRODUCTION_B_BACKING_SHELL_V43_OK")
    print(json.dumps({
        "name": shell.name,
        "vertices": len(mesh.vertices),
        "polygons": len(mesh.polygons),
        "localMatrix": [float(v) for row in shell.matrix_local for v in row],
        "emission": False,
        "registrationProxySource": str(options.registration_proxy),
        "registrationProxyRendered": False,
    }))


if __name__ == "__main__":
    main()
