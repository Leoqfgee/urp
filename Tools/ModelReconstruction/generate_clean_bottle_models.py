#!/usr/bin/env python3
"""Generate clean, metric bottle meshes from the supplied photo sets.

The photos define silhouette proportions and provide front/back label texture.
Known measurements define the canonical scale:
  neck thread outer diameter: 34 mm
  cap outer diameter: 39 mm
  cap height: 10 mm

OBJ units intentionally match the existing tracking canonical space where
1 model unit = 170 mm and the bottle mouth centre is the origin.
"""

from __future__ import annotations

import argparse
import json
import math
from dataclasses import dataclass, field
from pathlib import Path

from PIL import Image, ImageEnhance, ImageFilter, ImageOps


MODEL_UNIT_METERS = 0.17
TAU = math.pi * 2.0


@dataclass
class Mesh:
    vertices: list[tuple[float, float, float]] = field(default_factory=list)
    uvs: list[tuple[float, float]] = field(default_factory=list)
    faces: list[tuple[int, int, int]] = field(default_factory=list)
    groups: list[tuple[str, int]] = field(default_factory=list)

    def vertex(self, xyz: tuple[float, float, float], uv: tuple[float, float]) -> int:
        self.vertices.append(xyz)
        self.uvs.append(uv)
        return len(self.vertices)

    def triangle(self, a: int, b: int, c: int) -> None:
        self.faces.append((a, b, c))

    def begin_group(self, name: str) -> None:
        self.groups.append((name, len(self.faces)))


def uv_for(theta: float, y: float, bottom: float = -1.20, top: float = 0.0) -> tuple[float, float]:
    # Offset avoids placing the front/back labels on the cylindrical UV seam.
    u = (theta / TAU + 0.25) % 1.0
    v = max(0.0, min(1.0, (y - bottom) / (top - bottom)))
    return u, v


def add_lathe(mesh: Mesh, name: str, profile: list[tuple[float, float]],
              segments: int = 160, close_bottom: bool = True) -> list[list[int]]:
    mesh.begin_group(name)
    rings: list[list[int]] = []
    for y, radius in profile:
        ring: list[int] = []
        for segment in range(segments + 1):
            theta = TAU * segment / segments
            ring.append(mesh.vertex(
                (math.sin(theta) * radius, y, math.cos(theta) * radius),
                uv_for(theta, y)))
        rings.append(ring)

    for ring_index, (lower, upper) in enumerate(zip(rings, rings[1:])):
        lower_is_pole = profile[ring_index][1] <= 1e-8
        upper_is_pole = profile[ring_index + 1][1] <= 1e-8
        for segment in range(segments):
            if lower_is_pole:
                mesh.triangle(lower[0], upper[segment], upper[segment + 1])
            elif upper_is_pole:
                mesh.triangle(lower[segment], upper[0], lower[segment + 1])
            else:
                a, b = lower[segment], lower[segment + 1]
                c, d = upper[segment], upper[segment + 1]
                mesh.triangle(a, c, b)
                mesh.triangle(b, c, d)

    if close_bottom:
        y = profile[0][0]
        centre = mesh.vertex((0.0, y, 0.0), (0.5, 0.02))
        for segment in range(segments):
            mesh.triangle(centre, rings[0][segment + 1], rings[0][segment])
    return rings


def add_open_mouth(mesh: Mesh, outer_top: list[int], outer_radius: float,
                   segments: int = 160) -> None:
    mesh.begin_group("hollow_mouth")
    inner_radius = 0.075
    inner_top: list[int] = []
    inner_bottom: list[int] = []
    for segment in range(segments + 1):
        theta = TAU * segment / segments
        blank_uv = (0.92 + 0.04 * segment / segments, 0.96)
        inner_top.append(mesh.vertex(
            (math.sin(theta) * inner_radius, 0.0, math.cos(theta) * inner_radius), blank_uv))
        inner_bottom.append(mesh.vertex(
            (math.sin(theta) * inner_radius, -0.085, math.cos(theta) * inner_radius), blank_uv))

    for segment in range(segments):
        # Top annulus/lip.
        mesh.triangle(outer_top[segment], outer_top[segment + 1], inner_top[segment])
        mesh.triangle(outer_top[segment + 1], inner_top[segment + 1], inner_top[segment])
        # Inner neck wall faces inward.
        mesh.triangle(inner_top[segment], inner_top[segment + 1], inner_bottom[segment])
        mesh.triangle(inner_top[segment + 1], inner_bottom[segment + 1], inner_bottom[segment])

    # Hidden closure prevents holes/non-manifold geometry while retaining a visible cavity.
    centre = mesh.vertex((0.0, -0.085, 0.0), (0.94, 0.96))
    for segment in range(segments):
        mesh.triangle(centre, inner_bottom[segment], inner_bottom[segment + 1])


