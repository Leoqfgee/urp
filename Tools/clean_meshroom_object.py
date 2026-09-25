#!/usr/bin/env python3
"""Clean a textured Meshroom OBJ using object-specific spatial and texture rules."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import trimesh
from PIL import Image


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    return parser.parse_args()


def component_record(index: int, component: trimesh.Trimesh, main_center: np.ndarray) -> dict:
    volume = float(abs(component.volume)) if component.is_volume else None
    return {
        "index": index,
        "vertices": int(len(component.vertices)),
        "faces": int(len(component.faces)),
        "area": float(component.area),
        "volume": volume,
        "centroid": component.centroid.tolist(),
        "bounds": component.bounds.tolist(),
        "distance_to_main_center": float(np.linalg.norm(component.centroid - main_center)),
        "watertight": bool(component.is_watertight),
    }


def sample_face_colors(mesh: trimesh.Trimesh, texture_path: Path) -> np.ndarray:
    if not hasattr(mesh.visual, "uv") or mesh.visual.uv is None:
        raise ValueError("mesh has no UV coordinates")
    image = np.asarray(Image.open(texture_path).convert("RGB"), dtype=np.float32) / 255.0
    uv = np.asarray(mesh.visual.uv)
    face_uv = uv[mesh.faces].mean(axis=1)
    x = np.clip((face_uv[:, 0] * (image.shape[1] - 1)).astype(int), 0, image.shape[1] - 1)
    y = np.clip(((1.0 - face_uv[:, 1]) * (image.shape[0] - 1)).astype(int), 0, image.shape[0] - 1)
    return image[y, x]


def accepted_color(colors: np.ndarray, rules: dict) -> np.ndarray:
    red, green, blue = colors[:, 0], colors[:, 1], colors[:, 2]
    masks = []
    if rules.get("blue", True):
        masks.append((blue >= 0.58) & (blue >= red * 1.30) & (blue >= green * 1.08))
    if rules.get("white", True):
        masks.append((colors.mean(axis=1) >= 0.68) & ((colors.max(axis=1) - colors.min(axis=1)) <= 0.25))
    if rules.get("pink", True):
        masks.append((red >= 0.48) & (red >= green * 1.18) & (blue >= green * 0.95))
    if not masks:
        return np.ones(len(colors), dtype=bool)
    return np.logical_or.reduce(masks)


def write_obj_with_material(mesh: trimesh.Trimesh, output_obj: Path, material_name: str,
                            texture_name: str) -> None:
    output_obj.parent.mkdir(parents=True, exist_ok=True)
    mesh.export(output_obj)
    mtl_path = output_obj.with_suffix(".mtl")
    mtl_path.write_text(
        f"newmtl {material_name}\n"
        "Ka 0.08 0.08 0.08\nKd 1.0 1.0 1.0\nKs 0.02 0.02 0.02\n"
        "Ns 12\nillum 2\n"
        f"map_Kd {texture_name}\n",
        encoding="ascii",
    )
    lines = output_obj.read_text(encoding="utf-8", errors="replace").splitlines()
    lines = [line for line in lines if not line.startswith(("mtllib ", "usemtl "))]
    output_obj.write_text(
        f"mtllib {mtl_path.name}\nusemtl {material_name}\n" + "\n".join(lines) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    args = parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8"))
    source_obj = Path(config["input_obj"])
    texture_path = Path(config["texture"])
    output_obj = Path(config["output_obj"])
    report_path = Path(config["report"])
    preview_dir = Path(config["preview_dir"])
    minimum = np.asarray(config["bounds_min"], dtype=np.float64)
    maximum = np.asarray(config["bounds_max"], dtype=np.float64)

    scene = trimesh.load(source_obj, force="scene", process=False)
    mesh = trimesh.util.concatenate(tuple(scene.geometry.values()))
    original_components = mesh.split(only_watertight=False)
    face_centers = mesh.triangles_center
    inside = np.all(face_centers >= minimum, axis=1) & np.all(face_centers <= maximum, axis=1)
    colors = sample_face_colors(mesh, texture_path)
    selected_faces = np.flatnonzero(inside & accepted_color(colors, config.get("colors", {})))
    if len(selected_faces) == 0:
        raise SystemExit("selection removed every face")

    cleaned = mesh.submesh([selected_faces], append=True, repair=False)
    cleaned.update_faces(cleaned.nondegenerate_faces())
    cleaned.update_faces(cleaned.unique_faces())
    cleaned.remove_unreferenced_vertices()
    cleaned.merge_vertices(merge_tex=False)
    cleaned.fix_normals()

    minimum_component_faces = int(config.get("minimum_component_faces", 3))
    selected_components = [
        component for component in cleaned.split(only_watertight=False)
        if len(component.faces) >= minimum_component_faces
    ]
    if selected_components:
        cleaned = trimesh.util.concatenate(selected_components)
        cleaned.remove_unreferenced_vertices()
        cleaned.fix_normals()

    output_obj.parent.mkdir(parents=True, exist_ok=True)
    texture_output = output_obj.parent / config.get("output_texture", "tissue_albedo.png")
    Image.open(texture_path).convert("RGB").save(texture_output, optimize=True)
    write_obj_with_material(cleaned, output_obj, config.get("material_name", "tissue_lit"),
                            texture_output.name)

    main_center = cleaned.bounds.mean(axis=0)
    output_components = cleaned.split(only_watertight=False)
    records = [
        component_record(index, component, main_center)
        for index, component in enumerate(output_components)
    ]
    records.sort(key=lambda item: item["faces"], reverse=True)
    report = {
        "source_obj": str(source_obj),
        "texture": str(texture_path),
        "selection_method": "configured bounds plus sampled texture color classes",
        "input_vertices": int(len(mesh.vertices)),
        "input_faces": int(len(mesh.faces)),
        "input_components": int(len(original_components)),
        "output_vertices": int(len(cleaned.vertices)),
        "output_faces": int(len(cleaned.faces)),
        "output_components": int(len(output_components)),
        "bounds_min": minimum.tolist(),
        "bounds_max": maximum.tolist(),
        "output_bounds": cleaned.bounds.tolist(),
        "components": records,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    preview_dir.mkdir(parents=True, exist_ok=True)
    import matplotlib.pyplot as plt
    centers = cleaned.triangles_center
    stride = max(1, len(centers) // 60000)
    views = {
        "front": (0, 2),
        "back": (0, 2),
        "left": (1, 2),
        "right": (1, 2),
        "top": (0, 1),
    }
    for name, axes in views.items():
        figure, plot = plt.subplots(figsize=(7, 7), facecolor="#f6f9fd")
        plot.scatter(centers[::stride, axes[0]], centers[::stride, axes[1]], s=1,
                     color="#246bb2")
        if name in {"back", "right"}:
            plot.invert_xaxis()
        plot.set_aspect("equal")
        plot.axis("off")
        figure.tight_layout(pad=0)
        figure.savefig(preview_dir / f"tissue_{name}.png", dpi=180,
                       facecolor=figure.get_facecolor())
        plt.close(figure)
    print(json.dumps({key: value for key, value in report.items() if key != "components"}, indent=2))


if __name__ == "__main__":
    main()
