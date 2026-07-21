#!/usr/bin/env python3
"""Generate the runtime ORB 3D database from registered model b, never cap c.

Pipeline:
  Blender b-only renders -> ORB descriptors -> Blender surface ray casts ->
  canonical descriptor/XYZ database consumed by solvePnP at runtime.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import struct
import subprocess
from pathlib import Path

import cv2
import numpy as np


MAGIC = b"URP3DM1\0"
VERSION = "coconut-reference-b-rendered-v1"


def parse_args() -> argparse.Namespace:
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--blender",
        type=Path,
        default=Path(r"F:\Program Files\Blender 4.5 LTS\blender.exe"),
    )
    parser.add_argument(
        "--blend",
        type=Path,
        default=Path(r"F:\Au\暑期任务\bottle0720\processed_20260721\b_c_registration\b_c_registered_canonical.blend"),
    )
    parser.add_argument(
        "--work",
        type=Path,
        default=root / "Tools" / "TrackingReferenceB",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=root / "Tools" / "TrackingReferenceB" / "bottle_reference_b_synthetic_experimental.bytes",
    )
    parser.add_argument("--max-records", type=int, default=8000)
    parser.add_argument("--rerender", action="store_true")
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def run_blender(blender: Path, blend: Path, script: Path, arguments: list[str]) -> None:
    command = [
        str(blender), "--background", str(blend), "--python", str(script), "--", *arguments
    ]
    completed = subprocess.run(command, check=False)
    if completed.returncode != 0:
        raise RuntimeError(f"Blender failed with exit code {completed.returncode}: {command}")


def detect_features(work: Path) -> tuple[dict, dict[str, tuple[list[cv2.KeyPoint], np.ndarray]]]:
    metadata = json.loads((work / "views.json").read_text(encoding="utf-8"))
    width, height = metadata["resolution"]
    orb = cv2.ORB_create(
        nfeatures=2600,
        scaleFactor=1.2,
        nlevels=8,
        edgeThreshold=31,
        patchSize=31,
        fastThreshold=12,
    )
    view_payloads = []
    extracted: dict[str, tuple[list[cv2.KeyPoint], np.ndarray]] = {}
    for view in metadata["views"]:
        image = cv2.imread(str(work / view["image"]), cv2.IMREAD_UNCHANGED)
        if image is None or image.shape[2] != 4:
            raise RuntimeError(f"Expected RGBA Blender render: {work / view['image']}")
        gray = cv2.cvtColor(image[:, :, :3], cv2.COLOR_BGR2GRAY)
        mask = np.where(image[:, :, 3] >= 240, 255, 0).astype(np.uint8)
        mask = cv2.erode(mask, np.ones((11, 11), np.uint8), iterations=1)
        keypoints, descriptors = orb.detectAndCompute(gray, mask)
        if descriptors is None:
            keypoints, descriptors = [], np.empty((0, 32), np.uint8)
        extracted[view["id"]] = (keypoints, descriptors)
        view_payloads.append({
            **view,
            "keypoints": [
                {"index": index, "x": float(keypoint.pt[0]), "y": float(keypoint.pt[1])}
                for index, keypoint in enumerate(keypoints)
            ],
        })

    payload = {
        "version": "reference-b-keypoints-v1",
        "resolution": [width, height],
        "repair_c_excluded": True,
        "views": view_payloads,
    }
    (work / "keypoints.json").write_text(
        json.dumps(payload, indent=2) + "\n", encoding="utf-8"
    )
    return metadata, extracted


def collect_candidates(work: Path, extracted):
    hits = json.loads((work / "surface_hits.json").read_text(encoding="utf-8"))
    candidates = []
    for view_index, view in enumerate(hits["views"]):
        keypoints, descriptors = extracted[view["id"]]
        for item in view["hits"]:
            index = int(item["index"])
            point = np.asarray(item["canonical"], dtype=np.float32)
            keypoint = keypoints[index]
            candidates.append({
                "point": point,
                "descriptor": descriptors[index].copy(),
                "response": float(keypoint.response),
                "view": view_index,
            })
    if not candidates:
        raise RuntimeError("No ORB keypoint ray hit the surface of reference b")
    return candidates


def select_records(candidates, maximum: int):
    # Exact duplicates add matching ambiguity without adding viewpoint coverage.
    exact = set()
    unique = []
    for candidate in sorted(candidates, key=lambda item: item["response"], reverse=True):
        voxel = tuple(np.floor(candidate["point"] / 0.004).astype(np.int32))
        key = (voxel, bytes(candidate["descriptor"]))
        if key in exact:
            continue
        exact.add(key)
        candidate["cell"] = (
            int(np.clip((candidate["point"][0] + 0.30) / 0.60 * 6, 0, 5)),
            int(np.clip((candidate["point"][1] + 1.24) / 1.28 * 14, 0, 13)),
            int(np.clip((candidate["point"][2] + 0.40) / 0.80 * 6, 0, 5)),
        )
        unique.append(candidate)

    buckets = {}
    for candidate in unique:
        # Keep viewpoint variants, while round-robin selection prevents the front
        # label or one rendered distance from consuming the whole database.
        key = (candidate["cell"], candidate["view"] % 12)
        buckets.setdefault(key, []).append(candidate)
    for values in buckets.values():
        values.sort(key=lambda item: item["response"], reverse=True)

    selected = []
    keys = sorted(buckets)
    while len(selected) < maximum:
        added = False
        for key in keys:
            values = buckets[key]
            if values and len(selected) < maximum:
                selected.append(values.pop(0))
                added = True
        if not added:
            break
    return selected


def write_database(path: Path, records) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as handle:
        handle.write(MAGIC)
        handle.write(struct.pack("<I", len(records)))
        for record in records:
            handle.write(struct.pack("<3f", *record["point"]))
            handle.write(np.asarray(record["descriptor"], np.uint8).tobytes())


def make_contact_sheet(work: Path, metadata: dict) -> None:
    samples = metadata["views"][::6]
    thumbs = []
    for view in samples:
        image = cv2.imread(str(work / view["image"]), cv2.IMREAD_UNCHANGED)
        background = np.full((image.shape[0], image.shape[1], 3), 235, np.uint8)
        alpha = image[:, :, 3:4].astype(np.float32) / 255.0
        rgb = (image[:, :, :3] * alpha + background * (1.0 - alpha)).astype(np.uint8)
        thumbs.append(cv2.resize(rgb, (180, 240), interpolation=cv2.INTER_AREA))
    rows = [np.hstack(thumbs[index:index + 4]) for index in range(0, len(thumbs), 4)]
    cv2.imwrite(str(work / "reference_b_render_contact_sheet.png"), np.vstack(rows))


def main() -> None:
    args = parse_args()
    root = Path(__file__).resolve().parents[2]
    blender_script = Path(__file__).with_name("render_reference_b_orb_views.py")
    if not args.blender.is_file():
        raise SystemExit(f"Blender not found: {args.blender}")
    if not args.blend.is_file():
        raise SystemExit(f"Registered b+c Blender file not found: {args.blend}")
    if args.rerender and args.work.exists():
        shutil.rmtree(args.work)
    args.work.mkdir(parents=True, exist_ok=True)

    if not (args.work / "views.json").is_file():
        run_blender(args.blender, args.blend, blender_script, [
            "--mode", "render", "--output", str(args.work)
        ])
    metadata, extracted = detect_features(args.work)
    run_blender(args.blender, args.blend, blender_script, [
        "--mode", "raycast", "--output", str(args.work),
        "--keypoints", str(args.work / "keypoints.json"),
    ])
    candidates = collect_candidates(args.work, extracted)
    records = select_records(candidates, args.max_records)
    if len(records) < 1000:
        raise RuntimeError(f"Reference b produced too few usable records: {len(records)}")
    write_database(args.output, records)
    make_contact_sheet(args.work, metadata)

    points = np.asarray([record["point"] for record in records])
    manifest = {
        "version": VERSION,
        "logic": "camera image of real bottle a matches natural features rendered from no-cap model b; solvePnP estimates b; registered child c alone is rendered",
        "database": str(args.output.relative_to(root)),
        "database_sha256": sha256(args.output),
        "records": len(records),
        "candidate_surface_hits": len(candidates),
        "canonical_bounds_min": points.min(axis=0).tolist(),
        "canonical_bounds_max": points.max(axis=0).tolist(),
        "registered_blend": str(args.blend),
        "registered_blend_sha256": sha256(args.blend),
        "render_views": len(metadata["views"]),
        "reference_objects": metadata["reference_objects"],
        "repair_c_excluded_from_matching": True,
        "meters_per_model_unit": 0.17,
        "generator": str(Path(__file__).relative_to(root)),
    }
    manifest_path = args.output.with_name(args.output.stem + "_manifest.json")
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