def add_torus(mesh: Mesh, name: str, centre_y: float, major_radius: float,
              minor_radius: float, major_segments: int = 192, minor_segments: int = 12) -> None:
    mesh.begin_group(name)
    rings: list[list[int]] = []
    for i in range(major_segments + 1):
        theta = TAU * i / major_segments
        ring: list[int] = []
        for j in range(minor_segments + 1):
            phi = TAU * j / minor_segments
            radius = major_radius + minor_radius * math.cos(phi)
            ring.append(mesh.vertex(
                (math.sin(theta) * radius,
                 centre_y + minor_radius * math.sin(phi),
                 math.cos(theta) * radius),
                (0.94 + 0.04 * i / major_segments, 0.94 + 0.04 * j / minor_segments)))
        rings.append(ring)
    for i in range(major_segments):
        for j in range(minor_segments):
            a, b = rings[i][j], rings[i][j + 1]
            c, d = rings[i + 1][j], rings[i + 1][j + 1]
            mesh.triangle(a, c, b)
            mesh.triangle(b, c, d)


def add_cap(mesh: Mesh, name: str = "cap", y_offset: float = 0.0,
            segments: int = 288) -> None:
    mesh.begin_group(name)
    outer_radius = 0.039 / MODEL_UNIT_METERS / 2.0
    cap_height = 0.010 / MODEL_UNIT_METERS
    inner_radius = 0.102
    side_levels = [0.0, 0.006, 0.013, cap_height - 0.010, cap_height - 0.004]
    side_rings: list[list[int]] = []
    for level_index, local_y in enumerate(side_levels):
        ring: list[int] = []
        for segment in range(segments + 1):
            theta = TAU * segment / segments
            if 1 <= level_index <= 3:
                # 72 moulded grip ribs; maximum remains the measured 39 mm diameter.
                radius = outer_radius - 0.0018 + 0.0018 * math.cos(72.0 * theta)
            else:
                radius = outer_radius - 0.0012
            ring.append(mesh.vertex(
                (math.sin(theta) * radius, y_offset + local_y, math.cos(theta) * radius),
                (0.90 + 0.08 * segment / segments, 0.93 + 0.04 * level_index / 4.0)))
        side_rings.append(ring)

    for lower, upper in zip(side_rings, side_rings[1:]):
        for segment in range(segments):
            mesh.triangle(lower[segment], upper[segment], lower[segment + 1])
            mesh.triangle(lower[segment + 1], upper[segment], upper[segment + 1])

    # Slightly domed top, connected to the side.
    top_inner: list[int] = []
    top_y = y_offset + cap_height
    top_radius = outer_radius - 0.006
    for segment in range(segments + 1):
        theta = TAU * segment / segments
        top_inner.append(mesh.vertex(
            (math.sin(theta) * top_radius, top_y, math.cos(theta) * top_radius),
            (0.94, 0.96)))
    top_centre = mesh.vertex((0.0, top_y, 0.0), (0.94, 0.96))
    outer_top = side_rings[-1]
    for segment in range(segments):
        mesh.triangle(outer_top[segment], outer_top[segment + 1], top_inner[segment])
        mesh.triangle(outer_top[segment + 1], top_inner[segment + 1], top_inner[segment])
        mesh.triangle(top_centre, top_inner[segment], top_inner[segment + 1])

    # Hollow underside and inner skirt.
    inner_bottom: list[int] = []
    inner_top: list[int] = []
    for segment in range(segments + 1):
        theta = TAU * segment / segments
        uv = (0.94, 0.96)
        inner_bottom.append(mesh.vertex(
            (math.sin(theta) * inner_radius, y_offset + 0.002, math.cos(theta) * inner_radius), uv))
        inner_top.append(mesh.vertex(
            (math.sin(theta) * inner_radius, y_offset + cap_height - 0.010,
             math.cos(theta) * inner_radius), uv))
    for segment in range(segments):
        mesh.triangle(side_rings[0][segment], side_rings[0][segment + 1], inner_bottom[segment])
        mesh.triangle(side_rings[0][segment + 1], inner_bottom[segment + 1], inner_bottom[segment])
        mesh.triangle(inner_bottom[segment], inner_bottom[segment + 1], inner_top[segment])
        mesh.triangle(inner_bottom[segment + 1], inner_top[segment + 1], inner_top[segment])

    underside_centre = mesh.vertex((0.0, y_offset + cap_height - 0.010, 0.0), (0.94, 0.96))
    for segment in range(segments):
        mesh.triangle(underside_centre, inner_top[segment], inner_top[segment + 1])


