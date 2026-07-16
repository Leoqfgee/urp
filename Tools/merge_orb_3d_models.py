#!/usr/bin/env python3
"""Merge view-specific descriptor-to-3D maps into one shared-frame ORB model."""

from __future__ import annotations

import argparse
import struct
from pathlib import Path

import numpy as np


MAGIC = b"URP3DM1\0"
RECORD_SIZE = 44


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--max-points", type=int, default=5000)
    parser.add_argument("--voxel-size", type=float, default=0.008)
    return parser.parse_args()


def read_model(path: Path) -> tuple[np.ndarray, np.ndarray]:
    data = path.read_bytes()
    if data[:8] != MAGIC:
        raise ValueError(f"{path}: invalid magic")
    count = struct.unpack_from("<I", data, 8)[0]
    if len(data) != 12 + count * RECORD_SIZE:
        raise ValueError(f"{path}: invalid record length")
    points = np.empty((count, 3), dtype=np.float32)
    descriptors = np.empty((count, 32), dtype=np.uint8)
    for index in range(count):
        offset = 12 + index * RECORD_SIZE
        points[index] = struct.unpack_from("<fff", data, offset)
        descriptors[index] = np.frombuffer(data, dtype=np.uint8, count=32, offset=offset + 12)
    return points, descriptors


def write_model(path: Path, points: np.ndarray, descriptors: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as output:
        output.write(MAGIC)
        output.write(struct.pack("<I", len(points)))
        for point, descriptor in zip(points, descriptors):
            output.write(struct.pack("<fff", *point))
            output.write(descriptor.tobytes())


def main() -> None:
    args = parse_args()
    paths = sorted(args.input_dir.glob("bottle_view_*.bytes"))
    paths = [path for path in paths if path.resolve() != args.output.resolve()]
    if not paths:
        raise SystemExit("no view models found")
    point_sets, descriptor_sets = zip(*(read_model(path) for path in paths))
    points = np.concatenate(point_sets)
    descriptors = np.concatenate(descriptor_sets)

    selected = {}
    for index, (point, descriptor) in enumerate(zip(points, descriptors)):
        voxel = tuple(np.floor(point / args.voxel_size).astype(np.int64))
        descriptor_key = bytes(descriptor)
        key = (voxel, descriptor_key)
        if key not in selected:
            selected[key] = index
    indices = np.fromiter(selected.values(), dtype=np.int64)
    if len(indices) > args.max_points:
        grid = np.floor(points[indices] / (args.voxel_size * 2)).astype(np.int64)
        order = np.lexsort((grid[:, 2], grid[:, 1], grid[:, 0]))
        indices = indices[order[np.linspace(0, len(order) - 1, args.max_points).astype(int)]]

    write_model(args.output, points[indices], descriptors[indices])
    print(f"merged {sum(map(len, point_sets))} records from {len(paths)} views "
          f"into {len(indices)} records: {args.output}")


if __name__ == "__main__":
    main()
