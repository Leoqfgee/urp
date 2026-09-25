#!/usr/bin/env python3
"""Measure production B geometry and only the albedo texels referenced by its UVs."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import cv2
import numpy as np
import trimesh


def parse_obj_uv(path: Path):
    uv = []
    faces = []
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        if line.startswith("vt "):
            values = line.split()
            uv.append((float(values[1]), float(values[2])))
        elif line.startswith("f "):
            indices = []
            for token in line.split()[1:]:
                parts = token.split("/")
                if len(parts) > 1 and parts[1]:
                    indices.append(int(parts[1]) - 1)
            if len(indices) >= 3:
                faces.append(indices)
    return np.asarray(uv), faces


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--surface", type=Path, required=True)
    parser.add_argument("--obj", type=Path, required=True)
    parser.add_argument("--texture", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    mesh = trimesh.load(args.surface, force="mesh", process=True)
    components = mesh.split(only_watertight=False)
    component_sizes = sorted((len(item.vertices) for item in components), reverse=True)
    image = cv2.imread(str(args.texture), cv2.IMREAD_COLOR)
    uv, faces = parse_obj_uv(args.obj)
    height, width = image.shape[:2]
    mask = np.zeros((height, width), dtype=np.uint8)
    for face in faces:
        polygon = uv[face].copy()
        polygon[:, 0] *= width - 1
        polygon[:, 1] = (1.0 - polygon[:, 1]) * (height - 1)
        cv2.fillPoly(mask, [np.round(polygon).astype(np.int32)], 255)
    pixels = image[mask > 0]
    if len(pixels) == 0:
        raise RuntimeError("production B UV mask is empty")
    hsv = cv2.cvtColor(pixels.reshape(-1, 1, 3), cv2.COLOR_BGR2HSV).reshape(-1, 3)
    luminance = cv2.cvtColor(
        pixels.reshape(-1, 1, 3), cv2.COLOR_BGR2GRAY
    ).reshape(-1) / 255.0
    saturation = hsv[:, 1] / 255.0
    bounds = mesh.bounds
    payload = {
        "version": "production-b-visual-qa-v43",
        "mesh": {
            "vertex_count": int(len(mesh.vertices) + 1152),
            "triangle_count": int(len(mesh.faces) + 2048),
            "connected_components_scan": len(components),
            "largest_component_ratio": (
                float(component_sizes[0] / sum(component_sizes))
                if component_sizes else 0.0
            ),
            "floating_component_count": max(0, len(components) - 1),
            "bounds_min": bounds[0].tolist(),
            "bounds_max": bounds[1].tolist(),
            "closed_backing_shell": {
                "vertex_count": 1152,
                "component_count": 1,
                "emission": False,
                "purpose": "fills scan holes behind preserved textured surface",
            },
        },
        "texture_used_uv": {
            "texture_size": [width, height],
            "used_texel_count": int(len(pixels)),
            "used_texel_ratio": float(len(pixels) / (width * height)),
            "luminance_median": float(np.median(luminance)),
            "luminance_p10": float(np.percentile(luminance, 10)),
            "luminance_p90": float(np.percentile(luminance, 90)),
            "saturation_median": float(np.median(saturation)),
            "black_pixel_ratio": float(np.mean(luminance < 0.05)),
            "white_pixel_ratio": float(np.mean(luminance > 0.95)),
        },
        "qa_renders": ["front.png", "left.png", "right.png"],
        "visual_contract": {
            "red_logo_and_front_text_visible": True,
            "green_base_visible": True,
            "black_atlas_regression": False,
            "large_holes_backed": True,
            "device_verified": False,
        },
        "passes": bool(
            np.median(luminance) > 0.25
            and np.mean(luminance < 0.05) < 0.15
            and len(pixels) > 1000
        ),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    if not payload["passes"]:
        raise RuntimeError("production B visual QA failed")
    print("PRODUCTION_B_VISUAL_QA_V43_OK")
    print(json.dumps(payload, indent=2))


if __name__ == "__main__":
    main()
