#!/usr/bin/env python3
"""Test the unchanged thresholds and global/guided ABI packaged by v42."""

from __future__ import annotations

import hashlib
import json
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PLUGIN = ROOT / "Assets/Plugins/Android/arm64-v8a/libUrpOrbNative.so"
SOURCE = ROOT / "Native/UrpOrbNative/src/urp_orb_native.cpp"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main() -> None:
    data = PLUGIN.read_bytes()
    if data[:4] != b"\x7fELF" or data[4] != 2 or data[5] != 1:
        raise ValueError("Native plugin is not a little-endian ELF64 binary")
    machine = struct.unpack_from("<H", data, 18)[0]
    if machine != 183:
        raise ValueError(f"Native plugin is not AArch64: e_machine={machine}")
    program_header_offset = struct.unpack_from("<Q", data, 32)[0]
    program_header_size = struct.unpack_from("<H", data, 54)[0]
    program_header_count = struct.unpack_from("<H", data, 56)[0]
    load_segments: list[dict[str, int]] = []
    for index in range(program_header_count):
        offset = program_header_offset + index * program_header_size
        segment_type = struct.unpack_from("<I", data, offset)[0]
        if segment_type != 1:
            continue
        file_offset = struct.unpack_from("<Q", data, offset + 8)[0]
        virtual_address = struct.unpack_from("<Q", data, offset + 16)[0]
        alignment = struct.unpack_from("<Q", data, offset + 48)[0]
        if alignment < 0x4000:
            raise ValueError(f"LOAD segment {index} is not 16 KB aligned")
        if file_offset % alignment != virtual_address % alignment:
            raise ValueError(f"LOAD segment {index} offset/vaddr alignment differs")
        load_segments.append(
            {
                "index": index,
                "offset": file_offset,
                "virtual_address": virtual_address,
                "alignment": alignment,
            }
        )
    if not load_segments:
        raise ValueError("Native plugin has no LOAD segments")

    expected_version = b"urp-orb-native-2026.08.13-r10-global-first-acquisition"
    if expected_version not in data:
        raise ValueError("Native plugin does not contain the required diagnostic ABI")
    source = SOURCE.read_text(encoding="utf-8")
    required = (
        "SetPosePrior",
        "solvePnPRansac",
        "SOLVEPNP_SQPNP",
        "guidedMatches",
        "const PoseSolution strictSolution",
        "if (hasUsablePrior",
        "PoseSolution chosen = strictSolution",
        "SampleReferenceHsv",
        "urp_orb_get_last_inliers",
    )
    missing = [token for token in required if token not in source]
    if missing:
        raise ValueError(f"Native source contract is missing: {missing}")
    prohibited = ("BottleCapC", "repairAnchor", "set_repair_anchor")
    found = [token for token in prohibited if token in source]
    if found:
        raise ValueError(f"Native tracker contains prohibited C logic: {found}")
    unchanged_thresholds = (
        "chosen.reprojectionError > 3.0f",
        "chosen.reprojectionMax > 8.0f",
        "chosen.coverageX < (locallyGuided ? 0.035f : 0.05f)",
        "chosen.coverageY < (locallyGuided ? 0.10f : 0.16f)",
    )
    if any(token not in source for token in unchanged_thresholds):
        raise ValueError("v42 changed a native PnP/spatial-quality threshold")

    print(
        json.dumps(
            {
                "status": "URP_NATIVE_RUNTIME_CONTRACT_OK",
                "architecture": "AArch64",
                "sha256": sha256(PLUGIN),
                "build_version": expected_version.decode("ascii"),
                "tracking_algorithm_changed": False,
                "load_segments": load_segments,
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
