#!/usr/bin/env python3
"""Export a rendered B surface in an explicit registration coordinate frame.

Run with Blender.  This is intentionally a geometry extractor only: it does
not estimate a transform and it never changes the production asset.
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


def arguments() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("legacy-blend", "runtime-fbx"), required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--include-neck",
        action="store_true",
        help="Include ReferenceNeckProxyB. The default exports DamagedBottleB only.",
    )
    return parser.parse_args(argv)


def load_source(args: argparse.Namespace) -> list[tuple[bpy.types.Object, Matrix]]:
    if args.mode == "legacy-blend":
        # Blender file is already open because it is passed before --python.
        bodies = [
            obj for obj in bpy.data.objects
            if obj.type == "MESH" and obj.name.startswith("ReferenceBottleB_")
        ]
        if len(bodies) < 2:
            raise RuntimeError("legacy reference B body/neck surfaces are missing")
        # Historical renderer contract: Blender (x,y,z) -> ORB-style
        # canonical (x,z,-y).  This is the actual contract used when the
        # observed-point similarity was measured in commit 65d64d1.
        axis = Matrix(((1.0, 0.0, 0.0, 0.0),
                       (0.0, 0.0, 1.0, 0.0),
                       (0.0, -1.0, 0.0, 0.0),
                       (0.0, 0.0, 0.0, 1.0)))
        return [(body, axis @ body.matrix_world) for body in bodies]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(args.source))
    body = bpy.data.objects.get("DamagedBottleB")
    root = bpy.data.objects.get("BottleRepairRoot")
    if body is None or root is None:
        raise RuntimeError("runtime FBX must contain BottleRepairRoot/DamagedBottleB")
    # Export in BottleRepairRoot coordinates, not Blender scene/world or FBX
    # importer coordinates.
    objects = [body]
    neck = bpy.data.objects.get("ReferenceNeckProxyB")
    if args.include_neck and neck is not None:
        objects.append(neck)
    return [
        (obj, root.matrix_world.inverted() @ obj.matrix_world)
        for obj in objects
    ]


def write_binary_ply(path: Path, vertices: list[Vector], triangles: list[tuple[int, int, int]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    header = (
        "ply\nformat binary_little_endian 1.0\n"
        f"element vertex {len(vertices)}\n"
        "property float x\nproperty float y\nproperty float z\n"
        f"element face {len(triangles)}\n"
        "property list uchar int vertex_indices\nend_header\n"
    ).encode("ascii")
    with path.open("wb") as handle:
        handle.write(header)
        for point in vertices:
            handle.write(struct.pack("<3f", point.x, point.y, point.z))
        for triangle in triangles:
            handle.write(struct.pack("<B3i", 3, *triangle))


def main() -> None:
    args = arguments()
    sources = load_source(args)
    vertices: list[Vector] = []
    triangles: list[tuple[int, int, int]] = []
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for body, transform in sources:
        evaluated = body.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            mesh.calc_loop_triangles()
            offset = len(vertices)
            vertices.extend(transform @ vertex.co for vertex in mesh.vertices)
            triangles.extend(
                tuple(offset + index for index in loop.vertices)
                for loop in mesh.loop_triangles
            )
        finally:
            evaluated.to_mesh_clear()
    write_binary_ply(args.output, vertices, triangles)
    mins = [min(point[axis] for point in vertices) for axis in range(3)]
    maxs = [max(point[axis] for point in vertices) for axis in range(3)]
    print(
        "B_REGISTRATION_SURFACE_OK "
        f"mode={args.mode} objects={len(sources)} vertices={len(vertices)} "
        f"triangles={len(triangles)} min={mins} max={maxs} output={args.output}"
    )


if __name__ == "__main__":
    main()