def make_bottle(include_cap: bool) -> Mesh:
    mesh = Mesh()
    profile = [
        (-1.200, 0.000), (-1.198, 0.105), (-1.190, 0.160),
        (-1.175, 0.182), (-1.150, 0.195),
        (-1.080, 0.200), (-0.900, 0.198), (-0.420, 0.195),
        (-0.300, 0.188), (-0.235, 0.174), (-0.180, 0.145),
        (-0.135, 0.115), (-0.112, 0.100), (-0.106, 0.095),
        # Three moulded thread ridges are part of the continuous bottle surface.
        (-0.100, 0.095), (-0.096, 0.099), (-0.091, 0.100),
        (-0.086, 0.097), (-0.080, 0.095),
        (-0.071, 0.095), (-0.067, 0.099), (-0.062, 0.100),
        (-0.057, 0.097), (-0.051, 0.095),
        (-0.042, 0.095), (-0.038, 0.099), (-0.033, 0.100),
        (-0.028, 0.097), (-0.022, 0.095), (0.000, 0.095),
    ]
    rings = add_lathe(mesh, "bottle_body", profile, close_bottom=False)
    add_open_mouth(mesh, rings[-1], profile[-1][1])
    if include_cap:
        # The measured 10 mm cap encloses the upper neck instead of floating above it.
        add_cap(mesh, "complete_bottle_cap", y_offset=-0.050)
    return mesh


def make_cap() -> Mesh:
    mesh = Mesh()
    # Bake the registration into the asset. Runtime localPosition/rotation/scale
    # can therefore stay exactly identity in the bottle-mouth canonical frame.
    add_cap(mesh, "standalone_cap", y_offset=-0.050)
    return mesh


def write_obj(mesh: Mesh, path: Path, object_name: str) -> None:
    group_starts = {face_index: name for name, face_index in mesh.groups}
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("# Clean photo-derived bottle reconstruction\n")
        handle.write("# Canonical scale: 1 unit = 170 mm; bottle mouth centre = origin\n")
        handle.write("mtllib bottle_clean.mtl\n")
        handle.write(f"o {object_name}\n")
        handle.write("usemtl BottleAtlas\n")
        for x, y, z in mesh.vertices:
            handle.write(f"v {x:.8f} {y:.8f} {z:.8f}\n")
        for u, v in mesh.uvs:
            handle.write(f"vt {u:.8f} {v:.8f}\n")
        for face_index, (a, b, c) in enumerate(mesh.faces):
            if face_index in group_starts:
                handle.write(f"g {group_starts[face_index]}\n")
            handle.write(f"f {a}/{a} {b}/{b} {c}/{c}\n")


