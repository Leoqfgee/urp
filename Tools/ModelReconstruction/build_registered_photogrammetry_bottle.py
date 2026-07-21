#!/usr/bin/env python3
"""Build the registered bottle reference (b) and repair cap (c) in Blender.

Run with Blender, not the system Python.  The source is the exact extracted
photogrammetry bottle approved by the user.  Its malformed reconstructed cap
is removed; the bottle body is otherwise kept unchanged.  A measured open
mouth and a 39 mm x 10 mm cap are registered at one canonical mouth origin.

Canonical frame used by Unity and the ORB database:
  origin = mouth contact-plane centre
  +X     = bottle right
  +Y     = bottle axis up
  +Z     = front label direction
  scale  = 1 model unit = 0.17 metre
"""

from __future__ import annotations

import argparse
import json
import math
import shutil
import sys
from dataclasses import dataclass, field
from pathlib import Path

import bpy
from mathutils import Vector


MODEL_UNIT_METERS = 0.17
SOURCE_CAP_CUT_Z = 1.25
SOURCE_MOUTH_Z = 1.34
SOURCE_TO_CANONICAL_SCALE = 1.20 / SOURCE_MOUTH_Z
MOUTH_BLEND_BOTTOM_Y = (SOURCE_CAP_CUT_Z - SOURCE_MOUTH_Z) * SOURCE_TO_CANONICAL_SCALE
MOUTH_RADIUS_MODEL = 0.017 / MODEL_UNIT_METERS
MOUTH_INNER_RADIUS_MODEL = 0.0135 / MODEL_UNIT_METERS
CAP_RADIUS_MODEL = 0.0195 / MODEL_UNIT_METERS
CAP_HEIGHT_MODEL = 0.010 / MODEL_UNIT_METERS
TAU = math.pi * 2.0


@dataclass
class Face:
    vertices: tuple[int, ...]
    uvs: tuple[int, ...]
    material: str
    group: str


