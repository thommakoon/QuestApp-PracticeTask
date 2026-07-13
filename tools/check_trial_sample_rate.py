#!/usr/bin/env python3
"""Check fixed-rate trial logging from Quest PracticeTask JSON files."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np


def load_recording(path: Path) -> tuple[dict | None, list[dict]]:
    raw = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(raw, list):
        return None, raw
    if isinstance(raw, dict) and "data" in raw:
        return raw, raw["data"]
    raise ValueError(f"Unrecognized JSON shape in {path}")


def summarize_file(path: Path) -> None:
    envelope, frames = load_recording(path)
    if not frames:
        print(f"{path.name}: no frames")
        return

    t_ms = np.array([f["unixTimeMilliseconds"] for f in frames], dtype=np.float64)
    t_unity = np.array([f["timestamp"] for f in frames], dtype=np.float64)
    dt_ms = np.diff(t_ms)
    dt_unity = np.diff(t_unity)

    nominal_hz = envelope.get("log_sample_rate_hz") if envelope else None
    expected_ms = 1000.0 / nominal_hz if nominal_hz else None

    seq = [f.get("sample_seq", i) for i, f in enumerate(frames)]
    seq_ok = all(seq[i] == i for i in range(len(seq)))

    duration_s = (t_ms[-1] - t_ms[0]) / 1000.0 if len(t_ms) > 1 else 0.0
    observed_hz = (len(frames) - 1) / duration_s if duration_s > 0 else float("nan")

    print(f"\n=== {path.name} ===")
    if envelope:
        print(f"sub_num: {envelope.get('sub_num')}")
        print(f"subsub_num: {envelope.get('subsub_num')}")
        print(f"log_sample_rate_hz: {nominal_hz}")
    print(f"frames: {len(frames)}")
    print(f"duration (wall ms): {duration_s:.3f} s")
    print(f"observed rate (frames/duration): {observed_hz:.2f} Hz")
    print(f"sample_seq contiguous 0..N-1: {seq_ok}")

    print("dt from unixTimeMilliseconds [ms]:")
    print(f"  median: {np.median(dt_ms):.3f}")
    print(f"  mean:   {np.mean(dt_ms):.3f}")
    print(f"  std:    {np.std(dt_ms):.3f}")
    print(f"  min:    {np.min(dt_ms):.3f}")
    print(f"  max:    {np.max(dt_ms):.3f}")

    print("dt from Unity timestamp [ms]:")
    print(f"  median: {np.median(dt_unity) * 1000.0:.3f}")
    print(f"  mean:   {np.mean(dt_unity) * 1000.0:.3f}")

    if expected_ms is not None:
        err = np.median(dt_ms) - expected_ms
        print(f"expected dt: {expected_ms:.3f} ms")
        print(f"median error: {err:+.3f} ms")
        ok = abs(err) <= 2.0
        print(f"pass (|median error| <= 2 ms): {ok}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "path",
        nargs="?",
        default="practice_json",
        help="JSON file or directory (default: practice_json)",
    )
    args = parser.parse_args()
    root = Path(args.path)

    if root.is_file():
        paths = [root]
    elif root.is_dir():
        paths = sorted(root.glob("*.json"))
        if not paths:
            print(f"No .json files in {root}", file=sys.stderr)
            return 1
    else:
        print(f"Path not found: {root}", file=sys.stderr)
        return 1

    for path in paths:
        summarize_file(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