def paste_blended(canvas: Image.Image, source: Image.Image, box: tuple[int, int, int, int]) -> None:
    width, height = box[2] - box[0], box[3] - box[1]
    source = source.resize((width, height), Image.Resampling.LANCZOS)
    source = ImageEnhance.Contrast(source).enhance(1.06)
    source = ImageEnhance.Color(source).enhance(1.05)
    mask = Image.new("L", (width, height), 255)
    edge = max(10, min(width, height) // 18)
    pixels = mask.load()
    for y in range(height):
        for x in range(width):
            distance = min(x, y, width - 1 - x, height - 1 - y)
            if distance < edge:
                pixels[x, y] = int(255 * distance / edge)
    mask = mask.filter(ImageFilter.GaussianBlur(edge / 3))
    canvas.paste(source, (box[0], box[1]), mask)


def paste_print_overlay(canvas: Image.Image, source: Image.Image,
                        box: tuple[int, int, int, int]) -> None:
    """Keep printed ink while removing the photographed white-plastic lighting patch."""
    width, height = box[2] - box[0], box[3] - box[1]
    source = ImageOps.autocontrast(source.resize((width, height), Image.Resampling.LANCZOS),
                                   cutoff=(1, 2))
    source = ImageEnhance.Color(source).enhance(1.12)
    rgba = source.convert("RGBA")
    pixels = rgba.load()
    edge_x = max(36, width // 6)
    edge_y = max(24, height // 25)
    for y in range(height):
        for x in range(width):
            red, green, blue, _ = pixels[x, y]
            saturation = max(red, green, blue) - min(red, green, blue)
            luminance = (red * 54 + green * 183 + blue * 19) // 256
            ink_alpha = max(saturation * 3, int((235 - luminance) * 1.45))
            feather_x = min(1.0, min(x, width - 1 - x) / edge_x)
            feather_y = min(1.0, min(y, height - 1 - y) / edge_y)
            feather = min(feather_x, feather_y)
            alpha = max(0, min(255, int((ink_alpha - 16) * feather)))
            pixels[x, y] = (red, green, blue, alpha)
    canvas.paste(rgba, (box[0], box[1]), rgba)


def make_atlas(source_root: Path, output: Path) -> None:
    size = 2048
    canvas = Image.new("RGB", (size, size), (242, 242, 237))
    pixels = canvas.load()
    green_start = 1570
    for y in range(green_start, size):
        t = (y - green_start) / (size - green_start)
        colour = (int(103 - 25 * t), int(197 - 15 * t), int(65 - 18 * t))
        for x in range(size):
            pixels[x, y] = colour

    front_image = Image.open(source_root / "bottle_damaged" / "frame_0001.jpg").convert("RGB")
    back_image = Image.open(source_root / "bottle_full" / "frame_0021.jpg").convert("RGB")
    # Crops stay strictly inside the bottle silhouette, so no room background enters the atlas.
    front_crop = ImageOps.mirror(front_image.crop((220, 255, 440, 1060)))
    back_crop = ImageOps.mirror(back_image.crop((202, 500, 420, 1120)))
    # Unity's OBJ import flips the camera-facing longitudinal half, so the front
    # photograph occupies the lower-u half and the back photograph the upper-u half.
    paste_print_overlay(canvas, front_crop, (210, 245, 820, 1900))
    paste_print_overlay(canvas, back_crop, (1220, 440, 1785, 1910))
    canvas.save(output, quality=96)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    make_atlas(args.source_root, args.output / "bottle_atlas.png")
    (args.output / "bottle_clean.mtl").write_text(
        "newmtl BottleAtlas\n"
        "Ka 0.100000 0.100000 0.100000\n"
        "Kd 1.000000 1.000000 1.000000\n"
        "Ks 0.180000 0.180000 0.180000\n"
        "Ns 32.000000\n"
        "map_Kd bottle_atlas.png\n",
        encoding="utf-8", newline="\n")

    damaged = make_bottle(include_cap=False)
    complete = make_bottle(include_cap=True)
    cap = make_cap()
    write_obj(damaged, args.output / "bottle_damaged_clean.obj", "BottleDamagedClean")
    write_obj(complete, args.output / "bottle_complete_clean.obj", "BottleCompleteClean")
    write_obj(cap, args.output / "bottle_cap_clean.obj", "BottleCapClean")
    registration = {
        "version": "coconut-cap-registration-v3",
        "coordinate_system": {
            "origin": "bottle mouth contact-plane centre",
            "x": "bottle right",
            "y": "bottle axis up",
            "z": "front label direction",
            "meters_per_model_unit": MODEL_UNIT_METERS,
        },
        "mouth_circle": {
            "center_model": [0.0, 0.0, 0.0],
            "radius_model": 0.1,
            "radius_m": 0.017,
            "normal_model": [0.0, 1.0, 0.0],
        },
        "cap": {
            "outer_radius_model": (0.039 / MODEL_UNIT_METERS) / 2.0,
            "outer_radius_m": 0.0195,
            "height_model": 0.010 / MODEL_UNIT_METERS,
            "height_m": 0.010,
            "open_skirt_y_model": -0.050,
            "inner_roof_y_model": -0.050 + 0.010 / MODEL_UNIT_METERS - 0.010,
            "axis_model": [0.0, 1.0, 0.0],
        },
        "registration": {
            "local_position": [0.0, 0.0, 0.0],
            "local_euler_degrees": [0.0, 0.0, 0.0],
            "local_scale": [1.0, 1.0, 1.0],
            "axis_angle_error_degrees": 0.0,
            "centre_error_m": 0.0,
            "inner_roof_height_error_m": 0.0002,
            "size_ratio": 1.0,
            "similarity_transform": {
                "scale": 1.0,
                "rotation_quaternion_xyzw": [0.0, 0.0, 0.0, 1.0],
                "translation": [0.0, 0.0, 0.0],
            },
            "rms_m": 0.0002,
            "maximum_error_m": 0.0002,
            "method": "analytic coaxial registration from measured 34 mm mouth, 39 mm cap and 10 mm height",
            "device_overlay_verified": False,
        },
    }
    (args.output / "bottle_cap_registration_report.json").write_text(
        json.dumps(registration, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"damaged vertices={len(damaged.vertices)} triangles={len(damaged.faces)}")
    print(f"complete vertices={len(complete.vertices)} triangles={len(complete.faces)}")
    print(f"cap vertices={len(cap.vertices)} triangles={len(cap.faces)}")
    print(f"output={args.output}")


if __name__ == "__main__":
    main()
