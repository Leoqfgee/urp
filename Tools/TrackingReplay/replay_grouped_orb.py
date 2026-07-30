#!/usr/bin/env python3
"""Offline replay for the URP3DM2 grouped multi-view ORB database."""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

import cv2
import numpy as np

import replay_orb


def load_grouped(path: Path) -> list[tuple[int, np.ndarray, np.ndarray]]:
    data = path.read_bytes()
    if data[:8] != b"URP3DM2\0":
        raise ValueError("Expected URP3DM2 grouped model")
    total, group_count = struct.unpack_from("<II", data, 8)
    offset = 16
    groups = []
    parsed = 0
    for _ in range(group_count):
        group_id, count = struct.unpack_from("<II", data, offset)
        offset += 8
        points = np.empty((count, 3), np.float32)
        descriptors = np.empty((count, 32), np.uint8)
        for index in range(count):
            points[index] = struct.unpack_from("<3f", data, offset)
            offset += 12
            descriptors[index] = np.frombuffer(
                data,
                np.uint8,
                32,
                offset,
            ).copy()
            offset += 32
        parsed += count
        groups.append((group_id, points, descriptors))
    if parsed != total or offset != len(data):
        raise ValueError("Invalid grouped model record count")
    return groups


def quality(result: replay_orb.Result) -> tuple:
    return (
        int(result.accepted),
        result.pose_inliers,
        result.inlier_ratio,
        -result.reprojection_rms,
        result.unique_matches,
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--frames", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--frame-modulo", type=int, default=1)
    parser.add_argument("--frame-residue", type=int, default=0)
    parser.add_argument("--step", type=int, default=1)
    parser.add_argument("--ratio", type=float, default=0.72)
    parser.add_argument("--minimum-matches", type=int, default=8)
    parser.add_argument("--shortlist-groups", type=int, default=24)
    parser.add_argument("--coarse-descriptors", type=int, default=40)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    groups = load_grouped(args.model)
    coarse_rows = []
    coarse_group_indices = []
    for group_index, (_, _, descriptors) in enumerate(groups):
        count = min(args.coarse_descriptors, len(descriptors))
        if count <= 0:
            continue
        indices = np.linspace(
            0,
            len(descriptors) - 1,
            count,
            dtype=np.int32,
        )
        coarse_rows.append(descriptors[indices])
        coarse_group_indices.extend([group_index] * count)
    coarse_descriptors = np.concatenate(coarse_rows)
    coarse_group_indices = np.asarray(coarse_group_indices, np.int32)
    matcher = cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=False)
    frames = [
        path
        for index, path in enumerate(
            sorted(
                path
                for path in args.frames.rglob("*")
                if path.suffix.lower() in {".jpg", ".jpeg", ".png"}
            )
        )
        if index % max(1, args.frame_modulo) == args.frame_residue
    ][::max(1, args.step)]
    summaries = []
    for frame in frames:
        best = None
        best_group = None
        prepared = replay_orb.prepare_frame(frame)
        frame_descriptors = prepared[3]
        selected_group_indices = list(range(len(groups)))
        if (
            frame_descriptors is not None
            and args.shortlist_groups > 0
            and args.shortlist_groups < len(groups)
        ):
            pairs = matcher.knnMatch(
                coarse_descriptors,
                frame_descriptors,
                k=2,
            )
            scores = np.zeros(len(groups), np.float32)
            for row, pair in enumerate(pairs):
                if (
                    len(pair) >= 2
                    and pair[0].distance < max(args.ratio, 0.80) * pair[1].distance
                ):
                    scores[coarse_group_indices[row]] += (
                        1.0 + (256.0 - pair[0].distance) / 256.0
                    )
            selected_group_indices = sorted(
                range(len(groups)),
                key=lambda index: float(scores[index]),
                reverse=True,
            )[: args.shortlist_groups]
        for group_index in selected_group_indices:
            group_id, points, descriptors = groups[group_index]
            result = replay_orb.replay(
                frame,
                points,
                descriptors,
                args.ratio,
                args.minimum_matches,
                None,
                prepared,
            )
            if best is None or quality(result) > quality(best):
                best = result
                best_group = group_id
        summaries.append(
            {
                "image": str(frame),
                "accepted": best.accepted,
                "group": best_group,
                "matches": best.unique_matches,
                "inliers": best.pose_inliers,
                "inlierRatio": best.inlier_ratio,
                "rms": best.reprojection_rms,
                "rejection": best.rejection,
            }
        )
    output = {
        "frames": len(summaries),
        "accepted": sum(item["accepted"] for item in summaries),
        "successRate": (
            sum(item["accepted"] for item in summaries) / max(1, len(summaries))
        ),
        "groups": len(groups),
        "shortlistGroups": min(args.shortlist_groups, len(groups)),
        "results": summaries,
    }
    (args.output / "summary.json").write_text(
        json.dumps(output, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(
        json.dumps(
            {
                key: output[key]
                for key in ("frames", "accepted", "successRate", "groups")
            },
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
