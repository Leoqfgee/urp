#!/usr/bin/env python3
"""Remove Meshroom background geometry and create viewer-ready bottle assets."""

from __future__ import annotations

import argparse
import json
import shutil
from pathlib import Path

import numpy as np
import trimesh
from PIL import Image
import OpenEXR
import Imath


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-obj", type=Path, required=True)
    parser.add_argument("--input-exr", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--bounds", type=float, nargs=6, required=True,
                        metavar=("MIN_X", "MIN_Y", "MIN_Z", "MAX_X", "MAX_Y", "MAX_Z"))
    parser.add_argument("--min-component-faces", type=int, default=18)
    return parser.parse_args()


def read_exr_srgb(path: Path) -> Image.Image:
    source = OpenEXR.InputFile(str(path))
    window = source.header()["dataWindow"]
    width = window.max.x - window.min.x + 1
    height = window.max.y - window.min.y + 1
    pixel_type = Imath.PixelType(Imath.PixelType.FLOAT)
    rgb = np.stack([
        np.frombuffer(source.channel(channel, pixel_type), dtype=np.float32)
        for channel in ("R", "G", "B")
    ], axis=1).reshape(height, width, 3)
    rgb = np.nan_to_num(rgb, nan=0.0, posinf=1.0, neginf=0.0)
    rgb = np.clip(rgb, 0.0, None)
    rgb = rgb / (1.0 + rgb)
    rgb = np.where(rgb <= 0.0031308, 12.92 * rgb, 1.055 * np.power(rgb, 1 / 2.4) - 0.055)
    return Image.fromarray(np.uint8(np.clip(rgb, 0.0, 1.0) * 255.0), "RGB")


def main() -> None:
    args = parse_args()
    scene = trimesh.load(args.input_obj, force="scene", process=False)
    mesh = trimesh.util.concatenate(tuple(scene.geometry.values()))
    original_components = mesh.split(only_watertight=False)
    minimum = np.asarray(args.bounds[:3], dtype=np.float64)
    maximum = np.asarray(args.bounds[3:], dtype=np.float64)

    kept = []
    for component in original_components:
        centroid = component.vertices.mean(axis=0)
        if (len(component.faces) >= args.min_component_faces
                and np.all(centroid >= minimum)
                and np.all(centroid <= maximum)):
            kept.append(component)
    if not kept:
        raise SystemExit("crop removed every connected component")

    cleaned = trimesh.util.concatenate(kept)
    cleaned.remove_unreferenced_vertices()
    cleaned.merge_vertices()
    cleaned.update_faces(cleaned.nondegenerate_faces())
    cleaned.update_faces(cleaned.unique_faces())
    cleaned.remove_unreferenced_vertices()
    cleaned.fix_normals()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    output_obj = args.output_dir / f"{args.name}.obj"
    output_png = args.output_dir / f"{args.name}_albedo.png"
    output_mtl = args.output_dir / f"{args.name}.mtl"
    cleaned.export(output_obj)
    read_exr_srgb(args.input_exr).resize((2048, 2048), Image.Resampling.LANCZOS).save(
        output_png, optimize=True
    )
    output_mtl.write_text(
        "newmtl bottle_processed\n"
        "Kd 1.0 1.0 1.0\n"
        "Ka 0.08 0.08 0.08\n"
        "Ks 0.04 0.04 0.04\n"
        "Ns 18\n"
        f"map_Kd {output_png.name}\n",
        encoding="ascii",
    )

    text = output_obj.read_text(encoding="utf-8", errors="replace")
    lines = text.splitlines()
    lines = [line for line in lines if not line.startswith(("mtllib ", "usemtl "))]
    output_obj.write_text(
        f"mtllib {output_mtl.name}\nusemtl bottle_processed\n" + "\n".join(lines) + "\n",
        encoding="utf-8",
    )
    report = {
        "input_vertices": int(len(mesh.vertices)),
        "input_faces": int(len(mesh.faces)),
        "input_components": int(len(original_components)),
        "kept_components": int(len(kept)),
        "output_vertices": int(len(cleaned.vertices)),
        "output_faces": int(len(cleaned.faces)),
        "output_bounds": cleaned.bounds.tolist(),
        "crop_bounds": [minimum.tolist(), maximum.tolist()],
    }
    (args.output_dir / f"{args.name}_cleaning_report.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