@dataclass
class ObjData:
    vertices: list[tuple[float, float, float]] = field(default_factory=list)
    uvs: list[tuple[float, float]] = field(default_factory=list)
    faces: list[Face] = field(default_factory=list)

    def add_vertex(self, xyz: tuple[float, float, float]) -> int:
        self.vertices.append(xyz)
        return len(self.vertices)

    def add_uv(self, uv: tuple[float, float]) -> int:
        self.uvs.append(uv)
        return len(self.uvs)

    def add_face(
        self,
        vertices: tuple[int, ...],
        uvs: tuple[int, ...],
        material: str,
        group: str,
    ) -> None:
        self.faces.append(Face(vertices, uvs, material, group))

    def append(self, other: "ObjData") -> None:
        vertex_offset = len(self.vertices)
        uv_offset = len(self.uvs)
        self.vertices.extend(other.vertices)
        self.uvs.extend(other.uvs)
        for face in other.faces:
            self.faces.append(Face(
                tuple(index + vertex_offset for index in face.vertices),
                tuple(index + uv_offset for index in face.uvs),
                face.material,
                face.group,
            ))


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-blend", type=Path, required=True)
    parser.add_argument("--source-texture", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--registration-output", type=Path, required=True)
    return parser.parse_args(argv)


def source_to_canonical(co: Vector) -> tuple[float, float, float]:
    # The approved extracted model's front label is viewed from source +Y.
    # Looking toward the origin from +Y, screen-right is source -X.
    return (
        -co.x * SOURCE_TO_CANONICAL_SCALE,
        (co.z - SOURCE_MOUTH_Z) * SOURCE_TO_CANONICAL_SCALE,
        co.y * SOURCE_TO_CANONICAL_SCALE,
    )


def extract_reference_mesh(source_object: bpy.types.Object) -> ObjData:
    mesh = source_object.data
    uv_layer = mesh.uv_layers.active.data if mesh.uv_layers.active else None
    kept_polygons = [
        polygon for polygon in mesh.polygons
        if all(mesh.vertices[index].co.z <= SOURCE_CAP_CUT_Z for index in polygon.vertices)
    ]
    used_vertices = sorted({index for polygon in kept_polygons for index in polygon.vertices})
    vertex_map: dict[int, int] = {}
    output = ObjData()
    for source_index in used_vertices:
        vertex_map[source_index] = output.add_vertex(source_to_canonical(mesh.vertices[source_index].co))

    for polygon in kept_polygons:
        face_vertices: list[int] = []
        face_uvs: list[int] = []
        for loop_index in polygon.loop_indices:
            loop = mesh.loops[loop_index]
            face_vertices.append(vertex_map[loop.vertex_index])
            uv = uv_layer[loop_index].uv if uv_layer else (0.5, 0.5)
            face_uvs.append(output.add_uv((float(uv[0]), float(uv[1]))))
        output.add_face(
            tuple(face_vertices),
            tuple(face_uvs),
            "PhotogrammetryBottle",
            "reference_bottle_b_original_body",
        )
    return output


def add_ring(
    data: ObjData,
    y: float,
    radius: float,
    segments: int,
    uv_v: float,
) -> tuple[list[int], list[int]]:
    vertices: list[int] = []
    uvs: list[int] = []
    for segment in range(segments):
        theta = TAU * segment / segments
        vertices.append(data.add_vertex((math.sin(theta) * radius, y, math.cos(theta) * radius)))
        uvs.append(data.add_uv((segment / segments, uv_v)))
    return vertices, uvs


def connect_rings(
    data: ObjData,
    lower: tuple[list[int], list[int]],
    upper: tuple[list[int], list[int]],
    material: str,
    group: str,
    inward: bool = False,
) -> None:
    count = len(lower[0])
    for index in range(count):
        nxt = (index + 1) % count
        a, b = lower[0][index], lower[0][nxt]
        c, d = upper[0][index], upper[0][nxt]
        ua, ub = lower[1][index], lower[1][nxt]
        uc, ud = upper[1][index], upper[1][nxt]
        if inward:
            data.add_face((a, b, c), (ua, ub, uc), material, group)
            data.add_face((b, d, c), (ub, ud, uc), material, group)
        else:
            data.add_face((a, c, b), (ua, uc, ub), material, group)
            data.add_face((b, c, d), (ub, uc, ud), material, group)


def build_registered_open_mouth(segments: int = 144) -> ObjData:
    data = ObjData()
    profile = [
        (MOUTH_BLEND_BOTTOM_Y, 0.104),
        (-0.072, 0.101),
        (-0.064, 0.108),
        (-0.056, 0.110),
        (-0.048, 0.102),
        (-0.039, 0.101),
        (-0.031, 0.108),
        (-0.023, 0.110),
        (-0.015, 0.102),
        (-0.007, 0.100),
        (0.000, MOUTH_RADIUS_MODEL),
    ]
    rings = [add_ring(data, y, radius, segments, 0.75 + 0.2 * i / (len(profile) - 1))
             for i, (y, radius) in enumerate(profile)]
    for lower, upper in zip(rings, rings[1:]):
        connect_rings(data, lower, upper, "BottleWhite", "registered_open_mouth")

    inner_top = add_ring(data, -0.002, MOUTH_INNER_RADIUS_MODEL, segments, 0.98)
    inner_bottom = add_ring(data, -0.058, MOUTH_INNER_RADIUS_MODEL, segments, 0.94)
    connect_rings(data, inner_bottom, inner_top, "BottleWhite", "registered_open_mouth_inner", inward=True)

    outer_top = rings[-1]
    for index in range(segments):
        nxt = (index + 1) % segments
        data.add_face(
            (outer_top[0][index], outer_top[0][nxt], inner_top[0][index]),
            (outer_top[1][index], outer_top[1][nxt], inner_top[1][index]),
            "BottleWhite", "registered_open_mouth_lip")
        data.add_face(
            (outer_top[0][nxt], inner_top[0][nxt], inner_top[0][index]),
            (outer_top[1][nxt], inner_top[1][nxt], inner_top[1][index]),
            "BottleWhite", "registered_open_mouth_lip")
    return data


def build_registered_cap(segments: int = 288) -> ObjData:
    data = ObjData()
    bottom_y = -0.050
    levels = [0.0, 0.006, 0.013, CAP_HEIGHT_MODEL - 0.010, CAP_HEIGHT_MODEL]
    rings: list[tuple[list[int], list[int]]] = []
    for level_index, local_y in enumerate(levels):
        vertices: list[int] = []
        uvs: list[int] = []
        for segment in range(segments):
            theta = TAU * segment / segments
            rib = 0.0018 * (0.5 + 0.5 * math.cos(72.0 * theta)) if 1 <= level_index <= 3 else 0.0005
            radius = CAP_RADIUS_MODEL - 0.0018 + rib
            vertices.append(data.add_vertex((
                math.sin(theta) * radius,
                bottom_y + local_y,
                math.cos(theta) * radius,
            )))
            uvs.append(data.add_uv((segment / segments, 0.80 + 0.18 * level_index / (len(levels) - 1))))
        rings.append((vertices, uvs))
    for lower, upper in zip(rings, rings[1:]):
        connect_rings(data, lower, upper, "BottleCapWhite", "registered_bottle_cap")

    centre = data.add_vertex((0.0, bottom_y + CAP_HEIGHT_MODEL, 0.0))
    centre_uv = data.add_uv((0.5, 0.5))
    top = rings[-1]
    for index in range(segments):
        nxt = (index + 1) % segments
        data.add_face(
            (centre, top[0][index], top[0][nxt]),
            (centre_uv, top[1][index], top[1][nxt]),
            "BottleCapWhite", "registered_bottle_cap_top")
    return data


def write_obj(data: ObjData, path: Path, object_name: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("# Registered photogrammetry bottle reference and measured repair geometry\n")
        handle.write("# Canonical frame: mouth centre origin; +Y bottle axis; 1 unit = 170 mm\n")
        handle.write("mtllib bottle_clean.mtl\n")
        handle.write(f"o {object_name}\n")
        for x, y, z in data.vertices:
            handle.write(f"v {x:.8f} {y:.8f} {z:.8f}\n")
        for u, v in data.uvs:
            handle.write(f"vt {u:.8f} {v:.8f}\n")
        last_material = None
        last_group = None
        for face in data.faces:
            if face.group != last_group:
                handle.write(f"g {face.group}\n")
                last_group = face.group
            if face.material != last_material:
                handle.write(f"usemtl {face.material}\n")
                last_material = face.material
            refs = [f"{vertex}/{uv}" for vertex, uv in zip(face.vertices, face.uvs)]
            handle.write("f " + " ".join(refs) + "\n")


def write_materials(path: Path) -> None:
    path.write_text(
        "newmtl PhotogrammetryBottle\n"
        "Ka 0.100000 0.100000 0.100000\n"
        "Kd 1.000000 1.000000 1.000000\n"
        "Ks 0.100000 0.100000 0.100000\n"
        "Ns 24.000000\n"
        "map_Kd bottle_atlas.png\n\n"
        "newmtl BottleWhite\n"
        "Ka 0.150000 0.150000 0.150000\n"
        "Kd 0.940000 0.940000 0.910000\n"
        "Ks 0.180000 0.180000 0.180000\n"
        "Ns 48.000000\n\n"
        "newmtl BottleCapWhite\n"
        "Ka 0.180000 0.180000 0.180000\n"
        "Kd 0.930000 0.930000 0.900000\n"
        "Ks 0.260000 0.260000 0.260000\n"
        "Ns 64.000000\n",
        encoding="utf-8",
        newline="\n",
    )


def canonical_to_blender(vertex: tuple[float, float, float]) -> tuple[float, float, float]:
    x, y, z = vertex
    return x, -z, y


def create_blender_object(name: str, data: ObjData, materials: dict[str, bpy.types.Material]) -> bpy.types.Object:
    vertices = [canonical_to_blender(vertex) for vertex in data.vertices]
    polygons = [tuple(index - 1 for index in face.vertices) for face in data.faces]
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(vertices, [], polygons)
    mesh.update()
    for material in materials.values():
        mesh.materials.append(material)
    material_indices = {name: index for index, name in enumerate(materials)}
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for polygon, face in zip(mesh.polygons, data.faces):
        polygon.material_index = material_indices[face.material]
        for loop_index, uv_index in zip(polygon.loop_indices, face.uvs):
            uv_layer.data[loop_index].uv = data.uvs[uv_index - 1]
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def make_material(name: str, color: tuple[float, float, float, float], texture: Path | None = None) -> bpy.types.Material:
    material = bpy.data.materials.new(name)
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = next(node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = 0.48
    if texture is not None:
        image_node = material.node_tree.nodes.new("ShaderNodeTexImage")
        image_node.image = bpy.data.images.load(str(texture), check_existing=True)
        material.node_tree.links.new(image_node.outputs["Color"], bsdf.inputs["Base Color"])
    return material


def point_camera(camera: bpy.types.Object, target: tuple[float, float, float]) -> None:
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def render_registration_scene(
    registration_output: Path,
    reference: ObjData,
    mouth: ObjData,
    cap: ObjData,
    texture: Path,
) -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in bpy.data.materials:
        bpy.data.materials.remove(block)

    materials = {
        "PhotogrammetryBottle": make_material("PhotogrammetryBottle", (1, 1, 1, 1), texture),
        "BottleWhite": make_material("BottleWhite", (0.94, 0.94, 0.91, 1)),
        "BottleCapWhite": make_material("BottleCapWhite", (0.93, 0.93, 0.90, 1)),
    }
    reference_object = create_blender_object("ReferenceBottleB_OriginalPhotogrammetry", reference, materials)
    mouth_object = create_blender_object("ReferenceBottleB_RegisteredOpenMouth", mouth, materials)
    cap_object = create_blender_object("RepairPartC_RegisteredBottleCap", cap, materials)

    reference_root = bpy.data.objects.new("ReferenceBottleB_HiddenAtRuntime", None)
    repair_root = bpy.data.objects.new("RepairPartRoot", None)
    bpy.context.collection.objects.link(reference_root)
    bpy.context.collection.objects.link(repair_root)
    reference_object.parent = reference_root
    mouth_object.parent = reference_root
    cap_object.parent = repair_root

    ground_material = make_material("Ground", (0.08, 0.08, 0.08, 1))
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, -1.205))
    ground = bpy.context.object
    ground.name = "PreviewGround"
    ground.data.materials.append(ground_material)

    bpy.ops.object.light_add(type="AREA", location=(2.0, -2.8, 2.5))
    bpy.context.object.data.energy = 950
    bpy.context.object.data.shape = "DISK"
    bpy.context.object.data.size = 2.5
    bpy.ops.object.light_add(type="AREA", location=(-2.0, -1.0, 0.3))
    bpy.context.object.data.energy = 500
    bpy.context.object.data.size = 2.0

    bpy.ops.object.camera_add(location=(0.0, -3.1, -0.50))
    camera = bpy.context.object
    camera.name = "RegistrationPreviewCamera"
    camera.data.lens = 58
    point_camera(camera, (0.0, 0.0, -0.57))
    bpy.context.scene.camera = camera

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1200
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.color = (0.035, 0.035, 0.035)

    registration_output.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(registration_output / "registered_b_plus_c_front.png")
    bpy.ops.render.render(write_still=True)
    camera.location = (1.7, -2.7, -0.45)
    point_camera(camera, (0.0, 0.0, -0.57))
    scene.render.filepath = str(registration_output / "registered_b_plus_c_oblique.png")
    bpy.ops.render.render(write_still=True)
    cap_object.hide_render = True
    camera.location = (0.0, -3.1, -0.50)
    point_camera(camera, (0.0, 0.0, -0.57))
    scene.render.filepath = str(registration_output / "reference_b_no_cap_front.png")
    bpy.ops.render.render(write_still=True)
    cap_object.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=str(registration_output / "b_c_registered_canonical.blend"))


def bounds(data: ObjData) -> dict[str, list[float]]:
    return {
        "min": [min(vertex[axis] for vertex in data.vertices) for axis in range(3)],
        "max": [max(vertex[axis] for vertex in data.vertices) for axis in range(3)],
    }


def main() -> None:
    args = parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    args.registration_output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(args.source_blend))
    source_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(source_objects) != 1:
        raise RuntimeError(f"Expected one source mesh, found {len(source_objects)}")

    reference = extract_reference_mesh(source_objects[0])
    mouth = build_registered_open_mouth()
    cap = build_registered_cap()
    damaged = ObjData()
    damaged.append(reference)
    damaged.append(mouth)
    complete = ObjData()
    complete.append(damaged)
    complete.append(cap)

    shutil.copy2(args.source_texture, args.output / "bottle_atlas.png")
    write_materials(args.output / "bottle_clean.mtl")
    write_obj(damaged, args.output / "bottle_damaged_clean.obj", "ReferenceBottleB_NoCap")
    write_obj(complete, args.output / "bottle_complete_clean.obj", "ReferenceBottleB_WithRegisteredCapC")
    write_obj(cap, args.output / "bottle_cap_clean.obj", "RepairPartC_RegisteredBottleCap")

    report = {
        "version": "coconut-photogrammetry-b-c-registration-v4",
        "source": {
            "blend": str(args.source_blend),
            "texture": str(args.source_texture),
            "body_policy": "preserve approved photogrammetry body; remove only triangles touching the reconstructed cap region",
            "source_cap_cut_z": SOURCE_CAP_CUT_Z,
            "source_mouth_z": SOURCE_MOUTH_Z,
            "source_to_canonical_uniform_scale": SOURCE_TO_CANONICAL_SCALE,
        },
        "coordinate_system": {
            "origin": "bottle mouth contact-plane centre",
            "x": "bottle right",
            "y": "bottle axis up",
            "z": "front label direction",
            "meters_per_model_unit": MODEL_UNIT_METERS,
        },
        "reference_b": {
            "purpose": "natural-feature reference for real bottle a; hidden in AR tracking mode",
            "source_faces_kept": len(reference.faces),
            "generated_mouth_faces": len(mouth.faces),
            "bounds_model": bounds(damaged),
        },
        "cap_c": {
            "outer_diameter_m": 0.039,
            "height_m": 0.010,
            "bottom_contact_y_model": -0.050,
            "axis_model": [0.0, 1.0, 0.0],
            "bounds_model": bounds(cap),
        },
        "registration": {
            "local_position": [0.0, 0.0, 0.0],
            "local_euler_degrees": [0.0, 0.0, 0.0],
            "local_scale": [1.0, 1.0, 1.0],
            "axis_angle_error_degrees": 0.0,
            "centre_error_m": 0.0,
            "size_ratio": 1.0,
            "method": "Blender canonical registration: b mouth centre and c axis share one measured frame",
            "device_overlay_verified": False,
        },
    }
    (args.output / "bottle_cap_registration_report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    render_registration_scene(args.registration_output, reference, mouth, cap, args.source_texture)
    print(json.dumps({
        "reference_faces": len(reference.faces),
        "mouth_faces": len(mouth.faces),
        "cap_faces": len(cap.faces),
        "damaged_bounds": bounds(damaged),
        "cap_bounds": bounds(cap),
        "output": str(args.output),
        "registration_output": str(args.registration_output),
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
